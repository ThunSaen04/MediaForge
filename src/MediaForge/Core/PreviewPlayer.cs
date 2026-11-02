using NAudio.Wave;

namespace MediaForge.Core;

/// <summary>
/// Plays an <see cref="AudioPreview"/> through a list of ranges back to back, which is how the
/// trim page previews the result (skipping cut parts) or a single join. Position is reported in
/// source time, following what the sound card has actually played rather than what was buffered.
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    private WaveOut? _out;
    private PlanProvider? _provider;

    public bool IsPlaying => _out is not null;

    /// <summary>Raised on the UI thread when playback reaches the end of its plan.</summary>
    public event Action? Finished;

    /// <summary>Current playback position in source seconds, or null when stopped.</summary>
    public double? Position
    {
        get
        {
            if (_out is null || _provider is null) return null;
            var played = _out.GetPosition() / AudioPreview.BytesPerFrame;
            return _provider.SourceFrameAt(played) / (double)AudioPreview.SampleRate;
        }
    }

    public void Play(AudioPreview source, IReadOnlyList<TimeRange> plan)
    {
        Stop();
        if (plan.Count == 0) return;
        var provider = new PlanProvider(source, plan);
        var output = new WaveOut { BufferMilliseconds = 60, NumberOfBuffers = 3 };
        output.PlaybackStopped += (_, _) =>
        {
            // Only the current output may report the end; Stop() already cleaned up older ones.
            if (!ReferenceEquals(output, _out)) return;
            Stop();
            Finished?.Invoke();
        };
        output.Init(provider);
        _out = output;
        _provider = provider;
        output.Play();
    }

    public void Stop()
    {
        var output = _out;
        var provider = _provider;
        _out = null;
        _provider = null;
        if (output is not null)
        {
            try { output.Stop(); } catch { /* device already gone */ }
            output.Dispose();
        }
        provider?.Dispose();
    }

    public void Dispose() => Stop();

    /// <summary>Streams the PCM file range by range and remembers where each jump lands in the output.</summary>
    private sealed class PlanProvider : IWaveProvider, IDisposable
    {
        private readonly AudioPreview _source;
        private readonly FileStream _file;
        private readonly List<(long Start, long End)> _plan;
        private readonly Lock _gate = new();
        // Output frame where each stretch of contiguous source audio starts, with its source frame.
        private readonly List<(long Out, long Src, long SrcEnd)> _marks = [];
        private int _index;
        private long _frame;
        private long _written;

        public PlanProvider(AudioPreview source, IReadOnlyList<TimeRange> plan)
        {
            _source = source;
            _file = new FileStream(source.PcmPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _plan = plan.Select(r => ((long)(r.Start * AudioPreview.SampleRate), (long)(r.End * AudioPreview.SampleRate)))
                        .Where(r => r.Item2 > r.Item1)
                        .ToList();
            if (_plan.Count > 0) Jump(0);
        }

        public WaveFormat WaveFormat { get; } = new(AudioPreview.SampleRate, 16, AudioPreview.Channels);

        public int Read(Span<byte> buffer)
        {
            var frames = buffer.Length / AudioPreview.BytesPerFrame;
            var done = 0;
            while (done < frames && _index < _plan.Count)
            {
                var end = Math.Min(_plan[_index].End, _source.IsComplete ? _source.DecodedFrames : long.MaxValue);
                if (_frame >= end)
                {
                    if (_index + 1 >= _plan.Count)
                    {
                        _index = _plan.Count;
                        break;
                    }
                    Jump(_index + 1);
                    continue;
                }

                var available = _source.DecodedFrames - _frame;
                if (available <= 0)
                {
                    // Decoding has not reached this point yet: play silence and hold the position.
                    var gap = frames - done;
                    buffer.Slice(done * AudioPreview.BytesPerFrame, gap * AudioPreview.BytesPerFrame).Clear();
                    done += gap;
                    _written += gap;
                    lock (_gate) _marks.Add((_written, _frame, _plan[_index].End));
                    break;
                }

                var n = (int)Math.Min(Math.Min(frames - done, end - _frame), available);
                var bytes = buffer.Slice(done * AudioPreview.BytesPerFrame, n * AudioPreview.BytesPerFrame);
                int got;
                try
                {
                    _file.Position = _frame * AudioPreview.BytesPerFrame;
                    got = _file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) / AudioPreview.BytesPerFrame;
                }
                catch (ObjectDisposedException)
                {
                    break; // stopped while the device thread was still reading
                }
                if (got == 0) break;
                done += got;
                _frame += got;
                _written += got;
            }
            return done * AudioPreview.BytesPerFrame;
        }

        public long SourceFrameAt(long played)
        {
            lock (_gate)
            {
                for (var i = _marks.Count - 1; i >= 0; i--)
                {
                    var m = _marks[i];
                    if (m.Out <= played) return Math.Min(m.Src + (played - m.Out), m.SrcEnd);
                }
                return _marks.Count > 0 ? _marks[0].Src : 0;
            }
        }

        private void Jump(int index)
        {
            _index = index;
            _frame = _plan[index].Start;
            lock (_gate) _marks.Add((_written, _frame, _plan[index].End));
        }

        public void Dispose() => _file.Dispose();
    }
}
