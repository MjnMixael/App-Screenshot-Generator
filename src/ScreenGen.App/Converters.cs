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

/// <summary>true -> green, false -> grey. For the device connection dot.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush On = new(Color.FromRgb(0x3F, 0xCB, 0x6B));
    private static readonly SolidColorBrush Off = new(Color.FromRgb(0x6A, 0x6A, 0x70));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? On : Off;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
