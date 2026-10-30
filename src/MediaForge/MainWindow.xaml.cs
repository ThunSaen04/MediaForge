using System.ComponentModel;
using System.Windows;
using MediaForge.Localization;
using MediaForge.UI;
using MediaForge.ViewModels;

namespace MediaForge;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm) _ = vm.Update.StartupCheckAsync();
        };
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // With WindowChrome a maximized window overhangs the screen by its resize frame; pad it back in.
        if (WindowState == WindowState.Maximized)
        {
            var frame = SystemParameters.WindowResizeBorderThickness;
            Root.Margin = new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4);
        }
        else
        {
            Root.Margin = new Thickness(0);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Installing an update already confirmed and cancelled the running jobs.
        if (DataContext is MainViewModel { Queue.HasActive: true, Update.IsInstalling: false } vm)
        {
            if (!Dialogs.Confirm(Loc.T("Ask_ExitWithJobs")))
            {
                e.Cancel = true;
                return;
            }
            // Cancelling kills the ffmpeg / yt-dlp process trees.
            vm.Queue.CancelAll();
        }
        base.OnClosing(e);
    }
}
