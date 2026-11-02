using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

/// <summary>
/// Multi-range trim: a list of kept ranges edited on a waveform timeline, with playback of the
/// source, of the result (cut parts skipped), of a selection and of each join.
/// </summary>
public sealed class TrimViewModel : SingleInputTool
{
    private const double JoinPreview = 2.5;

    private readonly PreviewPlayer _player = new();
    private readonly DispatcherTimer _timer;
    private readonly Stack<IReadOnlyList<TimeRange>> _undo = new();
    private DateTime _lastRangeChange;
    private bool _restoring;

    public TrimViewModel(EditViewModel owner) : base(owner)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) =>
        {
            if (_player.Position is { } t) Playhead = Math.Min(t, Duration);
        };
        _player.Finished += () => IsPlaying = false;

        PlayPauseCommand = new RelayCommand(PlayPause, () => Duration > 0);
        PlaySelectionCommand = new RelayCommand(PlaySelection, () => Duration > 0);
        PlayJoinCommand = new RelayCommand(PlayJoin, () => Ranges.Count > 0);
        SeekCommand = new RelayCommand(p => Seek(Convert.ToDouble(p)));
        MarkInCommand = new RelayCommand(() => MarkIn(Playhead), () => Duration > 0);
        MarkOutCommand = new RelayCommand(() => MarkOut(Playhead), () => Duration > 0);
        KeepSelectionCommand = new RelayCommand(() => ApplySelection(sel => TimeRanges.Add(Ranges, sel, Duration)), () => HasSelection);
        CutSelectionCommand = new RelayCommand(() => ApplySelection(sel => TimeRanges.Subtract(Ranges, sel, Duration)), () => HasSelection);
        KeepOnlySelectionCommand = new RelayCommand(() => ApplySelection(sel => TimeRanges.Normalize([sel], Duration)), () => HasSelection);
        ResetRangesCommand = new RelayCommand(() => Edit([new TimeRange(0, Duration)]), () => Duration > 0);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);

        Loc.Instance.LanguageChanged += RefreshTexts;
        RefreshTexts();
    }

    // ───────────── state ─────────────

    public double Duration
    {
        get;
        private set
        {
            if (Set(ref field, value)) Notify(nameof(PositionText));
        }
    }

    public AudioPreview? Preview { get; private set => Set(ref field, value); }

    /// <summary>Ranges to keep, sorted and non-overlapping. The timeline edits this directly.</summary>
    public IReadOnlyList<TimeRange> Ranges
    {
        get;
        set
        {
            var old = field;
            if (!Set(ref field, value)) return;
            // The timeline sets this on every step of an edge drag; one undo step per gesture is enough.
            if (!_restoring && old.Count > 0 && DateTime.UtcNow - _lastRangeChange > TimeSpan.FromMilliseconds(400))
                _undo.Push(old);
            _lastRangeChange = DateTime.UtcNow;
            SyncRows();
            RefreshTexts();
            CommandManager.InvalidateRequerySuggested();
        }
    } = [];

    public ObservableCollection<TrimRangeRow> Rows { get; } = [];

    public double SelectionStart
    {
        get;
        set
        {
            if (Set(ref field, value)) SelectionChanged();
        }
    } = double.NaN;

    public double SelectionEnd
    {
        get;
        set
        {
            if (Set(ref field, value)) SelectionChanged();
        }
    } = double.NaN;

    public bool HasSelection => TryGetSelection(out _);

    public double Playhead
    {
        get;
        private set
        {
            if (Set(ref field, value)) Notify(nameof(PositionText));
        }
    }

    public bool IsPlaying
    {
        get;
        private set
        {
            if (!Set(ref field, value)) return;
            Notify(nameof(PlayGlyph));
            if (value) _timer.Start();
            else _timer.Stop();
        }
    }

    /// <summary>Play or pause icon (Segoe Fluent Icons).</summary>
    public string PlayGlyph => IsPlaying ? "" : "";

    /// <summary>When on, Play skips the parts that will be cut, so you hear the output.</summary>
    public bool SkipCuts { get; set => Set(ref field, value); } = true;

    public string PositionText => $"{Time(Playhead)} / {Time(Duration)}";
    public string WaveStatus { get; private set => Set(ref field, value); } = "";
    public string Summary { get; private set => Set(ref field, value); } = "";
    public string SelectionText { get; private set => Set(ref field, value); } = "";

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

    public ICommand PlayPauseCommand { get; }
    public ICommand PlaySelectionCommand { get; }
    public ICommand PlayJoinCommand { get; }
    public ICommand SeekCommand { get; }
    public ICommand MarkInCommand { get; }
    public ICommand MarkOutCommand { get; }
    public ICommand KeepSelectionCommand { get; }
    public ICommand CutSelectionCommand { get; }
    public ICommand KeepOnlySelectionCommand { get; }
    public ICommand ResetRangesCommand { get; }
    public ICommand UndoCommand { get; }

    // ───────────── loading ─────────────

    protected override void OnInputChanged()
    {
        StopPlayback();
        Preview?.Dispose();
        Preview = null;
        _undo.Clear();
        _restoring = true;
        Ranges = [];
        _restoring = false;
        Duration = 0;
        Playhead = 0;
        ClearSelection();
        WaveStatus = File.Exists(Input) ? Loc.F("Wave_Loading", 0) : Loc.T("Wave_PickFile");
    }

    protected override void OnInfoLoaded(MediaInfo info)
    {
        Duration = info.Duration;
        _restoring = true;
        Ranges = Duration > 0 ? [new TimeRange(0, Duration)] : [];
        _restoring = false;
        _undo.Clear();

        if (!info.HasAudio || Tools.Ffmpeg is null)
        {
            WaveStatus = Loc.T("Wave_NoAudio");
            return;
        }
        var preview = AudioPreview.Start(Input, Duration);
        preview.Progress += () => Application.Current?.Dispatcher.BeginInvoke(() => UpdateWaveStatus(preview));
        Preview = preview;
    }

    private void UpdateWaveStatus(AudioPreview preview)
    {
        if (!ReferenceEquals(preview, Preview)) return;
        if (preview.IsComplete)
        {
            WaveStatus = preview.Error is { } err && preview.DecodedFrames == 0 ? Loc.F("Wave_Error", err) : "";
            // ffprobe can miss the length of some streams (raw AAC, broken headers); trust the decode then.
            if (Duration <= 0 && preview.DecodedSeconds > 0)
            {
                Duration = preview.DecodedSeconds;
                Ranges = [new TimeRange(0, Duration)];
                _undo.Clear();
            }
            return;
        }
        var percent = Duration > 0 ? Math.Clamp((int)(preview.DecodedSeconds / Duration * 100), 0, 99) : 0;
        WaveStatus = Loc.F("Wave_Loading", percent);
    }

    // ───────────── playback ─────────────

    public void StopPlayback()
    {
        if (_player.Position is { } t) Playhead = Math.Min(t, Duration);
        _player.Stop();
        IsPlaying = false;
    }

    private void PlayPause()
    {
        if (IsPlaying)
        {
            StopPlayback();
            return;
        }
        var from = Playhead >= Duration - 0.05 ? 0 : Playhead;
        if (!SkipCuts)
        {
            Play([new TimeRange(from, Duration)]);
            return;
        }
        var plan = PlanFrom(from);
        // At or past the last kept range: start the result over.
        Play(plan.Count > 0 ? plan : Ranges.ToList());
    }

    private void PlaySelection()
    {
        if (TryGetSelection(out var sel))
            Play([sel]);
        else if (Ranges.FirstOrDefault(r => r.Contains(Playhead)) is { Length: > 0 } current)
            Play([current]);
    }

    private void PlayJoin()
    {
        var r = Ranges;
        if (r.Count == 0) return;
        if (r.Count == 1)
        {
            // One range: listen to where it starts and where it ends.
            var only = r[0];
            Play([new TimeRange(only.Start, Math.Min(only.End, only.Start + JoinPreview)),
                  new TimeRange(Math.Max(only.Start, only.End - JoinPreview), only.End)]);
            return;
        }
        // The next join after the playhead, wrapping around to the first.
        var i = Enumerable.Range(0, r.Count - 1).FirstOrDefault(k => r[k].End > Playhead + 0.1, -1);
        if (i < 0) i = 0;
        Play([new TimeRange(Math.Max(r[i].Start, r[i].End - JoinPreview), r[i].End),
              new TimeRange(r[i + 1].Start, Math.Min(r[i + 1].End, r[i + 1].Start + JoinPreview))]);
    }

    private void Seek(double t)
    {
        t = Math.Clamp(t, 0, Duration);
        var wasPlaying = IsPlaying;
        if (wasPlaying) _player.Stop();
        Playhead = t;
        if (!wasPlaying) return;
        // Keep playing from the new spot, in whichever mode Play uses.
        var plan = SkipCuts ? PlanFrom(t) : [new TimeRange(t, Duration)];
        if (plan.Count > 0) Play(plan);
        else IsPlaying = false;
    }

    /// <summary>The kept ranges from <paramref name="from"/> on, the first one clipped to start there.</summary>
    private List<TimeRange> PlanFrom(double from) =>
        Ranges.Where(r => r.End > from + 0.01).Select(r => r with { Start = Math.Max(r.Start, from) }).ToList();

    private void Play(IReadOnlyList<TimeRange> plan)
    {
        if (Preview is not { } preview || preview.IsComplete && preview.DecodedFrames == 0)
        {
            Dialogs.Info(Loc.T("Msg_NoAudioToPlay"));
            return;
        }
        if (plan.Count == 0) return;
        _player.Play(preview, plan);
        Playhead = plan[0].Start;
        IsPlaying = true;
    }

    // ───────────── editing ─────────────

    private void MarkIn(double t)
    {
        SelectionStart = t;
        if (!(SelectionEnd > t)) SelectionEnd = double.NaN;
    }

    private void MarkOut(double t)
    {
        SelectionEnd = t;
        if (!(SelectionStart < t)) SelectionStart = double.NaN;
    }

    private void ApplySelection(Func<TimeRange, List<TimeRange>> change)
    {
        if (!TryGetSelection(out var sel)) return;
        Edit(change(sel));
        ClearSelection();
    }

    /// <summary>A discrete edit (button, row): always its own undo step.</summary>
    private void Edit(IReadOnlyList<TimeRange> ranges)
    {
        if (Ranges.Count > 0) _undo.Push(Ranges);
        _restoring = true;
        Ranges = ranges;
        _restoring = false;
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _restoring = true;
        Ranges = _undo.Pop();
        _restoring = false;
    }

    internal void PlayRange(int index)
    {
        if (index >= 0 && index < Ranges.Count) Play([Ranges[index]]);
    }

    internal void RemoveRange(int index)
    {
        if (index >= 0 && index < Ranges.Count) Edit(Ranges.Where((_, i) => i != index).ToList());
    }

    internal void EditRange(int index, double start, double end)
    {
        if (index < 0 || index >= Ranges.Count) return;
        var list = Ranges.ToList();
        list[index] = new TimeRange(start, end);
        Edit(TimeRanges.Normalize(list, Duration));
    }

    private void ClearSelection()
    {
        SelectionStart = double.NaN;
        SelectionEnd = double.NaN;
    }

    private bool TryGetSelection(out TimeRange selection)
    {
        selection = default;
        if (double.IsNaN(SelectionStart) || double.IsNaN(SelectionEnd)) return false;
        selection = new TimeRange(Math.Min(SelectionStart, SelectionEnd), Math.Max(SelectionStart, SelectionEnd));
        return selection.Length >= TimeRanges.MinLength;
    }

    private void SelectionChanged()
    {
        Notify(nameof(HasSelection));
        RefreshTexts();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Updates the rows in place when the count is unchanged, so an edited cell keeps focus.</summary>
    private void SyncRows()
    {
        var ranges = Ranges;
        while (Rows.Count > ranges.Count) Rows.RemoveAt(Rows.Count - 1);
        for (var i = 0; i < ranges.Count; i++)
        {
            if (i < Rows.Count) Rows[i].Update(ranges[i]);
            else Rows.Add(new TrimRangeRow(this, i, ranges[i]));
        }
    }

    private void RefreshTexts()
    {
        var kept = TimeRanges.Total(Ranges);
        Summary = Duration > 0 ? Loc.F("Trim_Summary", Ranges.Count, Time(kept), Time(Math.Max(0, Duration - kept))) : "";
        SelectionText = TryGetSelection(out var s)
            ? Loc.F("Trim_Selection", Time(s.Start), Time(s.End), Time(s.Length))
            : Loc.T("Trim_NoSelection");
        if (Preview is { } p) UpdateWaveStatus(p);
        else if (string.IsNullOrEmpty(Input)) WaveStatus = Loc.T("Wave_PickFile");
    }

    /// <summary>m:ss.f (or h:mm:ss.f) — tenths are enough to tell cuts apart on screen.</summary>
    internal static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(Math.Max(0, seconds), 1));
        var tenths = t.Milliseconds / 100;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{tenths}"
            : $"{t.Minutes}:{t.Seconds:00}.{tenths}";
    }

    // ───────────── job ─────────────

    protected override Job? CreateJob(string input, string outDir)
    {
        if (Info is null || Duration <= 0)
        {
            Dialogs.Info(Loc.T("Msg_ChooseInput"));
            return null;
        }
        var ranges = Ranges.ToList();
        if (ranges.Count == 0)
        {
            Dialogs.Info(Loc.T("Err_TrimNoRanges"));
            return null;
        }

        var precise = Precise;
        var detail = ranges.Count == 1
            ? Loc.F("Detail_Trim", Fmt.Clock(ranges[0].Start), Fmt.Clock(ranges[0].End))
            : Loc.F("Detail_TrimRanges", ranges.Count, Time(TimeRanges.Total(ranges)));
        return new Job(Path.GetFileName(input), detail, JobKind.Process,
            (j, ct) => Converter.TrimRangesAsync(input, ranges, precise, outDir, j, ct));
    }
}

