using System.Windows.Input;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

public sealed class SettingsViewModel : Observable
{
    private readonly AppSettings _settings;
    private readonly QueueViewModel _queue;

    public SettingsViewModel(AppSettings settings, QueueViewModel queue)
    {
        _settings = settings;
        _queue = queue;

        ParallelChoices = Enumerable.Range(1, 16).ToList();
        DownloadChoices = Enumerable.Range(1, 8).ToList();
        JsRuntimes =
        [
            new("Js_Auto", "auto"), new("Deno", "deno", false), new("Node.js", "node", false), new("Js_None", "none"),
        ];
        JsRuntime = JsRuntimes.FirstOrDefault(j => j.Value == settings.JsRuntime) ?? JsRuntimes[0];

        RefreshCommand = new AsyncCommand(RefreshAsync);
        InstallFfmpegCommand = new AsyncCommand(() => InstallAsync("FFmpeg", Tools.InstallFfmpegAsync));
        InstallYtDlpCommand = new AsyncCommand(() => InstallAsync("yt-dlp", Tools.InstallYtDlpAsync));
        InstallDenoCommand = new AsyncCommand(() => InstallAsync("Deno", Tools.InstallDenoAsync));
        UpdateYtDlpCommand = new AsyncCommand(UpdateYtDlpAsync, () => Tools.YtDlp is not null);
        BrowseFfmpegCommand = new AsyncCommand(async () =>
        {
            if (Dialogs.OpenFile(Dialogs.ExeFilter) is not { } exe) return;
            FfmpegPath = exe;
            await RefreshAsync();
        });
        BrowseYtDlpCommand = new AsyncCommand(async () =>
        {
            if (Dialogs.OpenFile(Dialogs.ExeFilter) is not { } exe) return;
            YtDlpPath = exe;
            await RefreshAsync();
        });
        OpenToolsFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.ToolsDir);
            Dialogs.RevealInExplorer(AppPaths.ToolsDir);
        });
        OpenWebsiteCommand = new RelayCommand(() => Dialogs.OpenUrl(AppInfo.Website));

        _ = RefreshAsync();
    }

    public List<int> ParallelChoices { get; }
    public List<int> DownloadChoices { get; }

    public int MaxParallelJobs
    {
        get => _settings.MaxParallelJobs;
        set
        {
            _settings.MaxParallelJobs = value;
            _settings.Save();
            Notify();
            _queue.Pump();
        }
    }

    public int MaxParallelDownloads
    {
        get => _settings.MaxParallelDownloads;
        set
        {
            _settings.MaxParallelDownloads = value;
            _settings.Save();
            Notify();
            _queue.Pump();
        }
    }

    public List<Option<string>> JsRuntimes { get; }

    public Option<string> JsRuntime
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            _settings.JsRuntime = value.Value;
            _settings.Save();
        }
    } = null!;

    public string FfmpegPath
    {
        get => _settings.FfmpegPath;
        set
        {
            _settings.FfmpegPath = value.Trim();
            _settings.Save();
            Notify();
        }
    }

    public string YtDlpPath
    {
        get => _settings.YtDlpPath;
        set
        {
            _settings.YtDlpPath = value.Trim();
            _settings.Save();
            Notify();
        }
    }

    public string FfmpegStatus { get; private set => Set(ref field, value); } = "";
    public string YtDlpStatus { get; private set => Set(ref field, value); } = "";
    public string JsStatus { get; private set => Set(ref field, value); } = "";
    public string ToolsFolder => AppPaths.ToolsDir;

    public ICommand RefreshCommand { get; }
    public ICommand InstallFfmpegCommand { get; }
    public ICommand InstallYtDlpCommand { get; }
    public ICommand InstallDenoCommand { get; }
    public ICommand UpdateYtDlpCommand { get; }
    public ICommand BrowseFfmpegCommand { get; }
    public ICommand BrowseYtDlpCommand { get; }
    public ICommand OpenToolsFolderCommand { get; }
    public ICommand OpenWebsiteCommand { get; }

    public async Task RefreshAsync()
    {
        FfmpegStatus = YtDlpStatus = JsStatus = Loc.T("Status_Checking");

        var ffmpeg = Tools.Ffmpeg;
        var ffmpegVersion = await Tools.VersionAsync(ffmpeg, "-version");
        FfmpegStatus = ffmpeg is null
            ? Loc.T("Status_NotFound")
            : $"{Shorten(ffmpegVersion)}{Environment.NewLine}{ffmpeg}" + (Tools.Ffprobe is null ? $"{Environment.NewLine}{Loc.T("Status_NoFfprobe")}" : "");

        var ytdlp = Tools.YtDlp;
        var ytdlpVersion = await Tools.VersionAsync(ytdlp, "--version");
        YtDlpStatus = ytdlp is null ? Loc.T("Status_NotFound") : $"{ytdlpVersion}{Environment.NewLine}{ytdlp}";

        var deno = Tools.Deno;
        var node = Tools.Node;
        var lines = new List<string>();
        if (deno is not null) lines.Add($"Deno: {await Tools.VersionAsync(deno, "--version")} — {deno}");
        if (node is not null) lines.Add($"Node.js: {await Tools.VersionAsync(node, "--version")} — {node}");
        JsStatus = lines.Count == 0 ? Loc.T("Status_NoJs") : string.Join(Environment.NewLine, lines);
    }

    private static string? Shorten(string? version)
    {
        if (version is null) return null;
        var copyright = version.IndexOf(" Copyright", StringComparison.Ordinal);
        return copyright > 0 ? version[..copyright] : version;
    }

    private async Task InstallAsync(string name, Func<IProgress<DownloadProgress>, CancellationToken, Task> install)
    {
        if (await ToolGuard.InstallAsync(name, install))
        {
            await RefreshAsync();
            Dialogs.Info(Loc.F("Msg_ToolInstalled", name));
        }
    }

    private async Task UpdateYtDlpAsync()
    {
        YtDlpStatus = Loc.T("Status_Updating");
        try
        {
            var output = await YtDlp.UpdateAsync(CancellationToken.None);
            Dialogs.Info(output);
        }
        finally
        {
            await RefreshAsync();
        }
    }
}
