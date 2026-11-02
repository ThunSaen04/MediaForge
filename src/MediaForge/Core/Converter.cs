using System.Globalization;
using System.Text;
using System.Text.Json;
using MediaForge.Localization;

namespace MediaForge.Core;

/// <summary>Builds ffmpeg command lines for every operation the app offers and runs them.</summary>
public static class Converter
{
    /// <summary>Rounds frame size to even numbers, which 4:2:0 encoders require.</summary>
    private const string EvenScale = "scale='trunc(iw/2)*2':'trunc(ih/2)*2'";

    // ───────────────────────── Convert ─────────────────────────

    public static async Task ToAudioAsync(string input, string outDir, AudioOptions o, Job job, CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var audio = info.Audio ?? throw new UserException(Loc.T("Err_NoAudio"));
        var ext = o.Format.IsCopy ? CopyExtension(audio.Codec) : o.Format.Ext;
        var output = OutputNamer.Reserve(outDir, BaseName(input), ext);

        List<string> a = ["-i", input, "-map", $"0:{audio.Index}"];
        MapCoverOrDropVideo(a, info, ext);
        a.AddRange(["-sn", "-dn"]);

        if (o.Format.IsCopy)
        {
            a.AddRange(["-c:a", "copy"]);
        }
        else
        {
            a.AddRange(["-c:a", o.Format.Codec]);
            if (o.Format.Lossy) a.AddRange(["-b:a", $"{o.Bitrate}k"]);
            if (o.SampleRate > 0) a.AddRange(["-ar", o.SampleRate.ToString(CultureInfo.InvariantCulture)]);
            var channels = o.Channels > 0 ? o.Channels : StereoIfNeeded(o.Format.Codec, audio.Channels);
            if (channels > 0) a.AddRange(["-ac", channels.ToString(CultureInfo.InvariantCulture)]);
        }
        if (ext == "mp3") a.AddRange(["-id3v2_version", "3"]);
        a.Add(output);

        await RunToFileAsync(job, output, a, info.Duration, ct);
    }

    public static async Task ToVideoAsync(string input, string outDir, VideoOptions o, Job job, CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var video = info.Video ?? throw new UserException(Loc.T("Err_NoVideo"));
        var f = o.Format;
        var output = OutputNamer.Reserve(outDir, BaseName(input), f.Ext);
        var withAudio = !o.RemoveAudio && info.HasAudio;

        List<string> a = ["-i", input, "-map", $"0:{video.Index}"];
        if (withAudio) a.AddRange(["-map", "0:a"]);
        MapSubtitles(a, info, f.Ext);

        if (f.IsRemux)
        {
            a.AddRange(["-c:v", "copy"]);
            if (video.Codec == "hevc" && f.Ext is "mp4" or "mov") a.AddRange(["-tag:v", "hvc1"]);
            if (withAudio) AddCompatibleAudio(a, info, f.Ext);
        }
        else
        {
            AddVideoEncoder(a, f, o);
            var filters = new List<string>
            {
                o.MaxHeight > 0 ? $"scale=-2:'trunc(min({o.MaxHeight},ih)/2)*2'" : EvenScale
            };
            if (o.Fps > 0) filters.Add($"fps={o.Fps}");
            a.AddRange(["-vf", string.Join(',', filters)]);
            if (withAudio)
            {
                a.AddRange(["-c:a", f.AudioCodec, "-b:a", $"{o.AudioBitrate}k"]);
                var channels = StereoIfNeeded(f.AudioCodec, info.AudioStreams.Max(s => s.Channels));
                if (channels > 0) a.AddRange(["-ac", "2"]);
            }
        }

        if (!withAudio) a.Add("-an");
        a.Add("-dn");
        if (f.Ext is "mp4" or "mov") a.AddRange(["-movflags", "+faststart"]);
        a.Add(output);

        await RunToFileAsync(job, output, a, info.Duration, ct);
    }

    // ───────────────────────── Edit ─────────────────────────

