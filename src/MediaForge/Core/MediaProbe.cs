using System.Globalization;
using System.Text;
using System.Text.Json;
using MediaForge.Localization;

namespace MediaForge.Core;

public sealed class StreamInfo
{
    public int Index { get; init; }
    public string Type { get; init; } = "";
    public string Codec { get; init; } = "";
    public string? Language { get; init; }
    public string? Title { get; init; }
    public bool IsAttachedPic { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? FrameRate { get; init; }
    public double Fps { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public long BitRate { get; init; }

    public bool IsTextSubtitle => Codec is "subrip" or "srt" or "ass" or "ssa" or "mov_text" or "webvtt" or "text";

    public string Description => Type switch
    {
        "video" when IsAttachedPic => $"#{Index} cover: {Codec} {Width}x{Height}",
        "video" => $"#{Index} video: {Codec} {Width}x{Height}{(Fps > 0 ? $" @ {Fps:0.##} fps" : "")}{BitRateText}",
        "audio" => $"#{Index} audio: {Codec} {SampleRate} Hz, {Channels} ch{BitRateText}{LanguageText}",
        "subtitle" => $"#{Index} subtitle: {Codec}{LanguageText}{TitleText}",
        _ => $"#{Index} {Type}: {Codec}"
    };

    private string BitRateText => BitRate > 0 ? $", {BitRate / 1000} kb/s" : "";
    private string LanguageText => string.IsNullOrEmpty(Language) ? "" : $" [{Language}]";
    private string TitleText => string.IsNullOrEmpty(Title) ? "" : $" \"{Title}\"";

    public override string ToString() => Description;
}

public sealed class MediaInfo
{
    public required string FilePath { get; init; }
    public double Duration { get; init; }
    public long Size { get; init; }
    public long BitRate { get; init; }
    public string FormatName { get; init; } = "";
    public string FormatLongName { get; init; } = "";
    public List<StreamInfo> Streams { get; init; } = [];
    public Dictionary<string, string> Tags { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public StreamInfo? Video => Streams.FirstOrDefault(s => s.Type == "video" && !s.IsAttachedPic);
    public StreamInfo? Audio => Streams.FirstOrDefault(s => s.Type == "audio");
    public StreamInfo? Cover => Streams.FirstOrDefault(s => s.Type == "video" && s.IsAttachedPic);
    public IEnumerable<StreamInfo> AudioStreams => Streams.Where(s => s.Type == "audio");
    public IEnumerable<StreamInfo> Subtitles => Streams.Where(s => s.Type == "subtitle");
    public bool HasVideo => Video is not null;
    public bool HasAudio => Audio is not null;

    /// <summary>First non-empty tag among the given aliases (tag keys differ per container).</summary>
    public string? Tag(params string[] keys)
    {
        foreach (var key in keys)
            if (Tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Path.GetFileName(FilePath));
        sb.AppendLine($"{Loc.T("Info_Format")}: {FormatLongName} ({FormatName})");
        sb.AppendLine($"{Loc.T("Info_Duration")}: {Fmt.Clock(Duration)}");
        sb.AppendLine($"{Loc.T("Info_Size")}: {Fmt.Bytes(Size)}");
        if (BitRate > 0) sb.AppendLine($"{Loc.T("Info_Bitrate")}: {BitRate / 1000} kb/s");
        sb.AppendLine();
        sb.AppendLine($"{Loc.T("Info_Streams")}:");
        foreach (var s in Streams) sb.AppendLine("  " + s.Description);
        if (Tags.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{Loc.T("Info_Tags")}:");
            foreach (var (key, value) in Tags) sb.AppendLine($"  {key} = {value}");
        }
        return sb.ToString();
    }
}

public static class MediaProbe
{
    public static async Task<MediaInfo> RunAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) throw new UserException(Loc.F("Err_FileNotFound", path));
        var ffprobe = Tools.Ffprobe ?? throw new ToolMissingException("ffprobe");

        var result = await ProcessRunner.RunAsync(ffprobe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
            ct: ct, captureOutput: true);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            throw new UserException(Loc.F("Err_Probe", Path.GetFileName(path), result.ErrorTail.Trim()));

        using var doc = JsonDocument.Parse(result.Output);
        var root = doc.RootElement;

        var streams = new List<StreamInfo>();
        if (root.TryGetProperty("streams", out var array) && array.ValueKind == JsonValueKind.Array)
            foreach (var s in array.EnumerateArray())
                streams.Add(ParseStream(s));

        var format = root.Obj("format");
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (format.Obj("tags") is { ValueKind: JsonValueKind.Object } tagObj)
            foreach (var prop in tagObj.EnumerateObject())
                tags[prop.Name] = prop.Value.ToString();

        var duration = format.Dbl("duration");
        if (duration <= 0 && array.ValueKind == JsonValueKind.Array)
            duration = array.EnumerateArray().Select(s => s.Dbl("duration")).DefaultIfEmpty(0).Max();

        return new MediaInfo
        {
            FilePath = path,
            Duration = duration,
            Size = format.Lng("size") is > 0 and var size ? size : new FileInfo(path).Length,
            BitRate = format.Lng("bit_rate"),
            FormatName = format.Str("format_name") ?? "",
            FormatLongName = format.Str("format_long_name") ?? "",
            Streams = streams,
            Tags = tags,
        };
    }

    private static StreamInfo ParseStream(JsonElement s)
    {
        var tags = s.Obj("tags");
        var rate = s.Str("avg_frame_rate") is { } avg && avg != "0/0" ? avg : s.Str("r_frame_rate");
        return new StreamInfo
        {
            Index = s.Int("index"),
            Type = s.Str("codec_type") ?? "",
            Codec = s.Str("codec_name") ?? "",
            Language = tags.Str("language"),
            Title = tags.Str("title") ?? tags.Str("TITLE"),
            IsAttachedPic = s.Obj("disposition").Int("attached_pic") == 1,
            Width = s.Int("width"),
            Height = s.Int("height"),
            FrameRate = rate,
            Fps = ParseRate(rate),
            SampleRate = s.Int("sample_rate"),
            Channels = s.Int("channels"),
            BitRate = s.Lng("bit_rate"),
        };
    }

    private static double ParseRate(string? rate)
    {
        if (string.IsNullOrEmpty(rate)) return 0;
        var parts = rate.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
            && den > 0)
            return num / den;
        return double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
