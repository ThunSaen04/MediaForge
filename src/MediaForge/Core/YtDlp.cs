using System.Globalization;
using System.Text.Json;
using MediaForge.Localization;

namespace MediaForge.Core;

public sealed class YtEntry : Observable
{
    public bool IsSelected { get; set => Set(ref field, value); } = true;
    /// <summary>Position in its playlist (1-based); 0 for a standalone video.</summary>
    public int Index { get; init; }
    public string IndexText => Index > 0 ? Index.ToString() : "";
    public required string Url { get; init; }
    public required string Title { get; init; }
    public double Duration { get; init; }
    public string? PlaylistTitle { get; init; }
    public string DurationText => Duration > 0 ? Fmt.Duration(Duration) : "";
}

public sealed record YtOptions(
    bool AudioOnly,
    int MaxHeight,
    string Container,
    string AudioFormat,
    string AudioQuality,
    bool EmbedThumbnail,
    bool EmbedMetadata,
    bool EmbedSubtitles,
    string SubtitleLanguages,
    bool NumberPrefix);

public static class YtDlp
{
    private static readonly Dictionary<string, string> Env = new()
    {
        // Force UTF-8 on the pipes so Thai titles and file names survive.
        ["PYTHONIOENCODING"] = "utf-8",
        ["PYTHONUTF8"] = "1",
    };

    private static List<string> BaseArgs()
    {
        List<string> args = ["--ignore-config", "--no-warnings", .. Tools.JsRuntimeArgs()];
        if (Tools.Ffmpeg is { } ffmpeg) args.AddRange(["--ffmpeg-location", Path.GetDirectoryName(ffmpeg)!]);
        return args;
    }

    /// <summary>Resolves a URL into downloadable entries (one for a video, many for a playlist).</summary>
    public static async Task<List<YtEntry>> AnalyzeAsync(string url, CancellationToken ct)
    {
        var exe = Tools.YtDlp ?? throw new ToolMissingException("yt-dlp");
        var args = BaseArgs();
        args.AddRange(["-J", "--flat-playlist", url]);

        var result = await ProcessRunner.RunAsync(exe, args, ct: ct, env: Env, captureOutput: true);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            throw new UserException(Loc.F("Err_Analyze", url, ErrorText(result.ErrorTail)));

        using var doc = JsonDocument.Parse(result.Output);
        var root = doc.RootElement;
        var entries = new List<YtEntry>();

        if (root.Str("_type") == "playlist" && root.TryGetProperty("entries", out var items))
        {
            var playlist = root.Str("title") ?? "Playlist";
            var index = 0;
            foreach (var e in items.EnumerateArray())
            {
                index++;
                var title = e.Str("title") ?? e.Str("id") ?? $"#{index}";
                var entryUrl = e.Str("url") ?? e.Str("webpage_url");
                if (entryUrl is null && e.Str("id") is { } id) entryUrl = $"https://www.youtube.com/watch?v={id}";
                if (entryUrl is null) continue;
                entries.Add(new YtEntry
                {
                    Index = index,
                    Url = entryUrl,
                    Title = title,
                    Duration = e.Dbl("duration"),
                    PlaylistTitle = playlist,
                    // Private/deleted items stay listed so numbering matches, but are not picked by default.
                    IsSelected = !(title.StartsWith("[Private", StringComparison.OrdinalIgnoreCase)
                                   || title.StartsWith("[Deleted", StringComparison.OrdinalIgnoreCase)),
                });
            }
        }
        else
        {
            entries.Add(new YtEntry
            {
                Url = root.Str("webpage_url") ?? url,
                Title = root.Str("title") ?? url,
                Duration = root.Dbl("duration"),
            });
        }
        return entries;
    }

