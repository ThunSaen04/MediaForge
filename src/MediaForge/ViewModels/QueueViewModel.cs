using System.Collections.ObjectModel;
using System.Windows.Input;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;

namespace MediaForge.ViewModels;

/// <summary>
/// Runs jobs with separate parallelism limits for ffmpeg work and downloads.
/// All scheduling happens on the UI thread, so no locking is needed.
/// </summary>
public sealed class QueueViewModel : Observable
{
    private readonly AppSettings _settings;

    public QueueViewModel(AppSettings settings)
    {
        _settings = settings;
        CancelCommand = new RelayCommand(p => { if (p is Job job) Cancel(job); });
        RetryCommand = new RelayCommand(p => { if (p is Job job) Retry(job); });
        OpenCommand = new RelayCommand(p => { if (p is Job { OutputPath: { } path }) Dialogs.RevealInExplorer(path); });
        LogCommand = new RelayCommand(p => { if (p is Job job) LogWindow.Show(job.Title, job.Log); });
        CancelAllCommand = new RelayCommand(CancelAll, () => HasActive);
        ClearFinishedCommand = new RelayCommand(ClearFinished, () => Jobs.Any(j => !j.IsActive));

        Jobs.CollectionChanged += (_, _) => Notify(nameof(Summary));
        Loc.Instance.LanguageChanged += () =>
        {
            foreach (var job in Jobs) job.RefreshLanguage();
            Notify(nameof(Summary));
        };
    }

    public ObservableCollection<Job> Jobs { get; } = [];

    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand LogCommand { get; }
    public ICommand CancelAllCommand { get; }
    public ICommand ClearFinishedCommand { get; }

    public bool HasActive => Jobs.Any(j => j.IsActive);

    public string Summary => Loc.F("Queue_Summary",
        Jobs.Count(j => j.State == JobState.Running),
        Jobs.Count(j => j.State == JobState.Queued),
        Jobs.Count(j => j.State == JobState.Completed),
        Jobs.Count(j => j.State == JobState.Failed));

    public void Add(Job job)
    {
        Jobs.Add(job);
        Pump();
    }

    /// <summary>Starts queued jobs up to the configured limits.</summary>
    public void Pump()
    {
        foreach (var kind in new[] { JobKind.Process, JobKind.Download })
        {
            var limit = Math.Max(1, kind == JobKind.Download ? _settings.MaxParallelDownloads : _settings.MaxParallelJobs);
            var running = Jobs.Count(j => j.Kind == kind && j.State == JobState.Running);
            var next = Jobs.Where(j => j.Kind == kind && j.State == JobState.Queued)
                           .Take(Math.Max(0, limit - running))
                           .ToList();
            foreach (var job in next) _ = RunAsync(job);
        }
        Notify(nameof(Summary));
    }

    private async Task RunAsync(Job job)
    {
        job.State = JobState.Running;
        job.Info = "";
        var token = job.Cancellation.Token;
        try
        {
            await Task.Run(() => job.Work(job, token), token);
            job.IsIndeterminate = false;
            job.Progress = 100;
            job.Info = job.OutputPath is { } path ? Path.GetFileName(path) : "";
            job.State = JobState.Completed;
            job.RaiseSucceeded();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            job.IsIndeterminate = false;
            job.Info = "";
            job.State = JobState.Canceled;
        }
        catch (Exception ex)
        {
            job.IsIndeterminate = false;
            job.Error = ex.Message;
            job.Info = ex.Message.ReplaceLineEndings(" ");
            job.AppendLog("ERROR: " + ex);
            job.State = JobState.Failed;
        }
        finally
        {
            Pump();
        }
    }

    private void Cancel(Job job)
    {
        if (job.State == JobState.Queued)
        {
            job.State = JobState.Canceled;
            Notify(nameof(Summary));
        }
        else if (job.State == JobState.Running)
        {
            job.Cancellation.Cancel();
        }
    }

    private void Retry(Job job)
    {
        var index = Jobs.IndexOf(job);
        if (index < 0) return;
        Jobs[index] = job.CloneForRetry();
        Pump();
    }

    public void CancelAll()
    {
        foreach (var job in Jobs.Where(j => j.IsActive).ToList()) Cancel(job);
    }

    private void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => !j.IsActive).ToList()) Jobs.Remove(job);
    }
}
