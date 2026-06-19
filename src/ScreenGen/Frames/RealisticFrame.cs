using SkiaSharp;

namespace ScreenGen;

/// <summary>
/// Stub for a future <c>realistic</c> mode: bundled per-device frame PNGs plus a
/// screen-inset map (where the screenshot is composited inside each frame).
/// Deferred because of frame-image licensing and the per-frame inset data needed.
/// Selecting it today is a clear, early error rather than a silent fallback.
/// </summary>
public sealed class RealisticFrame : IDeviceFrame
{
    public void Render(SKCanvas canvas, SKImage screenshot, LayoutResult layout, Config cfg, Target target) =>
        throw new ConfigException(
            "frame type 'realistic' is not implemented yet — use 'stylized' or 'none'. " +
            "(Realistic frames need bundled frame PNGs + a per-frame screen-inset map.)");
}
