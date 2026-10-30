using System.Collections.ObjectModel;
using System.Windows.Input;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

public sealed class EditViewModel : Observable
{
    public EditViewModel(QueueViewModel queue)
    {
        Queue = queue;
        Trim = new TrimViewModel(this);
        Merge = new MergeViewModel(this);
        Volume = new VolumeViewModel(this);
        Speed = new SpeedViewModel(this);
        Gif = new GifViewModel(this);
    }

    public QueueViewModel Queue { get; }

    /// <summary>Empty means "next to the input file".</summary>
    public string OutputFolder { get; set => Set(ref field, value); } = "";

    public TrimViewModel Trim { get; }
    public MergeViewModel Merge { get; }
    public VolumeViewModel Volume { get; }
    public SpeedViewModel Speed { get; }
    public GifViewModel Gif { get; }

    public string ResolveOutputDir(string input) =>
        string.IsNullOrWhiteSpace(OutputFolder) ? Path.GetDirectoryName(input)! : OutputFolder;
}

/// <summary>Base for tools that take one input file and probe it on selection.</summary>
public abstract class SingleInputTool : Observable
{
    protected SingleInputTool(EditViewModel owner)
    {
        Owner = owner;
        StartCommand = new AsyncCommand(StartAsync);
    }

    protected EditViewModel Owner { get; }

    public ICommand StartCommand { get; }

    public string Input
    {
        get;
        set
        {
            if (Set(ref field, value)) _ = LoadInfoAsync();
        }
    } = "";

    public string InputInfo { get; private set => Set(ref field, value); } = "";

    protected MediaInfo? Info { get; private set; }

    private async Task LoadInfoAsync()
    {
        Info = null;
        InputInfo = "";
        var path = Input;
        if (!File.Exists(path) || Tools.Ffprobe is null) return;
        try
        {
            var info = await MediaProbe.RunAsync(path);
            if (path != Input) return;
            Info = info;
            InputInfo = Loc.F("Info_Summary", Fmt.Duration(info.Duration),
                info.Video is { } v ? $"{v.Width}x{v.Height}" : Loc.T("Info_AudioOnly"), Fmt.Bytes(info.Size));
            OnInfoLoaded(info);
        }
        catch (Exception ex)
        {
            InputInfo = ex.Message;
        }
    }

    protected virtual void OnInfoLoaded(MediaInfo info) { }

    private async Task StartAsync()
    {
        if (!File.Exists(Input))
        {
            Dialogs.Info(Loc.T("Msg_ChooseInput"));
            return;
        }
        if (!await ToolGuard.EnsureFfmpegAsync()) return;
        if (CreateJob(Input, Owner.ResolveOutputDir(Input)) is { } job) Owner.Queue.Add(job);
    }

    /// <summary>Validates the form and builds the job, or returns null after telling the user what is wrong.</summary>
    protected abstract Job? CreateJob(string input, string outDir);

    protected static bool TryTime(string text, out TimeSpan value)
    {
        if (TimeParser.TryParse(text, out value)) return true;
        Dialogs.Info(Loc.F("Msg_BadTime", text));
        return false;
    }
}

public sealed class TrimViewModel(EditViewModel owner) : SingleInputTool(owner)
{
    public string StartTime { get; set => Set(ref field, value); } = "0:00:00";
    public string EndTime { get; set => Set(ref field, value); } = "";

    public bool Precise
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(Fast));
        }
    }

    public bool Fast
    {
        get => !Precise;
        set => Precise = !value;
    }

    protected override void OnInfoLoaded(MediaInfo info)
    {
        StartTime = "0:00:00";
        EndTime = Fmt.Clock(info.Duration);
    }

    protected override Job? CreateJob(string input, string outDir)
    {
        if (!TryTime(StartTime, out var start)) return null;
        TimeSpan? end = null;
        if (!string.IsNullOrWhiteSpace(EndTime))
        {
            if (!TryTime(EndTime, out var e)) return null;
            end = e;
        }
        if (end <= start)
        {
            Dialogs.Info(Loc.T("Err_TrimRange"));
            return null;
        }

        var precise = Precise;
        var detail = Loc.F("Detail_Trim", Fmt.Clock(start.TotalSeconds), end is { } x ? Fmt.Clock(x.TotalSeconds) : "∞");
        return new Job(Path.GetFileName(input), detail, JobKind.Process,
            (j, ct) => Converter.TrimAsync(input, start, end, precise, outDir, j, ct));
    }
}

public sealed class MergeViewModel : Observable
{
    private readonly EditViewModel _owner;

    public MergeViewModel(EditViewModel owner)
    {
        _owner = owner;
        Formats =
        [
            new("Opt_SameAsFirst", ""), new("MP4", "mp4", false), new("MKV", "mkv", false), new("MP3", "mp3", false),
            new("M4A", "m4a", false), new("WAV", "wav", false), new("FLAC", "flac", false),
        ];
        Format = Formats[0];

        AddCommand = new RelayCommand(() =>
        {
            foreach (var file in Dialogs.OpenFiles(Dialogs.MediaFilter, true)) Files.Add(file);
        });
        RemoveCommand = new RelayCommand(() => { if (SelectedIndex >= 0) Files.RemoveAt(SelectedIndex); });
        UpCommand = new RelayCommand(() => Move(-1));
        DownCommand = new RelayCommand(() => Move(+1));
        ClearCommand = new RelayCommand(Files.Clear);
        StartCommand = new AsyncCommand(StartAsync);
    }

