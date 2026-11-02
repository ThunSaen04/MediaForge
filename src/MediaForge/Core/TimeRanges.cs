namespace MediaForge.Core;

/// <summary>A span of a media file in seconds.</summary>
public readonly record struct TimeRange(double Start, double End)
{
    public double Length => End - Start;

    public bool Contains(double t) => t >= Start && t < End;
}

/// <summary>
/// Set operations on the list of ranges a multi-range trim keeps. Lists are always sorted,
/// non-overlapping and inside [0, duration].
/// </summary>
public static class TimeRanges
{
    /// <summary>Ranges shorter than this are dropped; gaps shorter than this are closed.</summary>
    public const double MinLength = 0.05;

    public static List<TimeRange> Normalize(IEnumerable<TimeRange> ranges, double duration)
    {
        var sorted = ranges
            .Select(r => new TimeRange(Math.Clamp(Math.Min(r.Start, r.End), 0, duration),
                                       Math.Clamp(Math.Max(r.Start, r.End), 0, duration)))
            .OrderBy(r => r.Start)
            .ToList();

        var result = new List<TimeRange>();
        foreach (var r in sorted)
        {
            if (result.Count > 0 && r.Start - result[^1].End < MinLength)
                result[^1] = result[^1] with { End = Math.Max(result[^1].End, r.End) };
            else
                result.Add(r);
        }
        result.RemoveAll(r => r.Length < MinLength);
        return result;
    }

    public static List<TimeRange> Add(IEnumerable<TimeRange> keep, TimeRange range, double duration) =>
        Normalize(keep.Append(range), duration);

    public static List<TimeRange> Subtract(IEnumerable<TimeRange> keep, TimeRange cut, double duration)
    {
        var (a, b) = (Math.Min(cut.Start, cut.End), Math.Max(cut.Start, cut.End));
        var result = new List<TimeRange>();
        foreach (var r in keep)
        {
            if (r.End <= a || r.Start >= b)
            {
                result.Add(r);
                continue;
            }
            if (r.Start < a) result.Add(new TimeRange(r.Start, a));
            if (r.End > b) result.Add(new TimeRange(b, r.End));
        }
        return Normalize(result, duration);
    }

    /// <summary>The parts of [0, duration] that <paramref name="keep"/> leaves out.</summary>
    public static List<TimeRange> Invert(IReadOnlyList<TimeRange> keep, double duration)
    {
        var result = new List<TimeRange>();
        var cursor = 0.0;
        foreach (var r in keep)
        {
            if (r.Start - cursor >= MinLength) result.Add(new TimeRange(cursor, r.Start));
            cursor = Math.Max(cursor, r.End);
        }
        if (duration - cursor >= MinLength) result.Add(new TimeRange(cursor, duration));
        return result;
    }

    public static double Total(IEnumerable<TimeRange> ranges) => ranges.Sum(r => r.Length);
}
