using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace MediaForge.Core;

public readonly record struct DownloadProgress(long Done, long? Total);

public enum HwEncoder { Cpu, Nvidia, Intel, Amd }

/// <summary>Locates and installs the external tools (ffmpeg, ffprobe, yt-dlp, deno).</summary>
public static class Tools
{
    public const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    public const string FfmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    public const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    internal static readonly HttpClient Http = CreateHttpClient();

    public static string? Ffmpeg => Find(AppSettings.Current.FfmpegPath, "ffmpeg.exe");

    public static string? Ffprobe
    {
        get
        {
            // Prefer the ffprobe that ships next to the ffmpeg we use, so versions match.
            if (Ffmpeg is { } ffmpeg)
            {
                var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
                if (File.Exists(sibling)) return sibling;
            }
            return Find("", "ffprobe.exe");
        }
    }

    public static string? YtDlp => Find(AppSettings.Current.YtDlpPath, "yt-dlp.exe");
    public static string? Deno => Find("", "deno.exe");
    public static string? Node => FindOnPath("node.exe");

    private static string? Find(string custom, string exe)
    {
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
        string[] dirs = [AppPaths.ToolsDir, AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools")];
        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return FindOnPath(exe);
    }

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }
        return null;
    }

    /// <summary>Arguments that tell yt-dlp which JavaScript runtime to use for YouTube's player challenges.</summary>
    public static IEnumerable<string> JsRuntimeArgs()
    {
        var mode = AppSettings.Current.JsRuntime;
        if (mode == "none") yield break;
        if (mode is "auto" or "deno" && Deno is { } deno)
        {
            yield return "--js-runtimes";
            yield return $"deno:{deno}";
        }
        else if (mode is "auto" or "node" && Node is { } node)
        {
            yield return "--js-runtimes";
            yield return $"node:{node}";
        }
    }

    public static async Task<string?> VersionAsync(string? exe, params string[] args)
    {
        if (exe is null) return null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await ProcessRunner.RunAsync(exe, args, ct: cts.Token, captureOutput: true);
            var first = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return first ?? result.ErrorTail.Split('\n').FirstOrDefault()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Probes which GPU H.264 encoders actually work on this machine.</summary>
    public static async Task<IReadOnlyList<HwEncoder>> DetectHardwareEncodersAsync()
    {
        if (Ffmpeg is not { } ffmpeg) return [];
        (HwEncoder Kind, string Codec)[] candidates =
            [(HwEncoder.Nvidia, "h264_nvenc"), (HwEncoder.Intel, "h264_qsv"), (HwEncoder.Amd, "h264_amf")];

        var checks = candidates.Select(async c =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await ProcessRunner.RunAsync(ffmpeg,
                    ["-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=black:s=256x256:d=0.1",
                     "-frames:v", "1", "-c:v", c.Codec, "-f", "null", "-"], ct: cts.Token);
                return result.ExitCode == 0 ? c.Kind : (HwEncoder?)null;
            }
            catch
            {
                return null;
            }
        });
        var results = await Task.WhenAll(checks);
        return results.OfType<HwEncoder>().ToList();
    }

    public static Task InstallYtDlpAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.ToolsDir);
        return DownloadFileAsync(YtDlpUrl, Path.Combine(AppPaths.ToolsDir, "yt-dlp.exe"), progress, ct);
    }

    public static async Task InstallFfmpegAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var zip = Path.Combine(AppPaths.TempDir, $"ffmpeg_{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(FfmpegUrl, zip, progress, ct);
            ExtractExecutables(zip, "ffmpeg.exe", "ffprobe.exe");
        }
        finally
        {
            TryDelete(zip);
        }
    }

    public static async Task InstallDenoAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var zip = Path.Combine(AppPaths.TempDir, $"deno_{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(DenoUrl, zip, progress, ct);
            ExtractExecutables(zip, "deno.exe");
        }
        finally
        {
            TryDelete(zip);
        }
    }

    private static void ExtractExecutables(string zipPath, params string[] names)
    {
        Directory.CreateDirectory(AppPaths.ToolsDir);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var name in names)
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"{name} not found in archive");
            entry.ExtractToFile(Path.Combine(AppPaths.ToolsDir, name), overwrite: true);
        }
    }

    internal static async Task DownloadFileAsync(string url, string destination, IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        var partial = destination + ".part";

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[128 * 1024];
                long done = 0;
                var clock = Stopwatch.StartNew();
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (clock.ElapsedMilliseconds >= 100)
                    {
                        progress?.Report(new DownloadProgress(done, total));
                        clock.Restart();
                    }
                }
                progress?.Report(new DownloadProgress(done, total));
            }
            File.Move(partial, destination, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MediaForge", AppInfo.Version));
        return client;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}
