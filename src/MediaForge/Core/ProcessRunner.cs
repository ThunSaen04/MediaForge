using System.Diagnostics;
using System.Text;

namespace MediaForge.Core;

public sealed record ProcessResult(int ExitCode, string Output, string ErrorTail);

public static class ProcessRunner
{
    /// <summary>
    /// Runs a console tool without a window, streaming stdout/stderr line by line.
    /// Cancelling the token kills the whole process tree and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default,
        string? workingDir = null,
        IReadOnlyDictionary<string, string>? env = null,
        bool captureOutput = false)
    {
        ct.ThrowIfCancellationRequested();

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (workingDir is not null) psi.WorkingDirectory = workingDir;
        if (env is not null)
            foreach (var (key, value) in env) psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        var output = captureOutput ? new StringBuilder() : null;
        var tail = new LineTail(60);

        process.Start();
        process.StandardInput.Close();

        var outTask = PumpAsync(process.StandardOutput, line =>
        {
            output?.AppendLine(line);
            onOutput?.Invoke(line);
        });
        var errTask = PumpAsync(process.StandardError, line =>
        {
            tail.Add(line);
            onError?.Invoke(line);
        });

        using (ct.Register(() => Kill(process)))
        {
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outTask, errTask);
        }

        ct.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, output?.ToString() ?? "", tail.ToString());
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            try { onLine(line); }
            catch { /* a faulty listener must not stall the pipe */ }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited.
        }
    }

    private sealed class LineTail(int capacity)
    {
        private readonly Queue<string> _lines = new();
        private readonly Lock _gate = new();

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_gate)
            {
                _lines.Enqueue(line);
                while (_lines.Count > capacity) _lines.Dequeue();
            }
        }

        public override string ToString()
        {
            lock (_gate) return string.Join(Environment.NewLine, _lines);
        }
    }
}