    public ObservableCollection<string> Files { get; } = [];
    public int SelectedIndex { get; set => Set(ref field, value); } = -1;

    public bool Reencode
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(Fast));
        }
    }

    public bool Fast
    {
        get => !Reencode;
        set => Reencode = !value;
    }

    public List<Option<string>> Formats { get; }
    public Option<string> Format { get; set => Set(ref field, value); } = null!;
    public string OutputName { get; set => Set(ref field, value); } = "merged";

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand StartCommand { get; }

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists)) Files.Add(path);
    }

    private void Move(int delta)
    {
        var from = SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Files.Count) return;
        Files.Move(from, to);
        SelectedIndex = to;
    }

    private async Task StartAsync()
    {
        if (Files.Count < 2)
        {
            Dialogs.Info(Loc.T("Msg_MergeNeedTwo"));
            return;
        }
        if (!await ToolGuard.EnsureFfmpegAsync()) return;

        var inputs = Files.ToList();
        var ext = string.IsNullOrEmpty(Format.Value) ? MediaTypes.Ext(inputs[0]) : Format.Value;
        var name = string.IsNullOrWhiteSpace(OutputName) ? "merged" : OutputName.Trim();
        var outDir = _owner.ResolveOutputDir(inputs[0]);
        var reencode = Reencode;
        var detail = Loc.F("Detail_Merge", inputs.Count, ext.ToUpperInvariant());
        _owner.Queue.Add(new Job($"{name}.{ext}", detail, JobKind.Process,
            (j, ct) => Converter.MergeAsync(inputs, reencode, outDir, name, ext, j, ct)));
    }
}

public sealed class VolumeViewModel : SingleInputTool
{
    public VolumeViewModel(EditViewModel owner) : base(owner)
    {
        Targets =
        [
            new("Lufs_Streaming", -14.0), new("Lufs_Podcast", -16.0), new("Lufs_Broadcast", -23.0),
        ];
        Target = Targets[0];
        Loc.Instance.LanguageChanged += () => Notify(nameof(GainText));
    }

    public bool Normalize
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(Adjust));
        }
    } = true;

    public bool Adjust
    {
        get => !Normalize;
        set => Normalize = !value;
    }

    public double GainDb
    {
        get;
        set
        {
            if (Set(ref field, Math.Round(value * 2) / 2)) Notify(nameof(GainText));
        }
    } = 3;

    public string GainText => $"{GainDb:+0.0;-0.0;0} dB";

    public List<Option<double>> Targets { get; }
    public Option<double> Target { get; set => Set(ref field, value); } = null!;

    protected override Job? CreateJob(string input, string outDir)
    {
        var normalize = Normalize;
        var gain = GainDb;
        var target = Target.Value;
        var detail = normalize ? Loc.F("Detail_Normalize", Fmt.Num(target)) : Loc.F("Detail_Gain", GainText);
        return new Job(Path.GetFileName(input), detail, JobKind.Process,
            (j, ct) => Converter.VolumeAsync(input, normalize, gain, target, outDir, j, ct));
    }
}

public sealed class SpeedViewModel : SingleInputTool
{
    public SpeedViewModel(EditViewModel owner) : base(owner)
    {
        Factors = new[] { 0.25, 0.5, 0.75, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0 }
            .Select(f => new Option<double>($"{Fmt.Num(f)}x", f, false)).ToList();
        Factor = Factors.First(f => f.Value == 1.5);
    }

    public List<Option<double>> Factors { get; }
    public Option<double> Factor { get; set => Set(ref field, value); } = null!;

    protected override Job? CreateJob(string input, string outDir)
    {
        var factor = Factor.Value;
        return new Job(Path.GetFileName(input), Loc.F("Detail_Speed", Fmt.Num(factor)), JobKind.Process,
            (j, ct) => Converter.SpeedAsync(input, factor, outDir, j, ct));
    }
}

public sealed class GifViewModel : SingleInputTool
{
    public GifViewModel(EditViewModel owner) : base(owner)
    {
        FpsOptions = new[] { 10, 12, 15, 20, 24, 30 }.Select(f => new Option<int>($"{f} fps", f, false)).ToList();
        Fps = FpsOptions.First(f => f.Value == 15);
        Widths =
        [
            new("320 px", 320, false), new("480 px", 480, false), new("640 px", 640, false),
            new("800 px", 800, false), new("Opt_Keep", 0),
        ];
        Width = Widths[1];
    }

    public string StartTime { get; set => Set(ref field, value); } = "0:00:00";
    public string Duration { get; set => Set(ref field, value); } = "5";
    public List<Option<int>> FpsOptions { get; }
    public Option<int> Fps { get; set => Set(ref field, value); } = null!;
    public List<Option<int>> Widths { get; }
    public Option<int> Width { get; set => Set(ref field, value); } = null!;

    protected override Job? CreateJob(string input, string outDir)
    {
        if (!TryTime(StartTime, out var start)) return null;
        if (!TryTime(Duration, out var length)) return null;
        if (length <= TimeSpan.Zero)
        {
            Dialogs.Info(Loc.F("Msg_BadTime", Duration));
            return null;
        }
        var fps = Fps.Value;
        var width = Width.Value;
        var detail = Loc.F("Detail_Gif", Fmt.Clock(start.TotalSeconds), Fmt.Num(length.TotalSeconds), fps);
        return new Job(Path.GetFileName(input), detail, JobKind.Process,
            (j, ct) => Converter.GifAsync(input, start, length.TotalSeconds, fps, width, outDir, j, ct));
    }
}
