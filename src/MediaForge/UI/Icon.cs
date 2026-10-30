using System.Windows;
using System.Windows.Controls;

namespace MediaForge.UI;

/// <summary>
/// <c>ui:Icon.Glyph</c> puts a Segoe Fluent Icons glyph in front of a button's text.
/// Nav items read the same property from their own template.
/// </summary>
public static class Icon
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(Icon), new PropertyMetadata(null, OnGlyphChanged));

    public static string? GetGlyph(DependencyObject d) => (string?)d.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject d, string? value) => d.SetValue(GlyphProperty, value);

    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Button button && Application.Current?.TryFindResource("IconLabelTemplate") is DataTemplate template)
            button.ContentTemplate = template;
    }
}
