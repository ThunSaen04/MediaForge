using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

public sealed class YouTubeViewModel : Observable
{
    private readonly QueueViewModel _queue;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _analyzeCts;
    private string _analyzedUrls = "";

    public YouTubeViewModel(QueueViewModel queue, AppSettings settings)
    {
        _queue = queue;
        _settings = settings;

        VideoQualities =
        [
            new("Opt_Best", 0), new("2160p (4K)", 2160, false), new("1440p", 1440, false), new("1080p", 1080, false),
            new("720p", 720, false), new("480p", 480, false), new("360p", 360, false),
        ];
        VideoQuality = VideoQualities.First(q => q.Value == 1080);
        Containers = [new("MP4", "mp4", false), new("MKV", "mkv", false), new("WebM", "webm", false)];
        Container = Containers[0];
        AudioFormats =
        [
            new("MP3", "mp3", false), new("M4A (AAC)", "m4a", false), new("Opus", "opus", false),
            new("FLAC", "flac", false), new("WAV", "wav", false), new("Opt_AudioOriginal", "best"),
        ];
        AudioFormat = AudioFormats[0];
        AudioQualities =
        [
            new("320 kbps", "320K", false), new("256 kbps", "256K", false), new("192 kbps", "192K", false),
            new("128 kbps", "128K", false), new("Opt_BestVbr", "0"),
        ];
        AudioQuality = AudioQualities[0];
        OutputFolder = string.IsNullOrWhiteSpace(settings.DownloadFolder) ? AppPaths.DefaultDownloadFolder : settings.DownloadFolder;

        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => !IsBusy);
        CancelAnalyzeCommand = new RelayCommand(() => _analyzeCts?.Cancel(), () => IsBusy);
        PasteCommand = new RelayCommand(Paste);
        SelectAllCommand = new RelayCommand(() => SetSelection(true));
        SelectNoneCommand = new RelayCommand(() => SetSelection(false));
        DownloadCommand = new AsyncCommand(DownloadAsync, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(() =>
        {
            if (Dialogs.OpenFolder(OutputFolder) is { } dir) OutputFolder = dir;
        });
        OpenOutputCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(OutputFolder);
            Dialogs.RevealInExplorer(OutputFolder);
        });

        Entries.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (YtEntry entry in e.NewItems) entry.PropertyChanged += OnEntryChanged;
            RefreshTexts();
        };
        Loc.Instance.LanguageChanged += RefreshTexts;
    }

    public string Urls { get; set => Set(ref field, value); } = "";
    public ObservableCollection<YtEntry> Entries { get; } = [];

    public bool IsBusy
    {
        get;
        private set
        {
            if (Set(ref field, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Status { get; private set => Set(ref field, value); } = "";
    public bool HasEntries => Entries.Count > 0;
    public string DownloadText => Loc.F("Yt_Download", Entries.Count == 0 ? "" : $" ({Entries.Count(e => e.IsSelected)})");
    public string ListSummary => Loc.F("Yt_ListSummary", Entries.Count(e => e.IsSelected), Entries.Count);

    public bool IsVideo
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(IsAudio));
        }
    } = true;

    public bool IsAudio
    {
        get => !IsVideo;
        set => IsVideo = !value;
    }

    public List<Option<int>> VideoQualities { get; }
    public Option<int> VideoQuality { get; set => Set(ref field, value); } = null!;
    public List<Option<string>> Containers { get; }
    public Option<string> Container { get; set => Set(ref field, value); } = null!;
    public List<Option<string>> AudioFormats { get; }

    public Option<string> AudioFormat
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(IsAudioQualityEnabled));
        }
    } = null!;

    public bool IsAudioQualityEnabled => AudioFormat.Value is "mp3" or "m4a" or "opus";
    public List<Option<string>> AudioQualities { get; }
    public Option<string> AudioQuality { get; set => Set(ref field, value); } = null!;

    public bool EmbedThumbnail { get; set => Set(ref field, value); } = true;
    public bool EmbedMetadata { get; set => Set(ref field, value); } = true;
    public bool EmbedSubtitles { get; set => Set(ref field, value); }
    public string SubtitleLanguages { get; set => Set(ref field, value); } = "th,en";
    public bool NumberPrefix { get; set => Set(ref field, value); } = true;
    public bool PlaylistFolder { get; set => Set(ref field, value); } = true;

    public string OutputFolder
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            _settings.DownloadFolder = value;
            _settings.Save();
        }
    } = "";

    public ICommand AnalyzeCommand { get; }
    public ICommand CancelAnalyzeCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand OpenOutputCommand { get; }

    private List<string> ParseUrls() =>
        Urls.Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

    private async Task AnalyzeAsync()
    {
        var urls = ParseUrls();
        if (urls.Count == 0)
        {
            Dialogs.Info(Loc.T("Msg_NoUrl"));
            return;
        }
        if (!await ToolGuard.EnsureYtDlpAsync()) return;

        Entries.Clear();
        IsBusy = true;
        _analyzeCts = new CancellationTokenSource();
        var errors = new List<string>();
        try
        {
            for (var i = 0; i < urls.Count; i++)
            {
                Status = Loc.F("Yt_Analyzing", i + 1, urls.Count);
                try
                {
                    var entries = await Task.Run(() => YtDlp.AnalyzeAsync(urls[i], _analyzeCts.Token));
                    if (entries.Count == 0) errors.Add(Loc.F("Err_NoEntries", urls[i]));
                    foreach (var entry in entries) Entries.Add(entry);
                }
                catch (UserException ex)
                {
                    errors.Add(ex.Message);
                }
            }
            _analyzedUrls = Urls;
            Status = "";
        }
        catch (OperationCanceledException)
        {
            Status = Loc.T("Yt_AnalyzeCanceled");
        }
        finally
        {
            IsBusy = false;
            _analyzeCts.Dispose();
            _analyzeCts = null;
        }

        if (errors.Count > 0) Dialogs.Error(string.Join(Environment.NewLine + Environment.NewLine, errors));
    }

    private async Task DownloadAsync()
    {
        // Analyze implicitly when the user pastes links and hits download straight away.
        if (Entries.Count == 0 || _analyzedUrls != Urls)
        {
            await AnalyzeAsync();
            if (Entries.Count == 0) return;
        }
        if (!await ToolGuard.EnsureYtDlpAsync()) return;

        var selected = Entries.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Dialogs.Info(Loc.T("Msg_NothingSelected"));
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = AppPaths.DefaultDownloadFolder;

        var options = new YtOptions(IsAudio, VideoQuality.Value, Container.Value, AudioFormat.Value, AudioQuality.Value,
            EmbedThumbnail, EmbedMetadata, EmbedSubtitles && IsVideo, SubtitleLanguages.Trim(), NumberPrefix);
        var detail = IsAudio
            ? $"{Loc.T("Yt_Audio")} · {AudioFormat.Label}" + (IsAudioQualityEnabled ? $" · {AudioQuality.Label}" : "")
            : $"{Loc.T("Yt_Video")} · {VideoQuality.Label} · {Container.Label}";

        foreach (var entry in selected)
        {
            var outDir = PlaylistFolder && entry.PlaylistTitle is { } playlist
                ? Path.Combine(OutputFolder, OutputNamer.Sanitize(playlist))
                : OutputFolder;
            _queue.Add(new Job(entry.Title, detail, JobKind.Download,
                (j, ct) => YtDlp.DownloadAsync(entry, options, outDir, j, ct)));
        }
    }

    private void Paste()
    {
        if (!Clipboard.ContainsText()) return;
        var text = Clipboard.GetText().Trim();
        Urls = string.IsNullOrWhiteSpace(Urls) ? text : Urls.TrimEnd() + Environment.NewLine + text;
    }

    private void SetSelection(bool selected)
    {
        foreach (var entry in Entries) entry.IsSelected = selected;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(YtEntry.IsSelected)) RefreshTexts();
    }

    private void RefreshTexts()
    {
        Notify(nameof(HasEntries));
        Notify(nameof(DownloadText));
        Notify(nameof(ListSummary));
    }
}