/// <summary>One editable row of the kept-ranges list under the timeline.</summary>
public sealed class TrimRangeRow : Observable
{
    private readonly TrimViewModel _owner;
    private TimeRange _range;

    public TrimRangeRow(TrimViewModel owner, int index, TimeRange range)
    {
        _owner = owner;
        Index = index;
        _range = range;
        PlayCommand = new RelayCommand(() => _owner.PlayRange(Index));
        RemoveCommand = new RelayCommand(() => _owner.RemoveRange(Index));
    }

    public int Index { get; }
    public string Number => (Index + 1).ToString();

    public string StartText
    {
        get => Fmt.Clock(Math.Round(_range.Start, 3));
        set => Commit(value, true);
    }

    public string EndText
    {
        get => Fmt.Clock(Math.Round(_range.End, 3));
        set => Commit(value, false);
    }

    public string LengthText => TrimViewModel.Time(_range.Length);

    public ICommand PlayCommand { get; }
    public ICommand RemoveCommand { get; }

    internal void Update(TimeRange range)
    {
        _range = range;
        Notify(nameof(StartText));
        Notify(nameof(EndText));
        Notify(nameof(LengthText));
    }

    private void Commit(string text, bool isStart)
    {
        if (!TimeParser.TryParse(text, out var t))
        {
            Dialogs.Info(Loc.F("Msg_BadTime", text));
            Update(_range);
            return;
        }
        var (start, end) = isStart ? (t.TotalSeconds, _range.End) : (_range.Start, t.TotalSeconds);
        if (end <= start)
        {
            Dialogs.Info(Loc.T("Err_TrimRange"));
            Update(_range);
            return;
        }
        _owner.EditRange(Index, start, end);
    }
}
