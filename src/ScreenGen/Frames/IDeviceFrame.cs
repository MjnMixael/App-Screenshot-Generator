using SkiaSharp;

namespace ScreenGen;

/// <summary>
/// Draws the app screenshot as a "device" onto the canvas, within the rects the
/// layout computed. One interface so a future <c>realistic</c> mode (bundled
/// frame PNGs + screen-inset map) can be added without touching the renderer.
/// </summary>
public interface IDeviceFrame
{
    void Render(SKCanvas canvas, SKImage screenshot, LayoutResult layout, Config cfg, Target target);
}

public static class DeviceFrameFactory
{
    /// <summary>Resolve the effective frame for a target: per-target override, else project default.</summary>
    public static FrameType Resolve(Config cfg, Target target) => target.Frame ?? cfg.Style.Frame.TypeEnum;

    public static IDeviceFrame Create(FrameType type) => type switch
    {
        FrameType.None => new BareFrame(),
        FrameType.Realistic => new RealisticFrame(),
        _ => new StylizedFrame(),
    };
}
