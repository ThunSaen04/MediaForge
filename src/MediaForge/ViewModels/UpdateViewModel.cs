using System.Windows;
using System.Windows.Input;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

/// <summary>Update check (on startup and on demand), the update banner and the download-and-install flow.</summary>
public sealed class UpdateViewModel : Observable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly AppSettings _settings;
    private readonly QueueViewModel _queue;
    private CancellationTokenSource? _download;
    private string _statusKey = "Upd_NotChecked";
    private object[] _statusArgs = [];

    public UpdateViewModel(AppSettings settings, QueueViewModel queue)
    {
        _settings = settings;
        _queue = queue;
        CheckCommand = new AsyncCommand(CheckAsync, () => !IsChecking && !IsDownloading);
        InstallCommand = new AsyncCommand(InstallAsync, () => Available is not null && !IsDownloading);
        ViewReleaseCommand = new RelayCommand(() => Dialogs.OpenUrl(Available?.PageUrl ?? Updater.ReleasesPage));
        DismissCommand = new RelayCommand(() => IsBannerVisible = false);
        CancelDownloadCommand = new RelayCommand(() => _download?.Cancel());

        Loc.Instance.LanguageChanged += () =>
        {
            Notify(nameof(Status));
            Notify(nameof(BannerTitle));
            Notify(nameof(BannerDetail));
        };
    }

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand ViewReleaseCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand CancelDownloadCommand { get; }

    public bool AutoCheck
    {
        get => _settings.AutoCheckUpdates;
        set
        {
            _settings.AutoCheckUpdates = value;
            _settings.Save();
            Notify();
        }
    }

    public UpdateInfo? Available
    {
        get;
        private set
        {
            if (!Set(ref field, value)) return;
            Notify(nameof(BannerTitle));
            Notify(nameof(BannerDetail));
            Notify(nameof(Notes));
            // The startup check runs outside any command, so WPF would not re-evaluate "Update now" by itself.
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBannerVisible { get; private set => Set(ref field, value); }
    public bool IsChecking { get; private set => Set(ref field, value); }
    public bool IsDownloading { get; private set => Set(ref field, value); }

    /// <summary>True once the installer has been started; the window then closes without asking.</summary>
    public bool IsInstalling { get; private set; }

    public double Progress { get; private set => Set(ref field, value); }
    public string ProgressText { get; private set => Set(ref field, value); } = "";

    public string Status => Loc.F(_statusKey, _statusArgs);

    public string BannerTitle => Available is { } u ? Loc.F("Upd_Available", u.Version.ToString(3)) : "";

    public string BannerDetail => Available is { } u ? Loc.F("Upd_AvailableDetail", AppInfo.Version, Fmt.Bytes(u.Size)) : "";

    public string Notes => Available?.Notes ?? "";

    /// <summary>Quiet check after start-up, at most once per <see cref="CheckInterval"/>.</summary>
    public async Task StartupCheckAsync()
    {
        if (!AutoCheck) return;
        if (_settings.LastUpdateCheck is { } last && DateTime.UtcNow - last < CheckInterval) return;
        // Let the window finish loading before touching the network.
        await Task.Delay(TimeSpan.FromSeconds(3));
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        IsChecking = true;
        SetStatus("Upd_Checking");
        try
        {
            var update = await Updater.CheckAsync(CancellationToken.None);
            _settings.LastUpdateCheck = DateTime.UtcNow;
            _settings.Save();
            Available = update;
            if (update is null)
            {
                SetStatus("Upd_UpToDate", AppInfo.Version);
            }
            else
            {
                SetStatus("Upd_Available", update.Version.ToString(3));
                IsBannerVisible = true;
            }
        }
        catch (Exception ex)
        {
            // Offline or GitHub unreachable: report it in Settings, never interrupt the user.
            SetStatus("Upd_CheckFailed", ex is TimeoutException ? "timeout" : ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task InstallAsync()
    {
        if (Available is not { } update) return;
        if (_queue.HasActive && !Dialogs.Confirm(Loc.T("Ask_ExitWithJobs"))) return;

        IsBannerVisible = true;
        IsDownloading = true;
        Progress = 0;
        ProgressText = "";
        _download = new CancellationTokenSource();
        try
        {
            var installer = await Updater.DownloadAsync(update, new Progress<DownloadProgress>(ReportProgress), _download.Token);
            _queue.CancelAll();
            IsInstalling = true;
            Updater.LaunchInstaller(installer);
            // The installer waits for this process to exit before replacing MediaForge.exe.
            Application.Current.Shutdown();
        }
        finally
        {
            IsDownloading = false;
            _download.Dispose();
            _download = null;
        }
    }

    private void ReportProgress(DownloadProgress p)
    {
        var total = p.Total ?? Available?.Size ?? 0;
        Progress = total > 0 ? p.Done * 100.0 / total : 0;
        ProgressText = total > 0 ? $"{Fmt.Bytes(p.Done)} / {Fmt.Bytes(total)}" : Fmt.Bytes(p.Done);
    }

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        Notify(nameof(Status));
    }
}