    public static async Task TrimAsync(string input, TimeSpan start, TimeSpan? end, bool precise, string outDir,
        Job job, CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var endSeconds = end?.TotalSeconds ?? info.Duration;
        var duration = endSeconds - start.TotalSeconds;
        if (duration <= 0) throw new UserException(Loc.T("Err_TrimRange"));

        var ext = MediaTypes.Ext(input);
        var output = OutputNamer.Reserve(outDir, BaseName(input) + "_trim", ext);

        List<string> a = ["-ss", Fmt.Num(start.TotalSeconds), "-i", input, "-t", Fmt.Num(duration)];
        if (precise)
        {
            var (hasVideo, hasAudio) = MapMain(a, info, ext);
            if (hasVideo) a.AddRange(["-vf", EvenScale]);
            AddReencode(a, ext, hasVideo, hasAudio, info);
        }
        else
        {
            a.AddRange(["-map", "0", "-dn", "-c", "copy", "-avoid_negative_ts", "make_zero"]);
        }
        if (ext is "mp4" or "mov" or "m4v") a.AddRange(["-movflags", "+faststart"]);
        a.Add(output);

        await RunToFileAsync(job, output, a, duration, ct);
    }

    /// <summary>Keeps several ranges of one file and joins them, in order, into a single output.</summary>
    public static async Task TrimRangesAsync(string input, IReadOnlyList<TimeRange> ranges, bool precise, string outDir,
        Job job, CancellationToken ct)
    {
        if (ranges.Count == 0) throw new UserException(Loc.T("Err_TrimNoRanges"));
        if (ranges.Count == 1)
        {
            var only = ranges[0];
            await TrimAsync(input, TimeSpan.FromSeconds(only.Start), TimeSpan.FromSeconds(only.End), precise, outDir, job, ct);
            return;
        }

        var info = await MediaProbe.RunAsync(input, ct);
        var total = TimeRanges.Total(ranges);
        var ext = MediaTypes.Ext(input);
        var output = OutputNamer.Reserve(outDir, BaseName(input) + "_trim", ext);

        if (precise)
        {
            // trim/atrim per range, then the concat filter: frame-accurate cuts, one encode.
            var hasVideo = info.Video is not null && !MediaTypes.IsAudioExtension(ext);
            var hasAudio = info.HasAudio;
            var graph = new StringBuilder();
            var pads = new StringBuilder();
            for (var i = 0; i < ranges.Count; i++)
            {
                var (s, e) = (Fmt.Num(ranges[i].Start), Fmt.Num(ranges[i].End));
                if (hasVideo)
                {
                    graph.Append($"[0:{info.Video!.Index}]trim=start={s}:end={e},setpts=PTS-STARTPTS,{EvenScale}[v{i}];");
                    pads.Append($"[v{i}]");
                }
                if (hasAudio)
                {
                    graph.Append($"[0:{info.Audio!.Index}]atrim=start={s}:end={e},asetpts=PTS-STARTPTS[a{i}];");
                    pads.Append($"[a{i}]");
                }
            }
            graph.Append(pads)
                 .Append($"concat=n={ranges.Count}:v={(hasVideo ? 1 : 0)}:a={(hasAudio ? 1 : 0)}")
                 .Append(hasVideo ? "[v]" : "").Append(hasAudio ? "[a]" : "");

            List<string> a = ["-i", input, "-filter_complex", graph.ToString()];
            if (hasVideo) a.AddRange(["-map", "[v]"]);
            if (hasAudio) a.AddRange(["-map", "[a]"]);
            if (!hasVideo) MapCoverOrDropVideo(a, info, ext);
            a.AddRange(["-sn", "-dn"]);
            AddReencode(a, ext, hasVideo, hasAudio, info);
            if (ext is "mp4" or "mov" or "m4v") a.AddRange(["-movflags", "+faststart"]);
            a.Add(output);
            await RunToFileAsync(job, output, a, total, ct);
            return;
        }

        // Stream copy through the concat demuxer, reading the same file once per range via inpoint/outpoint.
        var file = $"file '{input.Replace("'", "'\\''")}'";
        var list = Path.Combine(AppPaths.TempDir, $"trim_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(list,
            ranges.SelectMany(r => new[] { file, $"inpoint {Fmt.Num(r.Start)}", $"outpoint {Fmt.Num(r.End)}" }),
            new UTF8Encoding(false), ct);
        try
        {
            List<string> c = ["-f", "concat", "-safe", "0", "-i", list, "-map", "0", "-dn", "-c", "copy"];
            if (ext is "mp4" or "mov" or "m4v") c.AddRange(["-movflags", "+faststart"]);
            if (ext == "mp3") c.AddRange(["-id3v2_version", "3"]);
            c.Add(output);
            await RunToFileAsync(job, output, c, total, ct);
        }
        finally
        {
            Tools.TryDelete(list);
        }
    }

