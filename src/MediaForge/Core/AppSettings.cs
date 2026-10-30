using System.Text.Json;

namespace MediaForge.Core;

public static class AppPaths
{
    public static string LocalDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaForge");

    public static string RoamingDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaForge");

    public static string ToolsDir { get; } = Path.Combine(LocalDir, "tools");

    public static string TempDir
    {
        get
        {
            var dir = Path.Combine(Path.GetTempPath(), "MediaForge");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DefaultDownloadFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MediaForge");
}

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(AppPaths.RoamingDir, "settings.json");

    /// <summary>The settings instance shared by services that have no direct reference to it.</summary>
    public static AppSettings Current { get; private set; } = new();

    public string Language { get; set; } = "th";
    public string Theme { get; set; } = "System";
    public string DownloadFolder { get; set; } = AppPaths.DefaultDownloadFolder;
    public string ConvertOutputFolder { get; set; } = "";
    public int MaxParallelJobs { get; set; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
    public int MaxParallelDownloads { get; set; } = 3;
    public string FfmpegPath { get; set; } = "";
    public string YtDlpPath { get; set; } = "";

    /// <summary>auto | deno | node | none</summary>
    public string JsRuntime { get; set; } = "auto";

    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? settings;
        }
        catch
        {
            // A corrupt settings file falls back to defaults.
        }
        Current = settings;
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.RoamingDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Settings are a convenience; failing to persist them must not break the app.
        }
    }
}
