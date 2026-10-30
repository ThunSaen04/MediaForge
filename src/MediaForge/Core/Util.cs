using System.Globalization;
using System.Text.Json;
using MediaForge.Localization;

namespace MediaForge.Core;

/// <summary>An error whose message is meant to be shown to the user as-is.</summary>
public class UserException(string message) : Exception(message);

public sealed class ToolMissingException(string tool) : UserException(Loc.F("Err_ToolMissing", tool))
{
    public string Tool { get; } = tool;
}

public static class Fmt
{
    public static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) return "--:--";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>h:mm:ss(.fff) — precise enough to round-trip through <see cref="TimeParser"/>.</summary>
    public static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var text = $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        return t.Milliseconds > 0 ? $"{text}.{t.Milliseconds:000}" : text;
    }

    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{bytes:0.0} {units[i]}";
    }

    /// <summary>Invariant number for ffmpeg arguments.</summary>
    public static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

public static class TimeParser
{
    /// <summary>Accepts "90", "1:30", "01:02:03" and "1:02:03.250".</summary>
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3) return false;
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0)
                return false;
            total = total * 60 + n;
        }
        value = TimeSpan.FromSeconds(total);
        return true;
    }
}

public static class MediaTypes
{
    public static readonly string[] VideoExtensions =
        ["mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts", "m2ts", "mts", "3gp", "ogv", "vob"];

    public static readonly string[] AudioExtensions =
        ["mp3", "wav", "flac", "aac", "m4a", "ogg", "oga", "opus", "wma", "aiff", "aif", "alac", "ape", "wv", "ac3", "dts", "amr", "mka"];

    private static readonly HashSet<string> All =
        new(VideoExtensions.Concat(AudioExtensions), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Audio = new(AudioExtensions, StringComparer.OrdinalIgnoreCase);

    public static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static bool IsMedia(string path) => All.Contains(Ext(path));

    public static bool IsAudioExtension(string ext) => Audio.Contains(ext);

    /// <summary>Containers that can carry embedded cover art via ffmpeg.</summary>
    public static bool SupportsCover(string ext) => ext is "mp3" or "m4a" or "flac";
}

public static class OutputNamer
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    /// <summary>
    /// Picks a free "name.ext" (or "name (n).ext") in <paramref name="dir"/> and reserves it so
    /// parallel jobs never write to the same file.
    /// </summary>
    public static string Reserve(string dir, string baseName, string ext)
    {
        Directory.CreateDirectory(dir);
        var name = Sanitize(baseName);
        lock (Gate)
        {
            var path = Path.Combine(dir, $"{name}.{ext}");
            for (var i = 1; File.Exists(path) || Reserved.Contains(path); i++)
                path = Path.Combine(dir, $"{name} ({i}).{ext}");
            Reserved.Add(path);
            return path;
        }
    }

    public static void Release(string path)
    {
        lock (Gate) Reserved.Remove(path);
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrEmpty(clean) ? "output" : clean;
    }
}

public static class JsonExt
{
    public static string? Str(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static double Dbl(this JsonElement e, string name) =>
        double.TryParse(e.Str(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    public static long Lng(this JsonElement e, string name) =>
        long.TryParse(e.Str(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : (long)e.Dbl(name);

    public static int Int(this JsonElement e, string name) => (int)e.Lng(name);

    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
}
