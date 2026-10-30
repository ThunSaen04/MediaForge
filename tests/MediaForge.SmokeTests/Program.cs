// End-to-end smoke tests: generates sample media with ffmpeg, runs every operation the app offers
// through the real Converter / YtDlp code paths, and checks the results with ffprobe.
//
//   dotnet run --project tests/MediaForge.SmokeTests -- <work-dir> [--youtube] [--filter=<part of a test name>]

using System.Diagnostics;
using System.Text;
using MediaForge.Core;

Console.OutputEncoding = Encoding.UTF8;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var work = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(Path.GetTempPath(), "MediaForgeSmoke");
var runYouTube = args.Contains("--youtube");
var filter = args.FirstOrDefault(a => a.StartsWith("--filter="))?["--filter=".Length..];
var inDir = Path.Combine(work, "in");
var outDir = Path.Combine(work, "out");
if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(inDir);
Directory.CreateDirectory(outDir);

AppSettings.Load();
var ffmpeg = Tools.Ffmpeg ?? throw new InvalidOperationException("ffmpeg not found");
Console.WriteLine($"ffmpeg : {ffmpeg}");
Console.WriteLine($"work   : {work}");

var pass = 0;
var failures = new List<string>();

// ───────────── fixtures ─────────────
var video = Path.Combine(inDir, "ทดสอบ วิดีโอ.mp4");
await Gen("-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
    "-t", "6", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-shortest", video);
var small = Path.Combine(inDir, "small 640.mp4");
await Gen("-f", "lavfi", "-i", "testsrc=size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=48000",
    "-t", "4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", small);
var cover = Path.Combine(inDir, "cover.png");
await Gen("-f", "lavfi", "-i", "testsrc=size=300x300", "-frames:v", "1", cover);
var mp3 = Path.Combine(inDir, "song with cover.mp3");
await Gen("-f", "lavfi", "-i", "sine=frequency=330:duration=5", "-i", cover, "-map", "0:a", "-map", "1:0",
    "-c:a", "libmp3lame", "-b:a", "192k", "-c:v", "png", "-disposition:v:0", "attached_pic",
    "-metadata", "title=Old title", "-id3v2_version", "3", mp3);
var surround = Path.Combine(inDir, "surround 5.1.flac");
await Gen("-f", "lavfi", "-i", "sine=frequency=220:duration=4", "-af", "pan=5.1|c0=c0|c1=c0|c2=c0|c3=c0|c4=c0|c5=c0", surround);
const string srtText = "1\n00:00:00,500 --> 00:00:02,500\nสวัสดีครับ Hello\n\n2\n00:00:03,000 --> 00:00:05,000\nทดสอบซับไทย\n";
var srtUtf8 = Path.Combine(inDir, "subs utf8.srt");
File.WriteAllText(srtUtf8, srtText, new UTF8Encoding(false));
var srt874 = Path.Combine(inDir, "subs tis620.srt");
File.WriteAllText(srt874, srtText, Encoding.GetEncoding(874));
var mkv = Path.Combine(inDir, "with subs.mkv");
await Gen("-i", video, "-i", srtUtf8, "-map", "0", "-map", "1", "-c", "copy", "-c:s", "srt", "-metadata:s:s:0", "language=tha", mkv);
Console.WriteLine("fixtures ready\n");

// ───────────── convert: audio ─────────────
var expectedAudio = new Dictionary<string, string>
{
    ["mp3"] = "mp3", ["m4a"] = "aac", ["opus"] = "opus", ["ogg"] = "vorbis", ["wma"] = "wmav2",
    ["flac"] = "flac", ["wav"] = "pcm_s16le", ["alac"] = "alac", ["aiff"] = "pcm_s16be",
};
foreach (var format in AudioFormat.All.Where(f => !f.IsCopy))
{
    await Check($"video → {format.Id} (Thai file name)", async () =>
    {
        var job = NewJob();
        await Converter.ToAudioAsync(video, outDir, new AudioOptions(format, 192, 0, 0), job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        return Expect(info.Audio?.Codec == expectedAudio[format.Id] && !info.HasVideo && Near(info.Duration, 6, 0.3), info);
    });
}

await Check("video → original audio (stream copy)", async () =>
{
    var job = NewJob();
    await Converter.ToAudioAsync(video, outDir, new AudioOptions(Audio("copy"), 0, 0, 0), job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(job.OutputPath!.EndsWith(".m4a") && info.Audio?.Codec == "aac" && !info.HasVideo, info);
});

foreach (var target in new[] { "mp3", "m4a", "flac" })
{
    await Check($"mp3 with cover → {target} keeps cover", async () =>
    {
        var job = NewJob();
        await Converter.ToAudioAsync(mp3, outDir, new AudioOptions(Audio(target), 256, 44100, 0), job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        return Expect(info.Cover is not null && info.Audio?.SampleRate == 44100, info);
    });
}

foreach (var target in new[] { "mp3", "opus", "m4a" })
{
    await Check($"5.1 flac → {target}", async () =>
    {
        var job = NewJob();
        await Converter.ToAudioAsync(surround, outDir, new AudioOptions(Audio(target), 192, 0, 0), job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        var expectedChannels = target == "m4a" ? 6 : 2;
        return Expect(info.Audio?.Channels == expectedChannels, info);
    });
}

await Check("mono 48k override", async () =>
{
    var job = NewJob();
    await Converter.ToAudioAsync(video, outDir, new AudioOptions(Audio("mp3"), 128, 48000, 1), job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Audio is { Channels: 1, SampleRate: 48000 }, info);
});

// ───────────── convert: video ─────────────
var expectedVideo = new Dictionary<VCodec, string>
{
    [VCodec.H264] = "h264", [VCodec.H265] = "hevc", [VCodec.VP9] = "vp9", [VCodec.AV1] = "av1", [VCodec.MPEG4] = "mpeg4",
};
foreach (var format in VideoFormat.All)
{
    var source = format.IsRemux || format.Ext == "mkv" ? mkv : video;
    await Check($"{Path.GetFileName(source)} → {format.Id}", async () =>
    {
        var job = NewJob();
        var options = new VideoOptions(format, HwEncoder.Cpu, Quality.High, EncodeSpeed.Fast, 0, 0, 160, false);
        await Converter.ToVideoAsync(source, outDir, options, job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        var codecOk = format.IsRemux ? info.Video?.Codec == "h264" : info.Video?.Codec == expectedVideo[format.Codec];
        var subsOk = format.Ext switch
        {
            "mkv" => info.Subtitles.Any(s => s.Codec == "subrip"),
            "mp4" when source == mkv => info.Subtitles.Any(s => s.Codec == "mov_text"),
            _ => true,
        };
        return Expect(codecOk && subsOk && info.HasAudio && Near(info.Duration, 6, 0.4), info);
    });
}

await Check("mp4 h264 → 480p / 24 fps / no audio", async () =>
{
    var job = NewJob();
    var options = new VideoOptions(VideoFormat.All[0], HwEncoder.Cpu, Quality.Medium, EncodeSpeed.Fast, 480, 24, 128, true);
    await Converter.ToVideoAsync(video, outDir, options, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Video is { Height: 480, Width: 854 } v && Math.Abs(v.Fps - 24) < 0.1 && !info.HasAudio, info);
});

var hardware = await Tools.DetectHardwareEncodersAsync();
Console.WriteLine($"hardware encoders: {(hardware.Count == 0 ? "none" : string.Join(", ", hardware))}");
foreach (var hw in hardware)
{
    foreach (var format in VideoFormat.All.Where(f => f.SupportsHardware && f.Ext == "mp4"))
    {
        await Check($"{format.Id} via {hw}", async () =>
        {
            var job = NewJob();
            var options = new VideoOptions(format, hw, Quality.High, EncodeSpeed.Balanced, 720, 0, 160, false);
            await Converter.ToVideoAsync(video, outDir, options, job, default);
            var info = await MediaProbe.RunAsync(job.OutputPath!);
            return Expect(info.Video?.Codec == expectedVideo[format.Codec], info);
        });
    }
}

// ───────────── edit ─────────────
await Check("trim fast (stream copy)", async () =>
{
    var job = NewJob();
    await Converter.TrimAsync(video, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), false, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.HasVideo && info.HasAudio && info.Duration is > 2.5 and < 5.2, info);
});

await Check("trim precise (re-encode)", async () =>
{
    var job = NewJob();
    await Converter.TrimAsync(video, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4), true, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.HasVideo && Near(info.Duration, 2.5, 0.15), info);
});

await Check("trim mp3 precise keeps cover", async () =>
{
    var job = NewJob();
    await Converter.TrimAsync(mp3, TimeSpan.FromSeconds(1), null, true, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Cover is not null && Near(info.Duration, 4, 0.15), info);
});

await Check("merge fast (same codecs)", async () =>
{
    var job = NewJob();
    await Converter.MergeAsync([small, small], false, outDir, "merged fast", "mp4", job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(Near(info.Duration, 8, 0.3), info);
});

await Check("merge re-encode (mixed video sizes + audio-only)", async () =>
{
    var job = NewJob();
    await Converter.MergeAsync([video, mp3, small], true, outDir, "merged mixed", "mp4", job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Video is { Width: 1280, Height: 720 } && info.HasAudio && Near(info.Duration, 15, 0.5), info);
});

await Check("merge re-encode audio-only → mp3", async () =>
{
    var job = NewJob();
    await Converter.MergeAsync([mp3, surround], true, outDir, "merged audio", "mp3", job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(!info.HasVideo && info.Audio?.Codec == "mp3" && Near(info.Duration, 9, 0.3), info);
});

await Check("normalize loudness (2-pass) on video", async () =>
{
    var job = NewJob();
    await Converter.VolumeAsync(video, true, 0, -14, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    var twoPass = job.Log.Contains("measured_I=");
    return Expect(twoPass && info.Video?.Codec == "h264" && info.Audio?.SampleRate == 48000 && Near(info.Duration, 6, 0.3),
        info, twoPass ? "" : "single-pass fallback used");
});

await Check("volume +6 dB on mp3 keeps cover", async () =>
{
    var job = NewJob();
    await Converter.VolumeAsync(mp3, false, 6, -14, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Cover is not null && info.Audio?.Codec == "mp3", info);
});

await Check("speed 2x video", async () =>
{
    var job = NewJob();
    await Converter.SpeedAsync(video, 2, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.HasVideo && info.HasAudio && Near(info.Duration, 3, 0.25), info);
});

await Check("speed 0.25x audio (atempo chain)", async () =>
{
    var job = NewJob();
    await Converter.SpeedAsync(mp3, 0.25, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(Near(info.Duration, 20, 0.5), info);
});

await Check("gif", async () =>
{
    var job = NewJob();
    await Converter.GifAsync(video, TimeSpan.FromSeconds(1), 2, 10, 320, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Video is { Codec: "gif", Width: 320 }, info);
});

// ───────────── metadata ─────────────
var tags = new TagSet("ชื่อเพลงไทย", "ศิลปิน", "อัลบั้ม", "Album Artist", "2026", "3", "Pop", "comment");

await Check("tags + replace cover (mp3, new file)", async () =>
{
    var job = NewJob();
    await Converter.WriteTagsAsync(mp3, tags, CoverAction.Replace, cover, false, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(job.OutputPath!.Contains("_tagged") && info.Tag("title") == "ชื่อเพลงไทย" && info.Tag("artist") == "ศิลปิน"
                  && info.Cover is not null, info);
});

await Check("tags overwrite original (m4a)", async () =>
{
    var m4a = Path.Combine(outDir, "overwrite me.m4a");
    await Gen("-f", "lavfi", "-i", "sine=duration=3", "-c:a", "aac", m4a);
    var job = NewJob();
    await Converter.WriteTagsAsync(m4a, tags, CoverAction.Replace, cover, true, job, default);
    var info = await MediaProbe.RunAsync(m4a);
    var leftovers = Directory.GetFiles(outDir, ".overwrite me*");
    return Expect(job.OutputPath == m4a && info.Tag("album") == "อัลบั้ม" && info.Cover is not null && leftovers.Length == 0, info);
});

await Check("remove cover (flac)", async () =>
{
    var flacJob = NewJob();
    await Converter.ToAudioAsync(mp3, outDir, new AudioOptions(Audio("flac"), 0, 0, 0), flacJob, default);
    var job = NewJob();
    await Converter.WriteTagsAsync(flacJob.OutputPath!, tags, CoverAction.Remove, null, true, job, default);
    var info = await MediaProbe.RunAsync(flacJob.OutputPath!);
    return Expect(info.Cover is null && info.Tag("title") == "ชื่อเพลงไทย", info);
});

await Check("tags on video (mp4) keep streams", async () =>
{
    var job = NewJob();
    await Converter.WriteTagsAsync(video, tags, CoverAction.Keep, null, false, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.HasVideo && info.HasAudio && info.Tag("title") == "ชื่อเพลงไทย", info);
});

await Check("export cover", async () =>
{
    var dest = Path.Combine(outDir, "exported cover.png");
    var ok = await Converter.ExportCoverAsync(mp3, dest);
    return ok && new FileInfo(dest).Length > 100 ? null : "cover not exported";
});

await Check("subtitle charset detection", () => Task.FromResult(
    Converter.DetectSubtitleCharset(srtUtf8) is null && Converter.DetectSubtitleCharset(srt874) == "CP874"
        ? null
        : $"utf8={Converter.DetectSubtitleCharset(srtUtf8)}, 874={Converter.DetectSubtitleCharset(srt874)}"));

var mkvInfo = await MediaProbe.RunAsync(mkv);
var subIndex = mkvInfo.Subtitles.First().Index;
foreach (var format in new[] { "srt", "ass", "vtt" })
{
    await Check($"extract subtitle → {format}", async () =>
    {
        var job = NewJob();
        await Converter.ExtractSubtitleAsync(mkv, subIndex, format, outDir, job, default);
        var text = await File.ReadAllTextAsync(job.OutputPath!);
        return text.Contains("ทดสอบซับไทย") && job.OutputPath!.EndsWith($".tha.{format}") ? null : $"{job.OutputPath}: {text[..Math.Min(80, text.Length)]}";
    });
}

foreach (var container in new[] { (Path: video, Codec: "mov_text"), (Path: mkv, Codec: "subrip") })
{
    await Check($"embed TIS-620 subtitle into {Path.GetExtension(container.Path)}", async () =>
    {
        var job = NewJob();
        await Converter.EmbedSubtitleAsync(container.Path, srt874, "tha", outDir, job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        var added = info.Subtitles.LastOrDefault();
        if (added is null || added.Codec != container.Codec || added.Language != "tha") return Describe(info);
        // Round-trip: the embedded text must decode to proper Thai.
        var extract = NewJob();
        await Converter.ExtractSubtitleAsync(job.OutputPath!, added.Index, "srt", outDir, extract, default);
        var text = await File.ReadAllTextAsync(extract.OutputPath!);
        return text.Contains("สวัสดีครับ") ? null : $"garbled text: {text[..Math.Min(80, text.Length)]}";
    });
}

await Check("burn subtitle file (TIS-620)", async () =>
{
    var job = NewJob();
    await Converter.BurnSubtitleAsync(video, srt874, null, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Video?.Codec == "h264" && info.HasAudio && info.Subtitles.Count() == 0 && Near(info.Duration, 6, 0.3), info);
});

await Check("burn embedded subtitle track", async () =>
{
    var job = NewJob();
    await Converter.BurnSubtitleAsync(mkv, null, subIndex, outDir, job, default);
    var info = await MediaProbe.RunAsync(job.OutputPath!);
    return Expect(info.Video?.Codec == "h264" && Near(info.Duration, 6, 0.3), info);
});

await Check("cancel kills ffmpeg and removes the partial file", async () =>
{
    var job = NewJob();
    var before = Directory.GetFiles(outDir).ToHashSet();
    using var cts = new CancellationTokenSource();
    var slow = new VideoOptions(VideoFormat.All.First(f => f.Id == "webm_vp9"), HwEncoder.Cpu, Quality.VeryHigh, EncodeSpeed.Small, 0, 0, 160, false);
    var running = Converter.ToVideoAsync(video, outDir, slow, job, cts.Token);
    var waited = Stopwatch.StartNew();
    while (job.Progress <= 0 && !running.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
    var progressAtCancel = job.Progress;
    // ffmpeg processes alive right now (ours, plus any started by other apps) must all be gone after cancelling.
    var started = DateTime.Now - waited.Elapsed - TimeSpan.FromSeconds(1);
    var spawned = Process.GetProcessesByName("ffmpeg").Where(p => p.StartTime >= started).ToList();
    cts.Cancel();
    try
    {
        await running;
        return $"expected cancellation (progress {progressAtCancel:0.0}%)";
    }
    catch (OperationCanceledException)
    {
        var leftovers = Directory.GetFiles(outDir).Except(before).ToList();
        if (leftovers.Count > 0) return "partial file left behind: " + string.Join(", ", leftovers);
        if (spawned.Count == 0) return "ffmpeg process was not observed";
        if (spawned.Any(p => !p.WaitForExit(2000))) return "ffmpeg still running after cancel";
        return progressAtCancel is > 0 and < 100 ? null : $"no progress reported before cancel ({progressAtCancel:0.0}%)";
    }
});

// ───────────── YouTube ─────────────
if (runYouTube)
{
    if (Tools.YtDlp is null)
    {
        Console.WriteLine("installing yt-dlp...");
        await Tools.InstallYtDlpAsync(null, default);
    }
    Console.WriteLine($"yt-dlp : {Tools.YtDlp} {await Tools.VersionAsync(Tools.YtDlp, "--version")}");
    Console.WriteLine($"js     : {string.Join(' ', Tools.JsRuntimeArgs())}");
    var ytDir = Path.Combine(outDir, "youtube");
    const string single = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    List<YtEntry> entries = [];
    await Check("yt analyze single video", async () =>
    {
        entries = await YtDlp.AnalyzeAsync(single, default);
        return entries is [{ Title: var t, Duration: > 0 }] && t.Contains("zoo", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{entries.Count} entries: {string.Join(" | ", entries.Select(e => e.Title))}";
    });

    await Check("yt download audio mp3 + thumbnail + metadata", async () =>
    {
        var job = NewJob(JobKind.Download);
        var o = new YtOptions(true, 0, "mp4", "mp3", "192K", true, true, false, "", false);
        await YtDlp.DownloadAsync(entries[0], o, ytDir, job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        return Expect(info.Audio?.Codec == "mp3" && info.Cover is not null && info.Tag("title") is not null, info, job.OutputPath);
    });

    await Check("yt download video 360p mp4", async () =>
    {
        var job = NewJob(JobKind.Download);
        var o = new YtOptions(false, 360, "mp4", "mp3", "192K", true, true, false, "", false);
        await YtDlp.DownloadAsync(entries[0], o, ytDir, job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        return Expect(job.OutputPath!.EndsWith(".mp4") && info.Video is { Height: <= 360 } && info.HasAudio, info, job.OutputPath);
    });

    await Check("yt analyze playlist + download first entry (m4a)", async () =>
    {
        // "Uploads from jawed" — the channel behind the first YouTube video; small and long-lived.
        var list = await YtDlp.AnalyzeAsync("https://www.youtube.com/playlist?list=UU4QobU6STFB0P71PMvOGN5A", default);
        if (list.Count == 0 || list[0].PlaylistTitle is null) return $"{list.Count} entries";
        Console.WriteLine($"    playlist \"{list[0].PlaylistTitle}\": {list.Count} entries");
        var job = NewJob(JobKind.Download);
        var o = new YtOptions(true, 0, "mp4", "m4a", "128K", true, true, false, "", true);
        await YtDlp.DownloadAsync(list[0], o, Path.Combine(ytDir, OutputNamer.Sanitize(list[0].PlaylistTitle!)), job, default);
        var info = await MediaProbe.RunAsync(job.OutputPath!);
        return Expect(Path.GetFileName(job.OutputPath!).StartsWith("001 - ") && info.Audio?.Codec == "aac", info, job.OutputPath);
    });
}

Console.WriteLine();
Console.WriteLine($"{pass} passed, {failures.Count} failed");
foreach (var f in failures) Console.WriteLine("  ✗ " + f);
return failures.Count == 0 ? 0 : 1;

// ───────────── helpers ─────────────
async Task Check(string name, Func<Task<string?>> test)
{
    if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
    var clock = Stopwatch.StartNew();
    try
    {
        var problem = await test();
        if (problem is null)
        {
            pass++;
            Console.WriteLine($"PASS  {name}  ({clock.Elapsed.TotalSeconds:0.0}s)");
            return;
        }
        failures.Add($"{name}: {problem}");
        Console.WriteLine($"FAIL  {name}: {problem}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

async Task Gen(params string[] a)
{
    var r = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-y", "-loglevel", "error", .. a]);
    if (r.ExitCode != 0) throw new InvalidOperationException("fixture failed: " + r.ErrorTail);
}

static Job NewJob(JobKind kind = JobKind.Process) => new("test", "", kind, (_, _) => Task.CompletedTask);

static AudioFormat Audio(string id) => AudioFormat.All.First(f => f.Id == id);

static bool Near(double actual, double expected, double tolerance) => Math.Abs(actual - expected) <= tolerance;

static string? Expect(bool ok, MediaInfo info, string? extra = null) => ok ? null : Describe(info) + (extra is null ? "" : $" [{extra}]");

static string Describe(MediaInfo info) =>
    $"{Path.GetFileName(info.FilePath)} dur={info.Duration:0.00} streams=[{string.Join("; ", info.Streams.Select(s => s.Description))}]";
