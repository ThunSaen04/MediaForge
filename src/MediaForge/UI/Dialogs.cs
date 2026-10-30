using System.Diagnostics;
using System.Windows;
using MediaForge.Core;
using MediaForge.Localization;
using Microsoft.Win32;

namespace MediaForge.UI;

public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

    private static string Patterns(IEnumerable<string> exts) => string.Join(";", exts.Select(e => "*." + e));

    public static string MediaFilter =>
        $"{Loc.T("Filter_Media")}|{Patterns(MediaTypes.VideoExtensions.Concat(MediaTypes.AudioExtensions))}" +
        $"|{Loc.T("Filter_Video")}|{Patterns(MediaTypes.VideoExtensions)}" +
        $"|{Loc.T("Filter_Audio")}|{Patterns(MediaTypes.AudioExtensions)}" +
        $"|{Loc.T("Filter_All")}|*.*";

    public static string SubtitleFilter => $"{Loc.T("Filter_Subtitle")}|*.srt;*.ass;*.ssa;*.vtt|{Loc.T("Filter_All")}|*.*";

    public static string ImageFilter => $"{Loc.T("Filter_Image")}|*.jpg;*.jpeg;*.png";

    public static string ExeFilter => $"{Loc.T("Filter_Exe")}|*.exe";

    public static string[] OpenFiles(string filter, bool multiple)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiple };
        var ok = Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileNames : [];
    }

    public static string? OpenFile(string filter) => OpenFiles(filter, false).FirstOrDefault();

    public static string? OpenFolder(string? initial = null)
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        var ok = Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FolderName : null;
    }

    public static string? SaveFile(string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName };
        var ok = Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileName : null;
    }

    public static void Info(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public static bool Confirm(string message) =>
        Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage icon) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, "MediaForge", buttons, icon)
            : MessageBox.Show(message, "MediaForge", buttons, icon);

    /// <summary>Opens a web link in the default browser.</summary>
    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void RevealInExplorer(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
