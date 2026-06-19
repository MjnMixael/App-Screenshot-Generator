using SkiaSharp;

namespace ScreenGen;

public static class Util
{
    public static SKColor Color(string hex) =>
        SKColor.TryParse(hex, out var c) ? c : SKColors.Black;

    /// <summary>The solid backing colour (gradient center, or the flat colour).</summary>
    public static SKColor BaseBackground(Config cfg) =>
        cfg.Style.Background.TypeEnum == BackgroundType.Flat
            ? Color(cfg.Style.Background.Color)
            : Color(cfg.Style.Background.Center);

    /// <summary>The fit used to draw the shot into the screen rect. Default is
    /// cover; because the screen rect already matches the shot's aspect ratio this
    /// fills it exactly. A per-target <c>screen_fit</c> override is still honored.</summary>
    public static ScreenFit ResolveFit(Target target) => target.ScreenFit ?? ScreenFit.Cover;

    /// <summary>
    /// Rect to draw a source image into <paramref name="dst"/> under a fit mode.
    /// cover -> fills dst (overflow cropped by a clip), pinned to the TOP so only
    /// the bottom is cropped; contain -> fits inside, centered; fill -> stretches
    /// to exactly dst (non-uniform; may distort aspect).
    /// </summary>
    public static SKRect FitRect(SKRect dst, float srcW, float srcH, ScreenFit fit)
    {
        if (fit == ScreenFit.Fill) return dst;

        float scale = fit == ScreenFit.Cover
            ? Math.Max(dst.Width / srcW, dst.Height / srcH)
            : Math.Min(dst.Width / srcW, dst.Height / srcH);
        float w = srcW * scale, h = srcH * scale;
        float x = dst.MidX - w / 2f;                                  // centered horizontally
        float y = fit == ScreenFit.Cover ? dst.Top : dst.MidY - h / 2f; // cover pins top; contain centers
        return new SKRect(x, y, x + w, y + h);
    }
}
