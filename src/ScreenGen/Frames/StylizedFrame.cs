using SkiaSharp;

namespace ScreenGen;

/// <summary>
/// Default look: a dark rounded-rectangle bezel drawn at runtime, the screenshot
/// masked to rounded screen corners, and (on phones) a drawn Dynamic-Island pill
/// or notch. No photoreal frame, so no licensing and no Apple device-mismatch risk.
/// </summary>
public sealed class StylizedFrame : IDeviceFrame
{
    public void Render(SKCanvas canvas, SKImage screenshot, LayoutResult layout, Config cfg, Target target)
    {
        var bezelColor = Util.Color(cfg.Style.Frame.BezelColor);
        float outerR = layout.CornerRadius;
        float innerR = Math.Max(2f, outerR - layout.Bezel);

        // Bezel (outer rounded rect).
        using (var bezelPaint = new SKPaint { Color = bezelColor, IsAntialias = true })
        using (var outer = new SKRoundRect(layout.DeviceRect, outerR, outerR))
            canvas.DrawRoundRect(outer, bezelPaint);

        // Screen: clip to the inner rounded rect, fill backing, draw the fit screenshot.
        canvas.Save();
        using (var inner = new SKRoundRect(layout.ScreenRect, innerR, innerR))
        {
            canvas.ClipRoundRect(inner, antialias: true);

            using (var backing = new SKPaint { Color = Util.BaseBackground(cfg) })
                canvas.DrawRect(layout.ScreenRect, backing);

            var dst = Util.FitRect(layout.ScreenRect, screenshot.Width, screenshot.Height, Util.ResolveFit(target));
            using var img = new SKPaint { IsAntialias = true };
            canvas.DrawImage(screenshot, dst, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), img);
        }
        canvas.Restore();

        // Camera cutout (phones only).
        if (target.Class == DeviceClass.Phone)
            DrawIsland(canvas, layout, cfg.Style.Frame.IslandEnum, bezelColor);
    }

    private static void DrawIsland(SKCanvas canvas, LayoutResult layout, IslandType island, SKColor color)
    {
        if (island == IslandType.None) return;

        var screen = layout.ScreenRect;
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        if (island == IslandType.Dynamic)
        {
            float w = screen.Width * 0.30f;
            float h = screen.Width * 0.085f;
            float top = screen.Top + screen.Width * 0.030f;
            var pill = new SKRect(screen.MidX - w / 2f, top, screen.MidX + w / 2f, top + h);
            using var rr = new SKRoundRect(pill, h / 2f, h / 2f);
            canvas.DrawRoundRect(rr, paint);
        }
        else // Notch: wide shallow pill hanging from the top edge of the screen.
        {
            float w = screen.Width * 0.50f;
            float h = screen.Width * 0.062f;
            var notch = new SKRect(screen.MidX - w / 2f, screen.Top, screen.MidX + w / 2f, screen.Top + h);
            using var rr = new SKRoundRect(notch, h / 2f, h / 2f);
            canvas.DrawRoundRect(rr, paint);
        }
    }
}
