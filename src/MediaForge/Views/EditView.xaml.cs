using System.Windows;
using System.Windows.Controls;
using MediaForge.ViewModels;

namespace MediaForge.Views;

public partial class EditView : UserControl
{
    public EditView() => InitializeComponent();

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