    public static async Task DownloadAsync(YtEntry entry, YtOptions o, string outDir, Job job, CancellationToken ct)
    {
        var exe = Tools.YtDlp ?? throw new ToolMissingException("yt-dlp");
        Directory.CreateDirectory(outDir);

        var prefix = o.NumberPrefix && entry.Index > 0 ? $"{entry.Index:000} - " : "";
        var args = BaseArgs();
        args.AddRange(
        [
            "--no-playlist", "--newline", "--progress", "--no-mtime",
            "--progress-template",
            "download:[MF]%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s",
            "--progress-template", "postprocess:[MFPP]%(progress.postprocessor)s",
            "--print", "after_move:[MFFILE]%(filepath)s",
            "-P", outDir,
            "-o", prefix + "%(title)s.%(ext)s",
        ]);

        if (o.AudioOnly)
        {
            args.AddRange(["-f", "ba/b", "-x"]);
            if (o.AudioFormat != "best") args.AddRange(["--audio-format", o.AudioFormat]);
            if (o.AudioFormat is "mp3" or "m4a" or "opus" or "vorbis") args.AddRange(["--audio-quality", o.AudioQuality]);
        }
        else
        {
            var h = o.MaxHeight;
            args.AddRange(["-f", h > 0 ? $"bv*[height<={h}]+ba/b[height<={h}]/bv*+ba/b" : "bv*+ba/b"]);
            var sort = new List<string> { h > 0 ? $"res:{h}" : "res" };
            // Within the chosen resolution, prefer codecs that the container plays everywhere.
            if (o.Container == "mp4") sort.AddRange(["vcodec:h264", "acodec:aac"]);
            else if (o.Container == "webm") sort.AddRange(["vcodec:vp9", "acodec:opus"]);
            args.AddRange(["-S", string.Join(',', sort), "--merge-output-format", o.Container]);
            if (o.EmbedSubtitles)
                args.AddRange(["--write-subs", "--sub-langs", o.SubtitleLanguages, "--embed-subs"]);
        }

        if (o.EmbedThumbnail && !(o.AudioOnly && o.AudioFormat == "wav"))
            args.AddRange(["--embed-thumbnail", "--convert-thumbnails", "jpg"]);
        if (o.EmbedMetadata) args.Add("--embed-metadata");
        args.Add(entry.Url);

        job.AppendLog("> yt-dlp " + string.Join(' ', args));
        job.IsIndeterminate = true;

        var part = 1;
        double lastPercent = 0;
        var result = await ProcessRunner.RunAsync(exe, args,
            onOutput: line =>
            {
                job.AppendLog(line);
                if (line.StartsWith("[MF]", StringComparison.Ordinal))
                {
                    var p = line[4..].Split('|');
                    if (p.Length < 5) return;
                    var done = ParseNumber(p[0]);
                    var total = ParseNumber(p[1]) ?? ParseNumber(p[2]);
                    var speed = ParseNumber(p[3]);
                    var eta = ParseNumber(p[4]);
                    if (done is null) return;
                    if (total is > 0)
                    {
                        var percent = Math.Clamp(done.Value / total.Value * 100, 0, 100);
                        // Video and audio streams download one after another; a reset means the next part.
                        if (percent + 20 < lastPercent) part++;
                        lastPercent = percent;
                        job.IsIndeterminate = false;
                        job.Progress = percent;
                    }
                    var text = total is > 0 ? $"{Fmt.Bytes(done.Value)} / {Fmt.Bytes(total.Value)}" : Fmt.Bytes(done.Value);
                    if (speed is > 0) text += $" · {Fmt.Bytes(speed.Value)}/s";
                    if (eta is >= 0) text += $" · {Loc.F("Info_Eta", Fmt.Duration(eta.Value))}";
                    if (part > 1) text += $" · {Loc.F("Info_Part", part)}";
                    job.Info = text;
                }
                else if (line.StartsWith("[MFPP]", StringComparison.Ordinal))
                {
                    job.IsIndeterminate = true;
                    job.Info = Loc.F("Info_PostProcess", line[6..].Trim());
                }
                else if (line.StartsWith("[MFFILE]", StringComparison.Ordinal))
                {
                    job.OutputPath = line[8..].Trim();
                }
            },
            onError: job.AppendLog,
            ct: ct,
            env: Env);

        if (result.ExitCode != 0)
            throw new UserException(ErrorText(result.ErrorTail));
    }

    public static async Task<string> UpdateAsync(CancellationToken ct)
    {
        var exe = Tools.YtDlp ?? throw new ToolMissingException("yt-dlp");
        var result = await ProcessRunner.RunAsync(exe, ["-U"], ct: ct, env: Env, captureOutput: true);
        var text = string.Join(Environment.NewLine, result.Output.Trim(), result.ErrorTail.Trim()).Trim();
        if (result.ExitCode != 0) throw new UserException(text);
        return text;
    }

    private static double? ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string ErrorText(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errors = lines.Where(l => l.StartsWith("ERROR:", StringComparison.Ordinal)).ToList();
        return errors.Count > 0 ? string.Join(Environment.NewLine, errors) : lines.LastOrDefault() ?? "yt-dlp failed";
    }
}
