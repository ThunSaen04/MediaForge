using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediaForge.Core;

namespace MediaForge.UI;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class JobStateBrushConverter : IValueConverter
{
    private static readonly Brush Running = Freeze(Color.FromRgb(0x2B, 0x7C, 0xD3));
    private static readonly Brush Completed = Freeze(Color.FromRgb(0x2E, 0x9E, 0x5B));
    private static readonly Brush Failed = Freeze(Color.FromRgb(0xD1, 0x34, 0x38));
    private static readonly Brush Muted = Freeze(Color.FromRgb(0x8A, 0x8A, 0x8A));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        JobState.Running => Running,
        JobState.Completed => Completed,
        JobState.Failed => Failed,
        _ => Muted,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
