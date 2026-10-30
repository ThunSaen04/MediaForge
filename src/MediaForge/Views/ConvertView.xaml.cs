using System.Windows;
using System.Windows.Controls;
using MediaForge.ViewModels;

namespace MediaForge.Views;

public partial class ConvertView : UserControl
{
    public ConvertView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && DataContext is ConvertViewModel vm)
            vm.AddPaths(paths);
    }
}
