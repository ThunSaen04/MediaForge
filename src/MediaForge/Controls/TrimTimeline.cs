using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MediaForge.Core;

namespace MediaForge.Controls;

/// <summary>
/// Waveform timeline for the multi-range trim: shows kept ranges and the parts that will be cut,
/// a time ruler, the playhead and a selection. Click seeks, dragging selects, range edges can be
/// dragged, Ctrl+wheel zooms and the strip at the bottom pans.
/// </summary>
public sealed class TrimTimeline : FrameworkElement
{
    private const double RulerHeight = 22;
    private const double OverviewHeight = 12;
    private const double OverviewGap = 6;
    private const double EdgeGrab = 6;
    private const double DragThreshold = 3;
    private const double MinSpan = 0.25;

    private static readonly double[] TickSteps =
        [0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200];

    private double _viewStart;
    private double _viewSpan;
    private float[] _peaks = [];
    private int _peakCount;
    private float _peakMax;
    private bool _refreshQueued;

    private DragMode _mode;
    private Point _down;
    private double _downTime;
    private int _edgeIndex;
    private bool _edgeIsStart;
    private double _panOffset;
    private double? _hoverX;

    private enum DragMode { None, Pending, Select, Edge, Scrub, Pan }

    static TrimTimeline()
    {
        FocusableProperty.OverrideMetadata(typeof(TrimTimeline), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(TrimTimeline), new FrameworkPropertyMetadata(null));
    }

    public TrimTimeline()
    {
        ClipToBounds = true;
        Unloaded += (_, _) => Attach(null, Preview);
        Loaded += (_, _) => Attach(Preview, null);
    }