    public static async Task MergeAsync(IReadOnlyList<string> inputs, bool reencode, string outDir, string name,
        string ext, Job job, CancellationToken ct)
    {
        var infos = new List<MediaInfo>();
        foreach (var input in inputs) infos.Add(await MediaProbe.RunAsync(input, ct));
        var total = infos.Sum(i => i.Duration);
        var output = OutputNamer.Reserve(outDir, name, ext);

        if (!reencode)
        {
            // Concat demuxer: lossless, but every input must share the same codecs.
            var list = Path.Combine(AppPaths.TempDir, $"concat_{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(list,
                inputs.Select(p => $"file '{p.Replace("'", "'\\''")}'"), new UTF8Encoding(false), ct);
            try
            {
                List<string> c = ["-f", "concat", "-safe", "0", "-i", list, "-map", "0", "-dn", "-c", "copy"];
                if (ext is "mp4" or "mov" or "m4v") c.AddRange(["-movflags", "+faststart"]);
                c.Add(output);
                await RunToFileAsync(job, output, c, total, ct);
            }
            finally
            {
                Tools.TryDelete(list);
            }
            return;
        }

        // Concat filter: re-encodes, so inputs may differ in codec, size and frame rate.
        var withVideo = !MediaTypes.IsAudioExtension(ext) && infos.Any(i => i.HasVideo);
        var reference = infos.FirstOrDefault(i => i.HasVideo)?.Video;
        var width = Even(reference?.Width ?? 1280);
        var height = Even(reference?.Height ?? 720);
        var fps = reference?.FrameRate is { } r && reference.Fps > 0 ? r : "30";

        List<string> a = [];
        foreach (var input in inputs) a.AddRange(["-i", input]);

        var extraInputs = new List<string>();
        var graph = new StringBuilder();
        var concatPads = new StringBuilder();
        for (var i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            var seconds = Fmt.Num(Math.Max(info.Duration, 0.1));
            if (withVideo)
            {
                if (info.Video is { } v)
                {
                    graph.Append($"[{i}:{v.Index}]scale={width}:{height}:force_original_aspect_ratio=decrease,")
                         .Append($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},format=yuv420p[v{i}];");
                }
                else
                {
                    var k = inputs.Count + extraInputs.Count / 6;
                    extraInputs.AddRange(["-f", "lavfi", "-t", seconds, "-i", $"color=c=black:s={width}x{height}:r={fps}"]);
                    graph.Append($"[{k}:v]setsar=1,format=yuv420p[v{i}];");
                }
            }

            if (info.Audio is { } au)
            {
                graph.Append($"[{i}:{au.Index}]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a{i}];");
            }
            else
            {
                var k = inputs.Count + extraInputs.Count / 6;
                extraInputs.AddRange(["-f", "lavfi", "-t", seconds, "-i", "anullsrc=r=48000:cl=stereo"]);
                graph.Append($"[{k}:a]aformat=sample_fmts=fltp:channel_layouts=stereo[a{i}];");
            }
            concatPads.Append(withVideo ? $"[v{i}][a{i}]" : $"[a{i}]");
        }

        graph.Append(concatPads)
             .Append($"concat=n={infos.Count}:v={(withVideo ? 1 : 0)}:a=1")
             .Append(withVideo ? "[v][a]" : "[a]");

        a.AddRange(extraInputs);
        a.AddRange(["-filter_complex", graph.ToString()]);
        if (withVideo) a.AddRange(["-map", "[v]"]);
        a.AddRange(["-map", "[a]"]);
        AddVideoCodecFor(a, ext, withVideo);
        a.AddRange(AudioCodecFor(ext, 2));
        if (ext is "mp4" or "mov" or "m4v") a.AddRange(["-movflags", "+faststart"]);
        a.Add(output);

        await RunToFileAsync(job, output, a, total, ct);
    }

