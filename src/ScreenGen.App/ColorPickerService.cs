using SkiaSharp;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ScreenGen.App;

/// <summary>Wraps the native Windows color picker (full custom-color panel: 2D
/// hue/saturation field + RGB/HSL numeric inputs). Returns "#RRGGBB" or null.</summary>
internal static class ColorPickerService
{
    public static string? Pick(string? currentHex)
    {
        using var dlg = new WinForms.ColorDialog { FullOpen = true, AllowFullOpen = true };

        if (!string.IsNullOrWhiteSpace(currentHex) && SKColor.TryParse(currentHex, out var c))
            dlg.Color = Drawing.Color.FromArgb(c.Red, c.Green, c.Blue);

        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return null;
        var p = dlg.Color;
        return $"#{p.R:X2}{p.G:X2}{p.B:X2}";
    }
}
