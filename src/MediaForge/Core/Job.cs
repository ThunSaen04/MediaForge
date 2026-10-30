using MediaForge.Localization;

namespace MediaForge.Core;

public enum JobKind { Process, Download }

public enum JobState { Queued, Running, Completed, Failed, Canceled }

/// <summary>One unit of work in the queue (a conversion, an edit, a download...).</summary>
public sealed class Job(string title, string detail, JobKind kind, Func<Job, CancellationToken, Task> work) : Observable
{
    private readonly Queue<string> _log = new();
    private readonly Lock _logGate = new();

    public string Title { get; set => Set(ref field, value); } = title;
    public string Detail { get; set => Set(ref field, value); } = detail;
    public JobKind Kind { get; } = kind;
    public Func<Job, CancellationToken, Task> Work { get; } = work;
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>Raised on the UI thread after the job finishes successfully.</summary>
    public event Action<Job>? Succeeded;

    public JobState State
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;
            Notify(nameof(StateText));
            Notify(nameof(IsActive));
            Notify(nameof(CanRetry));
        }
    }

    public string StateText => Loc.T($"State_{State}");
    public bool IsActive => State is JobState.Queued or JobState.Running;
    public bool CanRetry => State is JobState.Failed or JobState.Canceled;

    public double Progress { get; set => Set(ref field, value); }
    public bool IsIndeterminate { get; set => Set(ref field, value); }
    public string Info { get; set => Set(ref field, value); } = "";
    public string? Error { get; set => Set(ref field, value); }

    public string? OutputPath
    {
        get;
        set
        {
            if (Set(ref field, value)) Notify(nameof(HasOutput));
        }
    }

    public bool HasOutput => OutputPath is not null;

    public string Log
    {
        get
        {
            lock (_logGate) return string.Join(Environment.NewLine, _log);
        }
    }

    public void AppendLog(string line)
    {
        lock (_logGate)
        {
            _log.Enqueue(line);
            while (_log.Count > 400) _log.Dequeue();
        }
    }

    public void RefreshLanguage() => Notify(nameof(StateText));

    internal void RaiseSucceeded() => Succeeded?.Invoke(this);

    public Job CloneForRetry()
    {
        var clone = new Job(Title, Detail, Kind, Work);
        clone.Succeeded = Succeeded;
        return clone;
    }
}