    public static async Task VolumeAsync(string input, bool normalize, double gainDb, double targetLufs, string outDir,
        Job job, CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var audio = info.Audio ?? throw new UserException(Loc.T("Err_NoAudio"));
        var ext = MediaTypes.Ext(input);
        var output = OutputNamer.Reserve(outDir, BaseName(input) + (normalize ? "_normalized" : "_volume"), ext);

        string filter;
        double progressFrom = 0;
        if (normalize)
        {
            var target = $"I={Fmt.Num(targetLufs)}:TP=-1.5:LRA=11";
            // Pass 1 measures loudness; pass 2 applies a linear correction for transparent results.
            var measure = await FFmpeg.RunAsync(
                ["-i", input, "-map", $"0:{audio.Index}", "-af", $"loudnorm={target}:print_format=json", "-f", "null", "-"],
                info.Duration, job, ct, logLevel: "info", progressTo: 50);
            filter = ParseLoudnorm(measure.ErrorTail) is { } m
                ? $"loudnorm={target}:measured_I={m.I}:measured_TP={m.Tp}:measured_LRA={m.Lra}:measured_thresh={m.Thresh}:offset={m.Offset}:linear=true"
                : $"loudnorm={target}";
            progressFrom = 50;
        }
        else
        {
            filter = $"volume={gainDb.ToString("+0.0;-0.0;0", CultureInfo.InvariantCulture)}dB";
        }

        List<string> a = ["-i", input];
        var videoKept = info.Video is { } v && !MediaTypes.IsAudioExtension(ext);
        if (videoKept) a.AddRange(["-map", $"0:{info.Video!.Index}", "-c:v", "copy", "-map", "0:s?", "-c:s", "copy"]);
        a.AddRange(["-map", "0:a"]);
        if (!videoKept) MapCoverOrDropVideo(a, info, ext);
        a.AddRange(["-af", filter]);
        a.AddRange(AudioCodecFor(ext, info.AudioStreams.Max(s => s.Channels)));
        if (normalize) a.AddRange(["-ar", (audio.SampleRate > 0 ? audio.SampleRate : 48000).ToString(CultureInfo.InvariantCulture)]);
        a.Add("-dn");
        if (ext == "mp3") a.AddRange(["-id3v2_version", "3"]);
        if (ext is "mp4" or "mov" or "m4v") a.AddRange(["-movflags", "+faststart"]);
        a.Add(output);

        try
        {
            await FFmpeg.RunAsync(a, info.Duration, job, ct, progressFrom: progressFrom);
            job.OutputPath = output;
        }
        catch
        {
            Tools.TryDelete(output);
            throw;
        }
        finally
        {
            OutputNamer.Release(output);
        }
    }

    public static async Task SpeedAsync(string input, double factor, string outDir, Job job, CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var ext = MediaTypes.Ext(input);
        var output = OutputNamer.Reserve(outDir, $"{BaseName(input)}_{Fmt.Num(factor)}x", ext);

        List<string> a = ["-i", input];
        var (hasVideo, hasAudio) = MapMain(a, info, ext);
        if (hasVideo) a.AddRange(["-filter:v", $"setpts=PTS/{Fmt.Num(factor)},{EvenScale}"]);
        if (hasAudio) a.AddRange(["-filter:a", AtempoChain(factor)]);
        AddReencode(a, ext, hasVideo, hasAudio, info);
        if (ext is "mp4" or "mov" or "m4v") a.AddRange(["-movflags", "+faststart"]);
        a.Add(output);

        await RunToFileAsync(job, output, a, info.Duration / factor, ct);
    }

    public static async Task GifAsync(string input, TimeSpan start, double duration, int fps, int width, string outDir,
        Job job, CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var video = info.Video ?? throw new UserException(Loc.T("Err_NoVideo"));
        var length = duration > 0 ? Math.Min(duration, Math.Max(info.Duration - start.TotalSeconds, 0.1)) : info.Duration - start.TotalSeconds;
        var output = OutputNamer.Reserve(outDir, BaseName(input), "gif");
        var scale = width > 0 ? $"scale={width}:-1:flags=lanczos," : "";

        List<string> a = ["-ss", Fmt.Num(start.TotalSeconds)];
        if (duration > 0) a.AddRange(["-t", Fmt.Num(duration)]);
        a.AddRange(["-i", input, "-map", $"0:{video.Index}",
            "-vf", $"fps={fps},{scale}split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5",
            "-loop", "0", output]);

        await RunToFileAsync(job, output, a, length, ct);
    }

    // ───────────────────────── Metadata ─────────────────────────

