using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScreenGen.App;

/// <summary>Two-way bridge between a "#RRGGBB" string in the view model and the
/// <see cref="Color"/> an Avalonia ColorPicker binds to. Non-hex values (e.g.
/// the literal "auto" used for cleanup fill) show as transparent in the picker
/// while the text box keeps the real value.</summary>
public sealed class HexToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Color.TryParse(s, out var c)) return c;
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : BindingOperations.DoNothing;
}

/// <summary>true -> green, false -> grey. For the device connection dot.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly IBrush On = new SolidColorBrush(Color.FromRgb(0x3F, 0xCB, 0x6B));
    private static readonly IBrush Off = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x70));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? On : Off;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
