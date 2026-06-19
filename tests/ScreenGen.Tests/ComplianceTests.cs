using ScreenGen;

namespace ScreenGen.Tests;

public class ComplianceTests : IDisposable
{
    private readonly string _outDir;
    private readonly Config _cfg;
    private readonly IReadOnlyDictionary<string, Target> _catalog;

    public ComplianceTests()
    {
        _outDir = Path.Combine(Path.GetTempPath(), "screengen_test_" + Guid.NewGuid().ToString("N"));
        _cfg = ConfigLoader.Load(TestSupport.ConfigPath());
        _catalog = TargetCatalog.Load(TestSupport.SeedPath(), _cfg.TargetDefs);
    }

    public static IEnumerable<object[]> Targets() => new[]
    {
        new object[] { "apple_phone_6_9" },  // apple phone, cover
        new object[] { "apple_ipad_13" },    // apple tablet, contain
        new object[] { "android_phone" },    // google phone, cover
        new object[] { "android_tablet_10" } // google tablet, contain
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void Rendered_png_is_store_compliant(string targetName)
    {
        var target = _catalog[targetName];
        using var renderer = new Renderer();
        var item = renderer.BuildPlan(_cfg, target, _cfg.Screens[0], 1, _outDir);
        renderer.Render(_cfg, item);

        Assert.True(File.Exists(item.OutputPath), $"output not written: {item.OutputPath}");

        var png = Png.Read(item.OutputPath);

        // Exact dimensions, no off-by-one.
        Assert.Equal(target.Width, png.Width);
        Assert.Equal(target.Height, png.Height);

        // No alpha channel — both stores reject transparency.
        Assert.False(png.HasAlpha, $"{targetName} has alpha (color type {png.ColorType})");
        Assert.True(png.IsTruecolorRgb, $"{targetName} is not 24-bit RGB (color type {png.ColorType})");

        // Google aspect must be within 1:2..2:1.
        if (target.Store == Store.Google)
        {
            int lo = Math.Min(png.Width, png.Height), hi = Math.Max(png.Width, png.Height);
            Assert.True(hi <= lo * 2, $"{targetName} aspect {hi}/{lo} exceeds 2:1");
        }

        // Google file-size limit is 8 MB (apply to all as a sane upper bound).
        long bytes = new FileInfo(item.OutputPath).Length;
        Assert.True(bytes < 8 * 1024 * 1024, $"{targetName} is {bytes} bytes (>= 8 MB)");
    }

    [Fact]
    public void Missing_source_image_throws()
    {
        var target = _catalog["android_phone"];
        using var renderer = new Renderer();
        var spec = new ScreenSpec { Image = "shots/does-not-exist.png", Title = "X" };
        var item = renderer.BuildPlan(_cfg, target, spec, 9, _outDir);
        Assert.False(item.SourceExists);
        Assert.Throws<ConfigException>(() => renderer.Render(_cfg, item));
    }

    public void Dispose()
    {
        if (Directory.Exists(_outDir)) Directory.Delete(_outDir, recursive: true);
    }
}
