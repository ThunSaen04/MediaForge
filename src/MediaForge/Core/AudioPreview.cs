using System.Diagnostics;

namespace MediaForge.Core;

/// <summary>
/// Decodes a file's first audio track once with ffmpeg into a temporary 16-bit stereo PCM file
/// for playback, and builds waveform peaks from the same pass. Both grow while decoding runs,
/// so the timeline fills in and playback can start before the decode finishes.
/// </summary>
public sealed class AudioPreview : IDisposable
{
    public const int SampleRate = 44100;
    public const int Channels = 2;
    public const int BytesPerFrame = Channels * 2;

    /// <summary>One waveform peak per 10 ms.</summary>
    public const int FramesPerPeak = SampleRate / 100;

    private static readonly Lock LiveGate = new();
    private static readonly HashSet<AudioPreview> Live = [];
    private static bool _sweptStale;

    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _peakGate = new();
    private float[] _peaks;
    private int _peakCount;
    private long _decodedFrames;

    private AudioPreview(string pcmPath, double duration)
    {
        PcmPath = pcmPath;
        Duration = duration;
        _peaks = new float[Math.Max(1024, (int)(duration * SampleRate / FramesPerPeak) + 256)];
    }

    public string PcmPath { get; }

    /// <summary>Duration reported by ffprobe; the decoded length may differ slightly.</summary>
    public double Duration { get; }

    public long DecodedFrames => Interlocked.Read(ref _decodedFrames);

    public double DecodedSeconds => DecodedFrames / (double)SampleRate;

    public bool IsComplete { get; private set; }

    /// <summary>Set when ffmpeg failed (for example, the file has no audio track).</summary>
    public string? Error { get; private set; }

    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Raised on a worker thread as more audio is decoded, and once at the end.</summary>
    public event Action? Progress;

    public static AudioPreview Start(string input, double duration)
    {
        SweepStaleFiles();
        var path = Path.Combine(AppPaths.TempDir, $"preview_{Guid.NewGuid():N}.pcm");
        var preview = new AudioPreview(path, duration);
        lock (LiveGate) Live.Add(preview);
        preview.Completion = Task.Run(() => preview.DecodeAsync(input));
        return preview;
    }

    /// <summary>Largest absolute sample (0–1) in each 10 ms block from <paramref name="first"/> on.</summary>
    public int CopyPeaks(int first, Span<float> destination)
    {
        lock (_peakGate)
        {
            if (first >= _peakCount) return 0;
            var n = Math.Min(destination.Length, _peakCount - first);
            _peaks.AsSpan(first, n).CopyTo(destination);
            return n;
        }
    }

    public int PeakCount
    {
        get
        {
            lock (_peakGate) return _peakCount;
        }
    }

    private async Task DecodeAsync(string input)
    {
        var ct = _cts.Token;
        Process? process = null;
        try
        {
            var exe = Tools.Ffmpeg ?? throw new ToolMissingException("ffmpeg");
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in (string[])
                     ["-hide_banner", "-nostdin", "-loglevel", "error", "-i", input, "-map", "0:a:0", "-vn", "-sn", "-dn",
                      "-ac", "2", "-ar", $"{SampleRate}", "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1"])
                psi.ArgumentList.Add(arg);

            process = Process.Start(psi)!;
            var errTask = process.StandardError.ReadToEndAsync(ct);
            await using (var file = new FileStream(PcmPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await PumpAsync(process.StandardOutput.BaseStream, file, ct);
            }
            await process.WaitForExitAsync(ct);
            var err = await errTask;
            if (process.ExitCode != 0 || DecodedFrames == 0)
                Error = err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .LastOrDefault() ?? $"ffmpeg exit code {process.ExitCode}";
        }
        catch (OperationCanceledException)
        {
            // Disposed while decoding.
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already exited.
                }
                process.Dispose();
            }
            IsComplete = true;
            Progress?.Invoke();
        }
    }

    private async Task PumpAsync(Stream source, FileStream file, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var carry = 0;            // bytes of an unfinished frame kept at the start of buffer
        var blockFrames = 0;      // frames folded into the current peak so far
        var blockPeak = 0f;
        var lastNotify = Stopwatch.StartNew();

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(carry), ct);
            if (read == 0) break;
            var available = carry + read;
            var whole = available - available % BytesPerFrame;

            await file.WriteAsync(buffer.AsMemory(0, whole), ct);
            await file.FlushAsync(ct);

            for (var i = 0; i < whole; i += BytesPerFrame)
            {
                var l = Math.Abs((int)BitConverter.ToInt16(buffer, i));
                var r = Math.Abs((int)BitConverter.ToInt16(buffer, i + 2));
                var v = Math.Max(l, r) / 32768f;
                if (v > blockPeak) blockPeak = v;
                if (++blockFrames == FramesPerPeak)
                {
                    AddPeak(blockPeak);
                    blockFrames = 0;
                    blockPeak = 0;
                }
            }

            Interlocked.Add(ref _decodedFrames, whole / BytesPerFrame);
            carry = available - whole;
            if (carry > 0) Buffer.BlockCopy(buffer, whole, buffer, 0, carry);

            if (lastNotify.ElapsedMilliseconds >= 150)
            {
                lastNotify.Restart();
                Progress?.Invoke();
            }
        }
        if (blockFrames > 0) AddPeak(blockPeak);
    }

    private void AddPeak(float value)
    {
        lock (_peakGate)
        {
            if (_peakCount == _peaks.Length) Array.Resize(ref _peaks, _peaks.Length * 2);
            _peaks[_peakCount++] = value;
        }
    }

    public void Dispose()
    {
        lock (LiveGate)
            if (!Live.Remove(this)) return;
        _cts.Cancel();
        // The decode loop owns the file handle; delete once it lets go.
        _ = Completion.ContinueWith(_ =>
        {
            Tools.TryDelete(PcmPath);
            _cts.Dispose();
        }, TaskScheduler.Default);
    }

    /// <summary>Called on app exit so temporary PCM files do not pile up.</summary>
    public static void DisposeAll()
    {
        List<AudioPreview> all;
        lock (LiveGate) all = [.. Live];
        foreach (var p in all) p.Dispose();
        foreach (var p in all) p.Completion.Wait(TimeSpan.FromSeconds(2));
        foreach (var p in all) Tools.TryDelete(p.PcmPath);
    }

    /// <summary>Removes previews left behind by a crash. Files still open by another instance stay put.</summary>
    private static void SweepStaleFiles()
    {
        lock (LiveGate)
        {
            if (_sweptStale) return;
            _sweptStale = true;
        }
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.TempDir, "preview_*.pcm"))
                Tools.TryDelete(file);
        }
        catch
        {
            // Best effort.
        }
    }
}