    // ───────────── properties ─────────────

    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(AudioPreview), typeof(TrimTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((TrimTimeline)d).Attach((AudioPreview?)e.NewValue, (AudioPreview?)e.OldValue)));

    public AudioPreview? Preview
    {
        get => (AudioPreview?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(double), typeof(TrimTimeline),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((TrimTimeline)d).ZoomFit()));

    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty RangesProperty = DependencyProperty.Register(
        nameof(Ranges), typeof(IReadOnlyList<TimeRange>), typeof(TrimTimeline),
        new FrameworkPropertyMetadata(Array.Empty<TimeRange>(),
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>The ranges that will be kept, sorted and non-overlapping.</summary>
    public IReadOnlyList<TimeRange> Ranges
    {
        get => (IReadOnlyList<TimeRange>)GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    public static readonly DependencyProperty PlayheadProperty = DependencyProperty.Register(
        nameof(Playhead), typeof(double), typeof(TrimTimeline),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((TrimTimeline)d).FollowPlayhead()));

    public double Playhead
    {
        get => (double)GetValue(PlayheadProperty);
        set => SetValue(PlayheadProperty, value);
    }

    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart), typeof(double), typeof(TrimTimeline),
        new FrameworkPropertyMetadata(double.NaN,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public static readonly DependencyProperty SelectionEndProperty = DependencyProperty.Register(
        nameof(SelectionEnd), typeof(double), typeof(TrimTimeline),
        new FrameworkPropertyMetadata(double.NaN,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(TrimTimeline));

    /// <summary>Executed with the target time in seconds (double) when the user clicks or scrubs.</summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(TrimTimeline),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Shown in the middle of the waveform area while there is no waveform (loading, no audio).</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public static readonly DependencyProperty AccentBrushProperty = BrushProperty(nameof(AccentBrush), Brushes.DodgerBlue);
    public static readonly DependencyProperty CutBrushProperty = BrushProperty(nameof(CutBrush), Brushes.IndianRed);
    public static readonly DependencyProperty TextBrushProperty = BrushProperty(nameof(TextBrush), Brushes.Gainsboro);
    public static readonly DependencyProperty TrackBrushProperty = BrushProperty(nameof(TrackBrush), Brushes.DimGray);

    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush CutBrush { get => (Brush)GetValue(CutBrushProperty); set => SetValue(CutBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    private static DependencyProperty BrushProperty(string name, Brush fallback) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(TrimTimeline),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    // ───────────── view ─────────────

    public void ZoomFit()
    {
        _viewStart = 0;
        _viewSpan = Math.Max(Duration, MinSpan);
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomAround(Playhead, 1 / 2.0);

    public void ZoomOut() => ZoomAround(Playhead, 2.0);

    private void ZoomAround(double t, double factor)
    {
        if (Duration <= 0) return;
        var span = Math.Clamp(_viewSpan * factor, Math.Min(MinSpan, Duration), Duration);
        // Keep t at the same spot on screen; if it is off screen, center on it.
        var ratio = t >= _viewStart && t <= _viewStart + _viewSpan ? (t - _viewStart) / _viewSpan : 0.5;
        SetView(t - ratio * span, span);
    }

    private void SetView(double start, double span)
    {
        _viewSpan = span;
        _viewStart = Math.Clamp(start, 0, Math.Max(0, Duration - span));
        InvalidateVisual();
    }

    private void FollowPlayhead()
    {
        if (_mode != DragMode.None || _viewSpan >= Duration) return;
        var t = Playhead;
        if (t < _viewStart || t > _viewStart + _viewSpan) SetView(t - _viewSpan * 0.1, _viewSpan);
    }

    private double WaveTop => RulerHeight;
    private double WaveBottom => Math.Max(WaveTop + 10, ActualHeight - OverviewHeight - OverviewGap);
    private double OverviewTop => ActualHeight - OverviewHeight;

    private double X(double t) => _viewSpan <= 0 ? 0 : (t - _viewStart) / _viewSpan * ActualWidth;
    private double T(double x) => Math.Clamp(_viewStart + x / Math.Max(1, ActualWidth) * _viewSpan, 0, Duration);

    // ───────────── peaks ─────────────

    private void Attach(AudioPreview? now, AudioPreview? old)
    {
        if (old is not null) old.Progress -= OnPreviewProgress;
        _peaks = [];
        _peakCount = 0;
        _peakMax = 0;
        if (now is not null && IsLoaded)
        {
            now.Progress -= OnPreviewProgress;
            now.Progress += OnPreviewProgress;
            RefreshPeaks();
        }
        InvalidateVisual();
    }

    private void OnPreviewProgress()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            RefreshPeaks();
            InvalidateVisual();
        });
    }

    private void RefreshPeaks()
    {
        if (Preview is not { } p) return;
        var count = p.PeakCount;
        if (count == _peakCount) return;
        if (_peaks.Length < count) Array.Resize(ref _peaks, Math.Max(count, _peaks.Length * 2));
        var added = p.CopyPeaks(_peakCount, _peaks.AsSpan(_peakCount, count - _peakCount));
        for (var i = _peakCount; i < _peakCount + added; i++)
            if (_peaks[i] > _peakMax) _peakMax = _peaks[i];
        _peakCount += added;
    }

    // ───────────── rendering ─────────────

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var waveTop = WaveTop;
        var waveBottom = WaveBottom;
        var waveRect = new Rect(0, waveTop, w, waveBottom - waveTop);

        dc.DrawRoundedRectangle(Faded(TrackBrush, 0.35), null, waveRect, 4, 4);
        if (Duration <= 0 || _viewSpan <= 0)
        {
            DrawCentered(dc, StatusText, waveRect, dpi);
            return;
        }

        dc.PushClip(new RectangleGeometry(waveRect, 4, 4));
        var ranges = Ranges;
        var cuts = TimeRanges.Invert(ranges, Duration);
        foreach (var c in cuts) dc.DrawRectangle(Faded(CutBrush, 0.16), null, Span(c, waveTop, waveBottom));
        foreach (var r in ranges) dc.DrawRectangle(Faded(AccentBrush, 0.16), null, Span(r, waveTop, waveBottom));

        DrawWaveform(dc, ranges, waveTop, waveBottom);
        if (_peakCount == 0) DrawCentered(dc, StatusText, waveRect, dpi);

        // Cut parts get a hatch so they read as "removed" even without color.
        var hatch = new Pen(Faded(CutBrush, 0.35), 1);
        foreach (var c in cuts)
        {
            var rect = Span(c, waveTop, waveBottom);
            if (rect.Width <= 0) continue;
            dc.PushClip(new RectangleGeometry(rect));
            for (var x = rect.Left - (rect.Left % 10) - rect.Height; x < rect.Right; x += 10)
                dc.DrawLine(hatch, new Point(x, waveBottom), new Point(x + rect.Height, waveTop));
            dc.Pop();
        }

        // Range edges, grips and numbers.
        var edgePen = new Pen(AccentBrush, 2);
        for (var i = 0; i < ranges.Count; i++)
        {
            var r = ranges[i];
            var (x1, x2) = (X(r.Start), X(r.End));
            dc.DrawLine(edgePen, new Point(x1, waveTop), new Point(x1, waveBottom));
            dc.DrawLine(edgePen, new Point(x2, waveTop), new Point(x2, waveBottom));
            dc.DrawRoundedRectangle(AccentBrush, null, new Rect(x1, waveTop, 7, 16), 2, 2);
            dc.DrawRoundedRectangle(AccentBrush, null, new Rect(x2 - 7, waveTop, 7, 16), 2, 2);
            if (x2 - x1 > 34)
            {
                var label = Text((i + 1).ToString(CultureInfo.InvariantCulture), 11, OnAccent(), dpi, bold: true);
                var badge = new Rect(x1 + 10, waveTop + 3, Math.Max(16, label.Width + 8), 16);
                dc.DrawRoundedRectangle(AccentBrush, new Pen(Faded(TrackBrush, 1), 1), badge, 8, 8);
                dc.DrawText(label, new Point(badge.Left + (badge.Width - label.Width) / 2, badge.Top + (badge.Height - label.Height) / 2));
            }
        }

        // Selection.
        if (HasSelection(out var sel))
        {
            var rect = Span(sel, waveTop, waveBottom);
            dc.DrawRectangle(Faded(TextBrush, 0.14), new Pen(Faded(TextBrush, 0.8), 1) { DashStyle = DashStyles.Dash }, rect);
        }
        dc.Pop();

        DrawRuler(dc, w, dpi);
        DrawOverview(dc, w, ranges);

        // Hover line and time.
        if (_hoverX is { } hx && _mode is DragMode.None or DragMode.Pending)
        {
            dc.DrawLine(new Pen(Faded(TextBrush, 0.35), 1), new Point(hx, waveTop), new Point(hx, waveBottom));
        }

        // Playhead.
        var px = X(Playhead);
        if (px >= -1 && px <= w + 1)
        {
            var pen = new Pen(TextBrush, 1.5);
            dc.DrawLine(pen, new Point(px, RulerHeight - 6), new Point(px, waveBottom));
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(new Point(px - 5, RulerHeight - 9), true, true);
                g.LineTo(new Point(px + 5, RulerHeight - 9), false, false);
                g.LineTo(new Point(px, RulerHeight - 2), false, false);
            }
            head.Freeze();
            dc.DrawGeometry(TextBrush, null, head);
        }
    }

    private void DrawWaveform(DrawingContext dc, IReadOnlyList<TimeRange> ranges, double top, double bottom)
    {
        if (_peakCount == 0) return;
        var w = (int)Math.Ceiling(ActualWidth);
        var mid = (top + bottom) / 2;
        var half = (bottom - top) / 2 - 2;
        // Quiet recordings are scaled up so their shape is still visible.
        var gain = _peakMax > 0 ? Math.Min(1 / _peakMax, 4) : 1;
        var perSecond = AudioPreview.SampleRate / (double)AudioPreview.FramesPerPeak;

        var kept = new StreamGeometry();
        var cut = new StreamGeometry();
        using (var keptCtx = kept.Open())
        using (var cutCtx = cut.Open())
        {
            var runStart = 0;
            var runKept = false;
            var heights = new double[w + 1];
            for (var x = 0; x <= w; x++)
            {
                var t0 = T(x);
                var t1 = T(x + 1);
                var i0 = (int)(t0 * perSecond);
                var i1 = Math.Max(i0 + 1, (int)Math.Ceiling(t1 * perSecond));
                float peak = 0;
                for (var i = Math.Max(0, i0); i < Math.Min(i1, _peakCount); i++)
                    if (_peaks[i] > peak) peak = _peaks[i];
                heights[x] = Math.Max(0.5, Math.Min(half, peak * gain * half));

                var isKept = InRanges(ranges, (t0 + t1) / 2);
                if (x == 0)
                {
                    runKept = isKept;
                    continue;
                }
                if (isKept != runKept || x == w)
                {
                    AddRun(runKept ? keptCtx : cutCtx, heights, runStart, x, mid);
                    runStart = x;
                    runKept = isKept;
                }
            }
        }
        kept.Freeze();
        cut.Freeze();
        dc.DrawGeometry(AccentBrush, null, kept);
        dc.DrawGeometry(Faded(TextBrush, 0.35), null, cut);
    }

    /// <summary>Mirrored waveform outline for columns [from, to].</summary>
    private static void AddRun(StreamGeometryContext g, double[] heights, int from, int to, double mid)
    {
        g.BeginFigure(new Point(from, mid - heights[from]), true, true);
        for (var x = from + 1; x <= to; x++) g.LineTo(new Point(x, mid - heights[x]), false, false);
        for (var x = to; x >= from; x--) g.LineTo(new Point(x, mid + heights[x]), false, false);
    }

    private void DrawRuler(DrawingContext dc, double w, double dpi)
    {
        var pxPerSecond = w / _viewSpan;
        var step = TickSteps.FirstOrDefault(s => s * pxPerSecond >= 90, TickSteps[^1]);
        var minor = step / 5;
        var pen = new Pen(Faded(TextBrush, 0.4), 1);
        var first = Math.Floor(_viewStart / minor) * minor;
        for (var t = first; t <= _viewStart + _viewSpan + minor; t += minor)
        {
            var x = Math.Round(X(t)) + 0.5;
            var major = Math.Abs(t / step - Math.Round(t / step)) < 1e-6;
            dc.DrawLine(pen, new Point(x, RulerHeight - (major ? 9 : 4)), new Point(x, RulerHeight - 1));
            if (major)
                dc.DrawText(Text(RulerLabel(t, step), 11, Faded(TextBrush, 0.75), dpi), new Point(x + 3, 1));
        }

        if (_hoverX is { } hx && Duration > 0)
        {
            var label = Text(Fmt.Clock(Math.Round(T(hx), 2)), 11, TextBrush, dpi);
            var x = Math.Clamp(hx + 6, 0, w - label.Width - 6);
            dc.DrawRoundedRectangle(Faded(TrackBrush, 0.95), null, new Rect(x - 4, 0, label.Width + 8, 17), 3, 3);
            dc.DrawText(label, new Point(x, 1));
        }
    }

    private void DrawOverview(DrawingContext dc, double w, IReadOnlyList<TimeRange> ranges)
    {
        var top = OverviewTop;
        var full = new Rect(0, top, w, OverviewHeight);
        dc.DrawRoundedRectangle(Faded(CutBrush, 0.35), null, full, 3, 3);
        double OX(double t) => t / Duration * w;
        foreach (var r in ranges)
            dc.DrawRectangle(Faded(AccentBrush, 0.8), null, new Rect(OX(r.Start), top, Math.Max(1, OX(r.End) - OX(r.Start)), OverviewHeight));
        dc.DrawLine(new Pen(TextBrush, 1.5), new Point(OX(Playhead), top), new Point(OX(Playhead), top + OverviewHeight));
        if (_viewSpan < Duration)
        {
            var view = new Rect(OX(_viewStart), top - 1, Math.Max(6, OX(_viewSpan)), OverviewHeight + 2);
            dc.DrawRoundedRectangle(Faded(TextBrush, 0.15), new Pen(TextBrush, 1.5), view, 3, 3);
        }
    }

    private void DrawCentered(DrawingContext dc, string text, Rect area, double dpi)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = Text(text, 12, Faded(TextBrush, 0.7), dpi);
        dc.DrawText(ft, new Point(area.Left + (area.Width - ft.Width) / 2, area.Top + (area.Height - ft.Height) / 2));
    }

    private Rect Span(TimeRange r, double top, double bottom)
    {
        var x1 = Math.Max(-2, X(r.Start));
        var x2 = Math.Min(ActualWidth + 2, X(r.End));
        return new Rect(x1, top, Math.Max(0, x2 - x1), bottom - top);
    }

    private FormattedText Text(string s, double size, Brush brush, double dpi, bool bold = false) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStretches.Normal), size, brush, dpi);

    private static string RulerLabel(double t, double step)
    {
        var ts = TimeSpan.FromSeconds(Math.Round(t, 2));
        var clock = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
        return step < 1 ? $"{clock}.{ts.Milliseconds / 10:00}" : clock;
    }

    private static Brush Faded(Brush brush, double opacity)
    {
        if (brush is SolidColorBrush s)
        {
            var c = s.Color;
            var b = new SolidColorBrush(Color.FromArgb((byte)(c.A * opacity), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        var clone = brush.Clone();
        clone.Opacity = opacity;
        clone.Freeze();
        return clone;
    }

    /// <summary>Black or white, whichever reads better on the accent color.</summary>
    private Brush OnAccent()
    {
        var c = AccentBrush is SolidColorBrush s ? s.Color : Colors.DodgerBlue;
        return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Brushes.Black : Brushes.White;
    }

    private static bool InRanges(IReadOnlyList<TimeRange> ranges, double t)
    {
        foreach (var r in ranges)
            if (r.Contains(t)) return true;
        return false;
    }

    private bool HasSelection(out TimeRange selection)
    {
        var (a, b) = (SelectionStart, SelectionEnd);
        selection = default;
        if (double.IsNaN(a) || double.IsNaN(b) || Math.Abs(b - a) < 0.001) return false;
        selection = new TimeRange(Math.Min(a, b), Math.Max(a, b));
        return true;
    }

    // ───────────── input ─────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Duration <= 0) return;
        Focus();
        var p = e.GetPosition(this);
        _down = p;
        _downTime = T(p.X);

        if (p.Y >= OverviewTop - OverviewGap / 2)
        {
            var overviewT = p.X / ActualWidth * Duration;
            // Grabbing outside the viewport box jumps the view there first.
            if (overviewT < _viewStart || overviewT > _viewStart + _viewSpan) SetView(overviewT - _viewSpan / 2, _viewSpan);
            _panOffset = overviewT - _viewStart;
            _mode = DragMode.Pan;
        }
        else if (p.Y < RulerHeight)
        {
            _mode = DragMode.Scrub;
            Seek(_downTime);
        }
        else if (HitEdge(p.X) is { } edge)
        {
            (_edgeIndex, _edgeIsStart) = edge;
            _mode = DragMode.Edge;
        }
        else if (e.ClickCount == 2)
        {
            // Double-click a kept range to select it.
            var hit = Ranges.FirstOrDefault(r => r.Contains(_downTime));
            if (hit.Length > 0)
            {
                SelectionStart = hit.Start;
                SelectionEnd = hit.End;
            }
            _mode = DragMode.None;
            e.Handled = true;
            return;
        }
        else
        {
            _mode = DragMode.Pending;
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        _hoverX = p.Y < OverviewTop - OverviewGap / 2 ? p.X : null;

        switch (_mode)
        {
            case DragMode.Pending when Math.Abs(p.X - _down.X) > DragThreshold:
                _mode = DragMode.Select;
                SelectionStart = Snap(_downTime);
                SelectionEnd = Snap(T(p.X));
                break;
            case DragMode.Select:
                SelectionEnd = Snap(T(p.X));
                break;
            case DragMode.Scrub:
                Seek(T(p.X));
                break;
            case DragMode.Pan:
                SetView(p.X / ActualWidth * Duration - _panOffset, _viewSpan);
                break;
            case DragMode.Edge:
                MoveEdge(Snap(T(p.X)));
                break;
        }

        Cursor = _mode switch
        {
            DragMode.Edge => Cursors.SizeWE,
            DragMode.Pan => Cursors.SizeWE,
            DragMode.None when p.Y >= OverviewTop - OverviewGap / 2 => Cursors.Hand,
            DragMode.None when p.Y >= RulerHeight && HitEdge(p.X) is not null => Cursors.SizeWE,
            _ => null,
        };
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_mode == DragMode.Pending)
        {
            SelectionStart = double.NaN;
            SelectionEnd = double.NaN;
            Seek(_downTime);
        }
        else if (_mode == DragMode.Edge)
        {
            Ranges = TimeRanges.Normalize(Ranges, Duration);
        }
        _mode = DragMode.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverX = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Duration <= 0) return;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            ZoomAround(T(e.GetPosition(this).X), e.Delta > 0 ? 1 / 1.5 : 1.5);
            e.Handled = true;
        }
        else if (mods.HasFlag(ModifierKeys.Shift) && _viewSpan < Duration)
        {
            SetView(_viewStart - Math.Sign(e.Delta) * _viewSpan * 0.15, _viewSpan);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Duration <= 0) return;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5.0 : 1.0;
        switch (e.Key)
        {
            case Key.Left: Seek(Playhead - step); break;
            case Key.Right: Seek(Playhead + step); break;
            case Key.Home: Seek(0); break;
            case Key.End: Seek(Duration); break;
            case Key.Add or Key.OemPlus: ZoomIn(); break;
            case Key.Subtract or Key.OemMinus: ZoomOut(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Seek(double t)
    {
        t = Math.Clamp(t, 0, Duration);
        if (SeekCommand is { } cmd && cmd.CanExecute(t)) cmd.Execute(t);
    }

    /// <summary>Snaps to the playhead when within a few pixels of it, so edges can be set by ear.</summary>
    private double Snap(double t) => Math.Abs(X(t) - X(Playhead)) <= EdgeGrab ? Playhead : t;

    private (int Index, bool IsStart)? HitEdge(double x)
    {
        var ranges = Ranges;
        (int, bool)? best = null;
        var bestDist = EdgeGrab + 1;
        for (var i = 0; i < ranges.Count; i++)
        {
            var ds = Math.Abs(X(ranges[i].Start) - x);
            var de = Math.Abs(X(ranges[i].End) - x);
            if (ds < bestDist) (best, bestDist) = ((i, true), ds);
            if (de < bestDist) (best, bestDist) = ((i, false), de);
        }
        return bestDist <= EdgeGrab ? best : null;
    }

    private void MoveEdge(double t)
    {
        var ranges = Ranges.ToList();
        if (_edgeIndex >= ranges.Count) return;
        var r = ranges[_edgeIndex];
        if (_edgeIsStart)
        {
            var min = _edgeIndex > 0 ? ranges[_edgeIndex - 1].End : 0;
            ranges[_edgeIndex] = r with { Start = Math.Clamp(t, min, r.End - TimeRanges.MinLength) };
        }
        else
        {
            var max = _edgeIndex + 1 < ranges.Count ? ranges[_edgeIndex + 1].Start : Duration;
            ranges[_edgeIndex] = r with { End = Math.Clamp(t, r.Start + TimeRanges.MinLength, max) };
        }
        Ranges = ranges;
    }
}
