using SkiaSharp;

namespace ScreenGen;

/// <summary>Frameless: the screenshot with rounded corners, offset down. The
/// simplest store-safe look. ScreenRect == DeviceRect (no bezel).</summary>
public sealed class BareFrame : IDeviceFrame
{
    public void Render(SKCanvas canvas, SKImage screenshot, LayoutResult layout, Config cfg, Target target)
    {
        float r = layout.CornerRadius;
        canvas.Save();
        using (var rr = new SKRoundRect(layout.ScreenRect, r, r))
        {
            canvas.ClipRoundRect(rr, antialias: true);

            using (var backing = new SKPaint { Color = Util.BaseBackground(cfg) })
                canvas.DrawRect(layout.ScreenRect, backing);

            var dst = Util.FitRect(layout.ScreenRect, screenshot.Width, screenshot.Height, Util.ResolveFit(target));
            using var img = new SKPaint { IsAntialias = true };
            canvas.DrawImage(screenshot, dst, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), img);
        }
        canvas.Restore();
    }
}
