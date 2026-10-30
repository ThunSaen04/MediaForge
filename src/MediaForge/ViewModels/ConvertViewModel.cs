using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

public sealed class MediaFile(string path)
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public string Folder { get; } = System.IO.Path.GetDirectoryName(path) ?? "";
    public string SizeText { get; } = Fmt.Bytes(new FileInfo(path).Length);
}

public sealed class ConvertViewModel : Observable
{
    private readonly QueueViewModel _queue;
    private readonly AppSettings _settings;

    public ConvertViewModel(QueueViewModel queue, AppSettings settings)
    {
        _queue = queue;
        _settings = settings;

        AudioFormats = AudioFormat.All.Select(f => new Option<AudioFormat>(f.Label, f, localize: f.IsCopy)).ToList();
        SelectedAudioFormat = AudioFormats[0];
        Bitrates = new[] { 64, 96, 128, 160, 192, 256, 320 }.Select(b => new Option<int>($"{b} kbps", b, false)).ToList();
        Bitrate = Bitrates.First(b => b.Value == 192);
        SampleRates =
        [
            new("Opt_Keep", 0), new("22050 Hz", 22050, false), new("44100 Hz", 44100, false),
            new("48000 Hz", 48000, false), new("96000 Hz", 96000, false),
        ];
        SampleRate = SampleRates[0];
        ChannelOptions = [new("Opt_Keep", 0), new("Opt_Mono", 1), new("Opt_Stereo", 2)];
        Channels = ChannelOptions[0];

        VideoFormats = VideoFormat.All.Select(f => new Option<VideoFormat>(f.Label, f, localize: f.IsRemux)).ToList();
        SelectedVideoFormat = VideoFormats[0];
        Encoders = [new("Enc_Cpu", HwEncoder.Cpu)];
        Encoder = Encoders[0];
        Qualities = [new("Q_VeryHigh", Quality.VeryHigh), new("Q_High", Quality.High), new("Q_Medium", Quality.Medium), new("Q_Low", Quality.Low)];
        VideoQuality = Qualities[1];
        Speeds = [new("Speed_Fast", EncodeSpeed.Fast), new("Speed_Balanced", EncodeSpeed.Balanced), new("Speed_Small", EncodeSpeed.Small)];
        Speed = Speeds[1];
        Resolutions =
        [
            new("Opt_Keep", 0), new("2160p (4K)", 2160, false), new("1440p", 1440, false), new("1080p", 1080, false),
            new("720p", 720, false), new("480p", 480, false), new("360p", 360, false),
        ];
        Resolution = Resolutions[0];
        FpsOptions = [new("Opt_Keep", 0), new("24", 24, false), new("25", 25, false), new("30", 30, false), new("50", 50, false), new("60", 60, false)];
        Fps = FpsOptions[0];
        VideoAudioBitrates = new[] { 96, 128, 160, 192, 256, 320 }.Select(b => new Option<int>($"{b} kbps", b, false)).ToList();
        VideoAudioBitrate = VideoAudioBitrates.First(b => b.Value == 192);

        OutputFolder = settings.ConvertOutputFolder;
        UseSourceFolder = string.IsNullOrWhiteSpace(OutputFolder);

        AddFilesCommand = new RelayCommand(() => AddPaths(Dialogs.OpenFiles(Dialogs.MediaFilter, true)));
        AddFolderCommand = new RelayCommand(() => { if (Dialogs.OpenFolder() is { } dir) AddPaths([dir]); });
        RemoveSelectedCommand = new RelayCommand(p =>
        {
            if (p is IList items)
                foreach (var file in items.OfType<MediaFile>().ToList()) Files.Remove(file);
        });
        ClearCommand = new RelayCommand(Files.Clear);
        BrowseOutputCommand = new RelayCommand(() =>
        {
            if (Dialogs.OpenFolder(OutputFolder) is { } dir)
            {
                OutputFolder = dir;
                UseSourceFolder = false;
            }
        });
        StartCommand = new AsyncCommand(StartAsync);

        Files.CollectionChanged += (_, _) => RefreshTexts();
        Loc.Instance.LanguageChanged += RefreshTexts;
        _ = DetectEncodersAsync();
    }

    public ObservableCollection<MediaFile> Files { get; } = [];
    public bool IsEmpty => Files.Count == 0;
    public string StartText => Loc.F("Convert_Start", Files.Count);
    public string FilesSummary => Loc.F("Convert_FileCount", Files.Count);

    public bool IncludeSubfolders { get; set => Set(ref field, value); } = true;

