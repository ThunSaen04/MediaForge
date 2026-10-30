using System.Globalization;
using MediaForge.Localization;

namespace MediaForge.Core;

public static class FFmpeg
{
    /// <summary>
    /// Runs ffmpeg and maps its machine-readable progress onto <paramref name="job"/>.
    /// <paramref name="progressFrom"/>/<paramref name="progressTo"/> let multi-pass operations share one bar.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> args,
        double totalSeconds,
        Job? job,
        CancellationToken ct,
        string logLevel = "error",
        double progressFrom = 0,
        double progressTo = 100,
        string? workingDir = null)
    {
        var exe = Tools.Ffmpeg ?? throw new ToolMissingException("ffmpeg");
        List<string> full =
        [
            "-hide_banner", "-nostdin", "-y", "-loglevel", logLevel, "-progress", "pipe:1", "-nostats",
            .. args
        ];

        job?.AppendLog("> ffmpeg " + string.Join(' ', full.Select(Quote)));
        if (job is not null) job.IsIndeterminate = totalSeconds <= 0;

        double speed = 0;
        var result = await ProcessRunner.RunAsync(exe, full,
            onOutput: line =>
            {
                if (job is null) return;
                var eq = line.IndexOf('=');
                if (eq <= 0) return;
                var key = line[..eq];
                var value = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "out_time_us" when totalSeconds > 0 && long.TryParse(value, out var us) && us >= 0:
                        var done = us / 1_000_000.0;
                        var fraction = Math.Clamp(done / totalSeconds, 0, 1);
                        job.Progress = progressFrom + (progressTo - progressFrom) * fraction;
                        var eta = speed > 0 ? (totalSeconds - done) / speed : -1;
                        job.Info = speed > 0
                            ? $"{speed:0.0}x · {Loc.F("Info_Eta", Fmt.Duration(eta))}"
                            : Fmt.Clock(done);
                        break;
                    case "speed":
                        double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out speed);
                        break;
                }
            },
            onError: line => job?.AppendLog(line),
            ct: ct,
            workingDir: workingDir);

        if (result.ExitCode != 0)
            throw new UserException(LastLines(result.ErrorTail, 3) ?? $"ffmpeg exit code {result.ExitCode}");
        return result;
    }

    private static string? LastLines(string text, int count)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines.TakeLast(count));
    }

    private static string Quote(string arg) =>
        arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.Contains('"') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}
