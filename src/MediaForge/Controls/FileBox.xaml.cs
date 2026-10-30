using System.Windows;
using System.Windows.Controls;
using MediaForge.UI;

namespace MediaForge.Controls;

/// <summary>Path text box with a Browse button that also accepts a dropped file or folder.</summary>
public partial class FileBox : UserControl
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(FileBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsFolderProperty = DependencyProperty.Register(
        nameof(IsFolder), typeof(bool), typeof(FileBox), new PropertyMetadata(false));

    /// <summary>"media", "subtitle", "image" or "exe".</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(FileBox), new PropertyMetadata("media"));

    public FileBox() => InitializeComponent();

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public bool IsFolder
    {
        get => (bool)GetValue(IsFolderProperty);
        set => SetValue(IsFolderProperty, value);
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (IsFolder)
        {
            if (Dialogs.OpenFolder(FilePath) is { } folder) FilePath = folder;
            return;
        }
        var filter = Kind switch
        {
            "subtitle" => Dialogs.SubtitleFilter,
            "image" => Dialogs.ImageFilter,
            "exe" => Dialogs.ExeFilter,
            _ => Dialogs.MediaFilter,
        };
        if (Dialogs.OpenFile(filter) is { } file) FilePath = file;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        FilePath = paths[0];
        e.Handled = true;
    }
}