    public static async Task WriteTagsAsync(string input, TagSet tags, CoverAction cover, string? coverPath,
        bool overwrite, Job job, CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var ext = MediaTypes.Ext(input);
        var dir = Path.GetDirectoryName(input)!;
        var output = overwrite
            ? Path.Combine(dir, $".{BaseName(input)}.{Guid.NewGuid():N}.{ext}")
            : OutputNamer.Reserve(dir, BaseName(input) + "_tagged", ext);

        List<string> a = ["-i", input];
        var replaceCover = cover == CoverAction.Replace && coverPath is not null;
        if (replaceCover) a.AddRange(["-i", coverPath!]);

        if (info.HasVideo)
        {
            a.AddRange(["-map", "0"]);
        }
        else
        {
            a.AddRange(["-map", "0:a"]);
            if (cover == CoverAction.Keep && info.Cover is { } existing) a.AddRange(["-map", $"0:{existing.Index}"]);
            if (replaceCover) a.AddRange(["-map", "1:0"]);
        }

        a.AddRange(["-c", "copy", "-map_metadata", "0"]);
        if (replaceCover)
        {
            a.AddRange(["-disposition:v:0", "attached_pic"]);
            if (ext == "mp3") a.AddRange(["-metadata:s:v:0", "title=Album cover", "-metadata:s:v:0", "comment=Cover (front)"]);
        }
        foreach (var (key, value) in tags.Pairs) a.AddRange(["-metadata", $"{key}={value.Trim()}"]);
        if (ext == "mp3") a.AddRange(["-id3v2_version", "3", "-write_id3v1", "1"]);
        if (ext is "mp4" or "m4a" or "mov" or "m4v") a.AddRange(["-movflags", "+faststart"]);
        a.Add(output);

        try
        {
            await FFmpeg.RunAsync(a, info.Duration, job, ct);
            if (overwrite) File.Move(output, input, overwrite: true);
            job.OutputPath = overwrite ? input : output;
        }
        catch
        {
            Tools.TryDelete(output);
            throw;
        }
        finally
        {
            if (!overwrite) OutputNamer.Release(output);
        }
    }

