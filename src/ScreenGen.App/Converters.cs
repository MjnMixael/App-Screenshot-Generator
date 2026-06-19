using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ScreenGen.App;

/// <summary>"#RRGGBB" -> SolidColorBrush for color swatches. Invalid -> transparent.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));
        }
        catch { /* fall through */ }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
