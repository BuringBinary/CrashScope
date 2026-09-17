using System;
using System.Globalization;
using System.Windows.Data;

namespace CrashScope.Desktop.Converters;

/// <summary>
/// Splits an "icon + label" string such as "\uE768  Start" into its two parts.
/// The icon part is the leading run of Private Use Area glyphs (U+E000-U+F8FF, Segoe MDL2 Assets);
/// the label part is everything after that, trimmed. Used by IconTextTemplate so that the glyph and the
/// (possibly CJK) label are rendered by different fonts instead of one font that lacks text glyphs.
/// </summary>
public sealed class IconTextPartConverter : IValueConverter
{
    public string Part { get; set; } = "Label";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        var (icon, label) = Split(text);
        return string.Equals(Part, "Icon", StringComparison.OrdinalIgnoreCase) ? icon : label;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static (string Icon, string Label) Split(string text)
    {
        int i = 0;
        while (i < text.Length && IsPua(text[i])) i++;
        if (i == 0) return (string.Empty, text.Trim());
        return (text[..i], text[i..].Trim());
    }

    private static bool IsPua(char c) => c >= '\uE000' && c <= '\uF8FF';
}
