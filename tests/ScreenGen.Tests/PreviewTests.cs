using ScreenGen;
using SkiaSharp;

namespace ScreenGen.Tests;

public class PreviewTests
{
    private readonly Config _cfg = ConfigLoader.Load(TestSupport.ConfigPath());

    [Fact]
    public void RenderToImage_returns_image_at_target_size()
    {
        var catalog = TargetCatalog.Load(TestSupport.SeedPath(), _cfg.TargetDefs);
        var target = catalog["apple_phone_6_9"];
        using var renderer = new Renderer();
        var item = renderer.BuildPlan(_cfg, target, _cfg.Screens[0], 1, "out");

        using SKImage img = renderer.RenderToImage(_cfg, item);

        Assert.Equal(target.Width, img.Width);
        Assert.Equal(target.Height, img.Height);
    }

    [Fact]
    public void RenderToImage_uses_placeholder_when_source_missing()
    {
        var catalog = TargetCatalog.Load(TestSupport.SeedPath(), _cfg.TargetDefs);
        var target = catalog["android_phone"];
        using var renderer = new Renderer();
        var spec = new ScreenSpec { Image = "shots/missing.png", Title = "Preview" };
        var item = renderer.BuildPlan(_cfg, target, spec, 1, "out");

        Assert.False(item.SourceExists);
        using SKImage img = renderer.RenderToImage(_cfg, item); // must not throw
        Assert.Equal(target.Width, img.Width);
        Assert.Equal(target.Height, img.Height);
    }
}