    /// <summary>Writes the embedded cover to <paramref name="destination"/>; returns false if there is none.</summary>
    public static async Task<bool> ExportCoverAsync(string input, string destination, CancellationToken ct = default)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        if (info.Cover is not { } cover) return false;
        await FFmpeg.RunAsync(["-i", input, "-map", $"0:{cover.Index}", "-c", "copy", "-frames:v", "1", destination], 0, null, ct);
        return true;
    }

    public static string CoverExtension(StreamInfo cover) => cover.Codec == "png" ? "png" : "jpg";

    public static async Task ExtractSubtitleAsync(string input, int streamIndex, string format, string outDir, Job job,
        CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(input, ct);
        var stream = info.Streams.FirstOrDefault(s => s.Index == streamIndex && s.Type == "subtitle")
                     ?? throw new UserException(Loc.T("Err_NoSubtitle"));

        string codec, ext;
        if (stream.IsTextSubtitle)
        {
            (codec, ext) = format switch
            {
                "ass" => ("ass", "ass"),
                "vtt" => ("webvtt", "vtt"),
                _ => ("srt", "srt"),
            };
        }
        else
        {
            // Bitmap subtitles cannot become text; keep them as-is.
            codec = "copy";
            ext = stream.Codec == "hdmv_pgs_subtitle" ? "sup" : "mks";
        }

        var suffix = string.IsNullOrEmpty(stream.Language) ? $"track{stream.Index}" : stream.Language;
        var output = OutputNamer.Reserve(outDir, $"{BaseName(input)}.{suffix}", ext);
        await RunToFileAsync(job, output, ["-i", input, "-map", $"0:{stream.Index}", "-c:s", codec, output], info.Duration, ct);
    }

    public static async Task EmbedSubtitleAsync(string video, string subtitleFile, string language, string outDir, Job job,
        CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(video, ct);
        var ext = MediaTypes.Ext(video);
        var subExt = MediaTypes.Ext(subtitleFile);
        var codec = ext switch
        {
            "mp4" or "m4v" or "mov" => "mov_text",
            "mkv" => subExt switch { "ass" or "ssa" => "ass", "vtt" => "webvtt", _ => "srt" },
            "webm" => "webvtt",
            _ => throw new UserException(Loc.F("Err_SubContainer", ext)),
        };

        var newIndex = info.Subtitles.Count();
        var output = OutputNamer.Reserve(outDir, BaseName(video) + "_sub", ext);
        List<string> a = ["-i", video];
        if (DetectSubtitleCharset(subtitleFile) is { } charset) a.AddRange(["-sub_charenc", charset]);
        a.AddRange(["-i", subtitleFile, "-map", "0", "-map", "1:0", "-c", "copy", $"-c:s:{newIndex}", codec]);
        if (!string.IsNullOrWhiteSpace(language)) a.AddRange([$"-metadata:s:s:{newIndex}", $"language={language.Trim()}"]);
        if (ext is "mp4" or "mov" or "m4v") a.AddRange(["-movflags", "+faststart"]);
        a.Add(output);

        await RunToFileAsync(job, output, a, info.Duration, ct);
    }

    /// <summary>
    /// Renders subtitles into the picture. Source is an external file, or an embedded text track when
    /// <paramref name="subtitleFile"/> is null.
    /// </summary>
    public static async Task BurnSubtitleAsync(string video, string? subtitleFile, int? streamIndex, string outDir, Job job,
        CancellationToken ct)
    {
        var info = await MediaProbe.RunAsync(video, ct);
        var v = info.Video ?? throw new UserException(Loc.T("Err_NoVideo"));

        // ffmpeg's subtitles filter has awkward path escaping, so work from a temp folder with a plain file name.
        var work = Directory.CreateDirectory(Path.Combine(AppPaths.TempDir, $"burn_{Guid.NewGuid():N}")).FullName;
        try
        {
            string subName;
            string? charset = null;
            if (subtitleFile is not null)
            {
                subName = "sub." + MediaTypes.Ext(subtitleFile);
                File.Copy(subtitleFile, Path.Combine(work, subName));
                charset = DetectSubtitleCharset(subtitleFile);
            }
            else
            {
                var stream = info.Streams.FirstOrDefault(s => s.Index == streamIndex && s.Type == "subtitle")
                             ?? throw new UserException(Loc.T("Err_NoSubtitle"));
                if (!stream.IsTextSubtitle) throw new UserException(Loc.T("Err_ImageSubBurn"));
                subName = "sub.ass";
                await FFmpeg.RunAsync(["-i", video, "-map", $"0:{stream.Index}", "-c:s", "ass", Path.Combine(work, subName)],
                    0, job, ct);
            }

            var filter = $"subtitles={subName}" + (charset is null ? "" : $":charenc={charset}") + $",{EvenScale}";
            var output = OutputNamer.Reserve(outDir, BaseName(video) + "_hardsub", "mp4");
            List<string> a =
            [
                "-i", video, "-map", $"0:{v.Index}", "-map", "0:a?", "-vf", filter,
                "-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p",
            ];
            AddCompatibleAudio(a, info, "mp4");
            a.AddRange(["-sn", "-dn", "-movflags", "+faststart", output]);

            await RunToFileAsync(job, output, a, info.Duration, ct, workingDir: work);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp cleanup is best effort */ }
        }
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static async Task RunToFileAsync(Job job, string output, List<string> args, double duration,
        CancellationToken ct, string? workingDir = null)
    {
        try
        {
            await FFmpeg.RunAsync(args, duration, job, ct, workingDir: workingDir);
            job.OutputPath = output;
        }
        catch
        {
            Tools.TryDelete(output);
            throw;
        }
        finally
        {
            OutputNamer.Release(output);
        }
    }

    private static string BaseName(string path) => Path.GetFileNameWithoutExtension(path);

    private static int Even(int n) => Math.Max(2, n / 2 * 2);

    /// <summary>Keeps album art when the target container supports it; otherwise drops all video.</summary>
    private static void MapCoverOrDropVideo(List<string> a, MediaInfo info, string ext)
    {
        if (info.Cover is { Codec: "mjpeg" or "png" } cover && MediaTypes.SupportsCover(ext))
            a.AddRange(["-map", $"0:{cover.Index}", "-c:v", "copy", "-disposition:v:0", "attached_pic"]);
        else
            a.Add("-vn");
    }

    /// <summary>Maps the main video (unless the target is audio-only) and every audio stream.</summary>
    private static (bool HasVideo, bool HasAudio) MapMain(List<string> a, MediaInfo info, string ext)
    {
        var hasVideo = info.Video is not null && !MediaTypes.IsAudioExtension(ext);
        if (hasVideo) a.AddRange(["-map", $"0:{info.Video!.Index}"]);
        if (info.HasAudio) a.AddRange(["-map", "0:a"]);
        if (!hasVideo) MapCoverOrDropVideo(a, info, ext);
        a.AddRange(["-sn", "-dn"]);
        return (hasVideo, info.HasAudio);
    }

    private static void AddReencode(List<string> a, string ext, bool video, bool audio, MediaInfo info)
    {
        AddVideoCodecFor(a, ext, video);
        if (audio) a.AddRange(AudioCodecFor(ext, info.AudioStreams.Max(s => s.Channels)));
        if (ext == "mp3") a.AddRange(["-id3v2_version", "3"]);
    }

    private static void AddVideoCodecFor(List<string> a, string ext, bool video)
    {
        if (!video) return;
        switch (ext)
        {
            case "webm":
                a.AddRange(["-c:v", "libvpx-vp9", "-crf", "31", "-b:v", "0", "-row-mt", "1", "-deadline", "good",
                    "-cpu-used", "4", "-pix_fmt", "yuv420p"]);
                break;
            case "avi":
                a.AddRange(["-c:v", "mpeg4", "-q:v", "3", "-pix_fmt", "yuv420p"]);
                break;
            case "mp4" or "mov" or "mkv" or "m4v" or "ts" or "m2ts" or "mts" or "flv" or "3gp":
                a.AddRange(["-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p"]);
                break;
        }
    }

    private static string[] AudioCodecFor(string ext, int sourceChannels)
    {
        string[] codec = ext switch
        {
            "mp3" => ["-c:a", "libmp3lame", "-q:a", "2"],
            "m4a" or "aac" or "mp4" or "mov" or "mkv" or "m4v" or "ts" or "m2ts" or "mts" or "flv" or "3gp"
                => ["-c:a", "aac", "-b:a", "192k"],
            "flac" => ["-c:a", "flac"],
            "wav" => ["-c:a", "pcm_s16le"],
            "aiff" or "aif" => ["-c:a", "pcm_s16be"],
            "opus" or "webm" => ["-c:a", "libopus", "-b:a", "160k"],
            "ogg" or "oga" => ["-c:a", "libvorbis", "-q:a", "6"],
            "wma" => ["-c:a", "wmav2", "-b:a", "192k"],
            "avi" => ["-c:a", "libmp3lame", "-b:a", "192k"],
            _ => [],
        };
        return codec.Length > 1 && StereoIfNeeded(codec[1], sourceChannels) > 0 ? [.. codec, "-ac", "2"] : codec;
    }

    /// <summary>Returns 2 when the encoder cannot take the source's surround layout as-is.</summary>
    private static int StereoIfNeeded(string codec, int sourceChannels) =>
        sourceChannels > 2 && codec is "libmp3lame" or "wmav2" or "libopus" ? 2 : 0;

    /// <summary>Copies each audio stream when the container accepts its codec, otherwise encodes AAC.</summary>
    private static void AddCompatibleAudio(List<string> a, MediaInfo info, string ext)
    {
        var n = 0;
        foreach (var s in info.AudioStreams)
        {
            var copyable = ext switch
            {
                "mp4" or "mov" or "m4v" => s.Codec is "aac" or "mp3" or "ac3" or "eac3" or "alac" or "flac" or "opus",
                "webm" => s.Codec is "opus" or "vorbis",
                _ => true,
            };
            if (copyable) a.AddRange([$"-c:a:{n}", "copy"]);
            else a.AddRange([$"-c:a:{n}", ext == "webm" ? "libopus" : "aac", $"-b:a:{n}", "192k"]);
            n++;
        }
    }

    /// <summary>Carries subtitle tracks over in the format each container understands.</summary>
    private static void MapSubtitles(List<string> a, MediaInfo info, string ext)
    {
        var n = 0;
        foreach (var s in info.Subtitles)
        {
            var codec = ext switch
            {
                "mp4" or "mov" or "m4v" => s.IsTextSubtitle ? "mov_text" : null,
                "webm" => s.IsTextSubtitle ? "webvtt" : null,
                "mkv" => s.Codec == "mov_text" ? "srt" : "copy",
                _ => null,
            };
            if (codec is null) continue;
            a.AddRange(["-map", $"0:{s.Index}", $"-c:s:{n}", codec]);
            n++;
        }
        if (n == 0) a.Add("-sn");
    }

    private static void AddVideoEncoder(List<string> a, VideoFormat f, VideoOptions o)
    {
        string Q(int veryHigh, int high, int medium, int low) => (o.Quality switch
        {
            Quality.VeryHigh => veryHigh,
            Quality.High => high,
            Quality.Medium => medium,
            _ => low,
        }).ToString(CultureInfo.InvariantCulture);

        string S(string fast, string balanced, string small) => o.Speed switch
        {
            EncodeSpeed.Fast => fast,
            EncodeSpeed.Balanced => balanced,
            _ => small,
        };

        switch (f.Codec)
        {
            case VCodec.H264 or VCodec.H265:
                var hevc = f.Codec == VCodec.H265;
                switch (o.Encoder)
                {
                    case HwEncoder.Nvidia:
                        a.AddRange(["-c:v", hevc ? "hevc_nvenc" : "h264_nvenc", "-preset", S("p2", "p5", "p7"),
                            "-rc", "vbr", "-cq", Q(19, 23, 27, 31), "-b:v", "0", "-pix_fmt", "yuv420p"]);
                        break;
                    case HwEncoder.Intel:
                        a.AddRange(["-c:v", hevc ? "hevc_qsv" : "h264_qsv", "-preset", S("veryfast", "medium", "veryslow"),
                            "-global_quality", Q(19, 23, 27, 31), "-pix_fmt", "nv12"]);
                        break;
                    case HwEncoder.Amd:
                        var qp = Q(18, 22, 26, 30);
                        a.AddRange(["-c:v", hevc ? "hevc_amf" : "h264_amf", "-quality", S("speed", "balanced", "quality"),
                            "-rc", "cqp", "-qp_i", qp, "-qp_p", qp]);
                        if (!hevc) a.AddRange(["-qp_b", qp]);
                        a.AddRange(["-pix_fmt", "nv12"]);
                        break;
                    default:
                        a.AddRange(["-c:v", hevc ? "libx265" : "libx264", "-preset", S("veryfast", "medium", "slow"),
                            "-crf", hevc ? Q(20, 24, 28, 32) : Q(18, 21, 24, 28), "-pix_fmt", "yuv420p"]);
                        if (hevc) a.AddRange(["-x265-params", "log-level=error"]);
                        break;
                }
                if (hevc && f.Ext is "mp4" or "mov") a.AddRange(["-tag:v", "hvc1"]);
                break;
            case VCodec.VP9:
                a.AddRange(["-c:v", "libvpx-vp9", "-crf", Q(24, 31, 36, 42), "-b:v", "0", "-row-mt", "1",
                    "-deadline", "good", "-cpu-used", S("5", "3", "1"), "-pix_fmt", "yuv420p"]);
                break;
            case VCodec.AV1:
                a.AddRange(["-c:v", "libsvtav1", "-crf", Q(24, 30, 36, 44), "-preset", S("10", "7", "5"), "-pix_fmt", "yuv420p"]);
                break;
            case VCodec.MPEG4:
                a.AddRange(["-c:v", "mpeg4", "-q:v", Q(2, 4, 6, 9), "-pix_fmt", "yuv420p"]);
                break;
        }
    }

    private static string AtempoChain(double factor)
    {
        // atempo is limited to 0.5–2.0 per instance, so chain several for larger changes.
        var parts = new List<string>();
        while (factor > 2.0) { parts.Add("atempo=2.0"); factor /= 2.0; }
        while (factor < 0.5) { parts.Add("atempo=0.5"); factor /= 0.5; }
        parts.Add($"atempo={factor.ToString("0.######", CultureInfo.InvariantCulture)}");
        return string.Join(',', parts);
    }

    /// <summary>Picks a file extension that can hold the given audio codec without re-encoding.</summary>
    private static string CopyExtension(string codec) => codec switch
    {
        "aac" or "alac" => "m4a",
        "mp3" => "mp3",
        "opus" => "opus",
        "vorbis" => "ogg",
        "flac" => "flac",
        "ac3" => "ac3",
        "eac3" => "eac3",
        "dts" => "dts",
        "wmav1" or "wmav2" => "wma",
        _ when codec.StartsWith("pcm_", StringComparison.Ordinal) => "wav",
        _ => "mka",
    };

    private sealed record Loudness(string I, string Tp, string Lra, string Thresh, string Offset);

    private static Loudness? ParseLoudnorm(string stderr)
    {
        var start = stderr.LastIndexOf('{');
        var end = stderr.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(stderr[start..(end + 1)]);
            var r = doc.RootElement;
            var values = new[] { r.Str("input_i"), r.Str("input_tp"), r.Str("input_lra"), r.Str("input_thresh"), r.Str("target_offset") };
            if (values.Any(v => v is null || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)))
                return null;
            return new Loudness(values[0]!, values[1]!, values[2]!, values[3]!, values[4]!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// ffmpeg expects UTF-8 subtitles. Legacy Thai .srt files are usually Windows-874, so detect that
    /// and tell ffmpeg which code page to decode from.
    /// </summary>
    public static string? DetectSubtitleCharset(string path)
    {
        if (MediaTypes.Ext(path) is not ("srt" or "ass" or "ssa" or "vtt" or "sub" or "txt")) return null;
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return null;
        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..]) return "UTF-16";
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return null;
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8; guess below.
        }

        var high = bytes.Count(b => b >= 0x80);
        var ansi = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        // Thai text is mostly high bytes; Latin text with a few accents is not.
        if (ansi == 874 || high > bytes.Length * 0.2) return "CP874";
        return $"CP{ansi}";
    }
}
