using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaForge.Core;
using MediaForge.Localization;

namespace MediaForge.UI;

public static class ThemeHelper
{
    public static void Apply(string theme)
    {
        if (Application.Current is not { } app) return;
        app.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}

public static class LogWindow
{
    public static void Show(string title, string text)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(12),
        };
        var window = new Window
        {
            Title = title,
            Width = 960,
            Height = 560,
            Content = box,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        window.Show();
        box.ScrollToEnd();
    }
}

/// <summary>Small modeless window that shows a tool download.</summary>
public sealed class ProgressWindow : Window
{
    private readonly ProgressBar _bar = new() { Height = 8, Maximum = 100, IsIndeterminate = true };
    private readonly TextBlock _detail = new() { Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) };
    private bool _closing;

    public event Action? Canceled;

    public ProgressWindow(string title)
    {
        Title = "MediaForge";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = Loc.T("Btn_Cancel"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), MinWidth = 90 };
        cancel.Click += (_, _) => Canceled?.Invoke();

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_bar);
        panel.Children.Add(_detail);
        panel.Children.Add(cancel);
        Content = panel;
    }

    public void Report(DownloadProgress p)
    {
        if (p.Total is > 0)
        {
            _bar.IsIndeterminate = false;
            _bar.Value = p.Done * 100.0 / p.Total.Value;
            _detail.Text = $"{Fmt.Bytes(p.Done)} / {Fmt.Bytes(p.Total.Value)}";
        }
        else
        {
            _detail.Text = Fmt.Bytes(p.Done);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closing)
        {
            // Closing the window by hand means "cancel".
            e.Cancel = true;
            Canceled?.Invoke();
        }
        base.OnClosing(e);
    }

    public void CloseQuietly()
    {
        _closing = true;
        Close();
    }
}

/// <summary>Makes sure a required external tool exists, offering to download it when missing.</summary>
public static class ToolGuard
{
    public static async Task<bool> EnsureFfmpegAsync()
    {
        if (Tools.Ffmpeg is not null && Tools.Ffprobe is not null) return true;
        if (!Dialogs.Confirm(Loc.F("Ask_DownloadTool", "FFmpeg"))) return false;
        return await InstallAsync("FFmpeg", Tools.InstallFfmpegAsync);
    }

    public static async Task<bool> EnsureYtDlpAsync()
    {
        if (Tools.YtDlp is null)
        {
            if (!Dialogs.Confirm(Loc.F("Ask_DownloadTool", "yt-dlp"))) return false;
            if (!await InstallAsync("yt-dlp", Tools.InstallYtDlpAsync)) return false;
        }
        // yt-dlp needs ffmpeg to merge video+audio and to convert audio.
        return await EnsureFfmpegAsync();
    }

    public static async Task<bool> InstallAsync(string name, Func<IProgress<DownloadProgress>, CancellationToken, Task> install)
    {
        using var cts = new CancellationTokenSource();
        var window = new ProgressWindow(Loc.F("Msg_DownloadingTool", name));
        window.Canceled += cts.Cancel;
        window.Show();
        try
        {
            await install(new Progress<DownloadProgress>(window.Report), cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.F("Err_DownloadTool", name, ex.Message));
            return false;
        }
        finally
        {
            window.CloseQuietly();
        }
    }
}
