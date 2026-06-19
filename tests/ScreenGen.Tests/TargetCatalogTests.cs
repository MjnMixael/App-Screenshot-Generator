using ScreenGen;

namespace ScreenGen.Tests;

public class TargetCatalogTests
{
    [Fact]
    public void Seed_loads_and_lead_target_is_correct()
    {
        var catalog = TargetCatalog.Load(TestSupport.SeedPath(), null);

        Assert.True(catalog.ContainsKey("apple_phone_6_9"));
        var t = catalog["apple_phone_6_9"];
        Assert.Equal(1320, t.Width);
        Assert.Equal(2868, t.Height);
        Assert.Equal(Store.Apple, t.Store);
        Assert.Equal(DeviceClass.Phone, t.Class);
        Assert.Equal(ScreenFit.Cover, t.EffectiveFit);                         // default
        Assert.Equal(ScreenFit.Cover, catalog["apple_ipad_13"].EffectiveFit);  // default (screen matches shot aspect)
    }

    [Fact]
    public void All_seed_targets_are_valid()
    {
        var catalog = TargetCatalog.Load(TestSupport.SeedPath(), null);
        Assert.Equal(8, catalog.Count);
        foreach (var t in catalog.Values)
        {
            Assert.True(t.Width % 2 == 0 && t.Height % 2 == 0);
            if (t.Store == Store.Google)
            {
                int lo = Math.Min(t.Width, t.Height), hi = Math.Max(t.Width, t.Height);
                Assert.True(hi <= lo * 2);
            }
        }
    }

    [Fact]
    public void Odd_dimension_is_rejected()
    {
        var overrides = new Dictionary<string, TargetDto>
        {
            ["bad_odd"] = TestSupport.Dto("apple", 1001, 2000, "phone", "apple/bad"),
        };
        var ex = Assert.Throws<ConfigException>(() => TargetCatalog.Load(TestSupport.SeedPath(), overrides));
        Assert.Contains("even", ex.Message);
    }

    [Fact]
    public void Google_aspect_out_of_range_is_rejected()
    {
        var overrides = new Dictionary<string, TargetDto>
        {
            ["android_phone"] = new TargetDto { Height = 5000 }, // 1080x5000 -> 4.6:1
        };
        var ex = Assert.Throws<ConfigException>(() => TargetCatalog.Load(TestSupport.SeedPath(), overrides));
        Assert.Contains("aspect", ex.Message);
    }

    [Fact]
    public void Invalid_class_is_rejected()
    {
        var overrides = new Dictionary<string, TargetDto>
        {
            ["weird"] = TestSupport.Dto("apple", 1000, 2000, "phablet", "apple/weird"),
        };
        var ex = Assert.Throws<ConfigException>(() => TargetCatalog.Load(TestSupport.SeedPath(), overrides));
        Assert.Contains("class", ex.Message);
    }

    [Fact]
    public void Override_merges_field_by_field()
    {
        var overrides = new Dictionary<string, TargetDto>
        {
            ["apple_phone_6_9"] = new TargetDto { ScreenFit = "contain" }, // only override fit
        };
        var catalog = TargetCatalog.Load(TestSupport.SeedPath(), overrides);
        var t = catalog["apple_phone_6_9"];
        Assert.Equal(1320, t.Width);                  // preserved from seed
        Assert.Equal(ScreenFit.Contain, t.EffectiveFit); // overridden
    }
}