    public bool IsAudioMode
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(IsVideoMode));
        }
    } = true;

    public bool IsVideoMode
    {
        get => !IsAudioMode;
        set => IsAudioMode = !value;
    }

    // ── Audio ──
    public List<Option<AudioFormat>> AudioFormats { get; }

    public Option<AudioFormat> SelectedAudioFormat
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            Notify(nameof(IsBitrateEnabled));
            Notify(nameof(IsAudioEncodeEnabled));
        }
    } = null!;

    public bool IsBitrateEnabled => SelectedAudioFormat.Value.Lossy;
    public bool IsAudioEncodeEnabled => !SelectedAudioFormat.Value.IsCopy;
    public List<Option<int>> Bitrates { get; }
    public Option<int> Bitrate { get; set => Set(ref field, value); } = null!;
    public List<Option<int>> SampleRates { get; }
    public Option<int> SampleRate { get; set => Set(ref field, value); } = null!;
    public List<Option<int>> ChannelOptions { get; }
    public Option<int> Channels { get; set => Set(ref field, value); } = null!;

    // ── Video ──
    public List<Option<VideoFormat>> VideoFormats { get; }

    public Option<VideoFormat> SelectedVideoFormat
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            Notify(nameof(IsEncoderEnabled));
            Notify(nameof(IsReencode));
        }
    } = null!;

    public bool IsEncoderEnabled => SelectedVideoFormat.Value.SupportsHardware;
    public bool IsReencode => !SelectedVideoFormat.Value.IsRemux;
    public ObservableCollection<Option<HwEncoder>> Encoders { get; }
    public Option<HwEncoder> Encoder { get; set => Set(ref field, value); } = null!;
    public List<Option<Quality>> Qualities { get; }
    public Option<Quality> VideoQuality { get; set => Set(ref field, value); } = null!;
    public List<Option<EncodeSpeed>> Speeds { get; }
    public Option<EncodeSpeed> Speed { get; set => Set(ref field, value); } = null!;
    public List<Option<int>> Resolutions { get; }
    public Option<int> Resolution { get; set => Set(ref field, value); } = null!;
    public List<Option<int>> FpsOptions { get; }
    public Option<int> Fps { get; set => Set(ref field, value); } = null!;
    public List<Option<int>> VideoAudioBitrates { get; }
    public Option<int> VideoAudioBitrate { get; set => Set(ref field, value); } = null!;
    public bool RemoveAudio { get; set => Set(ref field, value); }

    // ── Output ──
    public bool UseSourceFolder
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(UseCustomFolder));
        }
    }

    public bool UseCustomFolder
    {
        get => !UseSourceFolder;
        set => UseSourceFolder = !value;
    }

    public string OutputFolder { get; set => Set(ref field, value); } = "";

    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand StartCommand { get; }

    public void AddPaths(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = IncludeSubfolders, IgnoreInaccessible = true };
                foreach (var file in Directory.EnumerateFiles(path, "*", options).Order(StringComparer.OrdinalIgnoreCase))
                    if (MediaTypes.IsMedia(file) && known.Add(file))
                        Files.Add(new MediaFile(file));
            }
            else if (File.Exists(path) && known.Add(path))
            {
                // Files picked explicitly are accepted even with unusual extensions; ffmpeg decides.
                Files.Add(new MediaFile(path));
            }
        }
    }

    private async Task StartAsync()
    {
        if (Files.Count == 0)
        {
            Dialogs.Info(Loc.T("Msg_NoFiles"));
            return;
        }
        if (UseCustomFolder && string.IsNullOrWhiteSpace(OutputFolder))
        {
            Dialogs.Info(Loc.T("Msg_ChooseOutput"));
            return;
        }
        if (!await ToolGuard.EnsureFfmpegAsync()) return;

        _settings.ConvertOutputFolder = UseCustomFolder ? OutputFolder : "";
        _settings.Save();

        foreach (var file in Files)
        {
            var outDir = UseCustomFolder ? OutputFolder : file.Folder;
            Job job;
            if (IsAudioMode)
            {
                var options = new AudioOptions(SelectedAudioFormat.Value, Bitrate.Value, SampleRate.Value, Channels.Value);
                var detail = "→ " + SelectedAudioFormat.Label + (options.Format.Lossy ? $" · {options.Bitrate} kbps" : "");
                job = new Job(file.Name, detail, JobKind.Process,
                    (j, ct) => Converter.ToAudioAsync(file.Path, outDir, options, j, ct));
            }
            else
            {
                var options = new VideoOptions(SelectedVideoFormat.Value, Encoder.Value, VideoQuality.Value, Speed.Value,
                    Resolution.Value, Fps.Value, VideoAudioBitrate.Value, RemoveAudio);
                var detail = "→ " + SelectedVideoFormat.Label;
                if (!options.Format.IsRemux)
                    detail += $" · {VideoQuality.Label}" + (options.MaxHeight > 0 ? $" · {options.MaxHeight}p" : "") +
                              (options.Format.SupportsHardware && options.Encoder != HwEncoder.Cpu ? $" · {Encoder.Label}" : "");
                job = new Job(file.Name, detail, JobKind.Process,
                    (j, ct) => Converter.ToVideoAsync(file.Path, outDir, options, j, ct));
            }
            _queue.Add(job);
        }
    }

    private async Task DetectEncodersAsync()
    {
        var found = await Tools.DetectHardwareEncodersAsync();
        foreach (var encoder in found)
        {
            Encoders.Add(encoder switch
            {
                HwEncoder.Nvidia => new Option<HwEncoder>("NVIDIA NVENC (GPU)", encoder, false),
                HwEncoder.Intel => new Option<HwEncoder>("Intel Quick Sync (GPU)", encoder, false),
                _ => new Option<HwEncoder>("AMD AMF (GPU)", encoder, false),
            });
        }
    }

    private void RefreshTexts()
    {
        Notify(nameof(IsEmpty));
        Notify(nameof(StartText));
        Notify(nameof(FilesSummary));
    }
}
