using System.Windows;
using System.Windows.Controls;
using MediaForge.ViewModels;

namespace MediaForge.Views;

public partial class EditView : UserControl
{
    public EditView() => InitializeComponent();

    private void OnZoomIn(object sender, RoutedEventArgs e) => Timeline.ZoomIn();

    private void OnZoomOut(object sender, RoutedEventArgs e) => Timeline.ZoomOut();

    private void OnZoomFit(object sender, RoutedEventArgs e) => Timeline.ZoomFit();

    /// <summary>Leaving the Trim tab (or the Edit page) stops playback.</summary>
    private void OnTimelineUnloaded(object sender, RoutedEventArgs e)
    {
        if (Timeline.DataContext is TrimViewModel vm) vm.StopPlayback();
    }

    private void OnMergeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnMergeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && MergeList.DataContext is MergeViewModel vm)
            vm.AddPaths(paths);
    }
}
