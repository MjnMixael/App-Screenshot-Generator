using SkiaSharp;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ScreenGen;

/// <summary>Thrown for any user-facing config/target/asset problem. Program.cs
/// turns it into a clean message + non-zero exit (no stack trace).</summary>
public sealed class ConfigException(string message) : Exception(message);

public enum BackgroundType { Gradient, Flat }
public enum IslandType { Dynamic, Notch, None }
public enum OutputFormat { Png, Jpeg }

public sealed class Config
{
    public string Project { get; set; } = "App";
    public StyleConfig Style { get; set; } = new();
    public List<ScreenSpec> Screens { get; set; } = new();
    public OutputConfig Output { get; set; } = new();
    public CleanupConfig Cleanup { get; set; } = new();
    public List<string> Targets { get; set; } = new();
    public Dictionary<string, TargetDto>? TargetDefs { get; set; }

    /// <summary>Directory of the config file; all relative paths resolve against it.</summary>
    [YamlIgnore] public string ConfigDir { get; set; } = "";

    public string ResolvePath(string p) =>
        Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(ConfigDir, p));
}

public sealed class StyleConfig
{
    public BackgroundConfig Background { get; set; } = new();
    public string TitleColor { get; set; } = "#FFFFFF";
    public string SubtitleColor { get; set; } = "#C9C9CC";
    public FontConfig Font { get; set; } = new();
    public TitleConfig Title { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();
    public FrameConfig Frame { get; set; } = new();
}

public sealed class BackgroundConfig
{
    public string Type { get; set; } = "gradient";   // gradient | flat
    public string Center { get; set; } = "#323234";
    public string Edge { get; set; } = "#1F1F21";
    public string Color { get; set; } = "#2B2B2D";    // used when type: flat
    [YamlIgnore] public BackgroundType TypeEnum => Enums.Parse(Type, BackgroundType.Gradient);
}

public sealed class FontConfig
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public int TitleWeight { get; set; } = 600;
    public int SubtitleWeight { get; set; } = 400;
}

public sealed class TitleConfig
{
    public bool Uppercase { get; set; } = true;
    public double LetterSpacing { get; set; } = 0.12;   // em-fraction tracking
    public double MaxSizePct { get; set; } = 0.085;     // cap height / canvas H
}

public sealed class LayoutConfig
{
    public double TopMarginPct { get; set; } = 0.06;
    public double TextBlockGapPct { get; set; } = 0.025;
    public double TitleToDeviceGapPct { get; set; } = 0.05;
    public double DeviceWidthPct { get; set; } = 0.82;  // device size as a fraction of canvas width
}

public sealed class FrameConfig
{
    public string Type { get; set; } = "stylized";    // stylized | none | realistic
    public string BezelColor { get; set; } = "#0B0B0D";
    public double BezelWidthPct { get; set; } = 0.013;
    public double CornerRadiusPct { get; set; } = 0.09;
    public string Island { get; set; } = "none";      // none | dynamic | notch (phones only)
    [YamlIgnore] public FrameType TypeEnum => Enums.Parse(Type, FrameType.Stylized);
    [YamlIgnore] public IslandType IslandEnum => Enums.Parse(Island, IslandType.None);
}

/// <summary>
/// Hides device chrome from source screenshots so App Store shots show no hint of
/// Android: paints over the top status bar (clock/battery/icons) and Flutter's
/// top-right debug ribbon. On by default. `fill` is "auto" (samples the pixel
/// just below the status bar to extend a solid background up) or a hex color.
/// </summary>
public sealed class CleanupConfig
{
    public bool StatusBar { get; set; } = true;
    public double StatusBarPct { get; set; } = 0.045;   // fraction of source height
    public bool DebugBanner { get; set; } = true;
    public double DebugBannerPct { get; set; } = 0.16;  // corner leg, fraction of source width
    public string Fill { get; set; } = "auto";          // auto | #RRGGBB

    [YamlIgnore] public bool Enabled => StatusBar || DebugBanner;
    [YamlIgnore] public bool FillIsAuto => string.Equals(Fill?.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}

public sealed class ScreenSpec
{
    public string Image { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
}

public sealed class OutputConfig
{
    public string Dir { get; set; } = "out";
    public string Format { get; set; } = "png";       // png | jpeg
    public bool SaveOriginals { get; set; } = true;    // also copy the raw source shots
    public string OriginalsDir { get; set; } = "originals"; // under Dir, or absolute
    [YamlIgnore] public OutputFormat FormatEnum => Enums.Parse(Format, OutputFormat.Png);
}

internal static class Enums
{
    public static T Parse<T>(string? s, T fallback) where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(s) && Enum.TryParse<T>(s.Trim(), ignoreCase: true, out var v) ? v : fallback;

    public static bool IsValid<T>(string? s) where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(s) && Enum.TryParse<T>(s.Trim(), ignoreCase: true, out _);
}

public static class ConfigLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlOut = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    /// <summary>Write a config back to YAML (used by the desktop app's Save).</summary>
    public static void Save(Config cfg, string path) =>
        File.WriteAllText(path, YamlOut.Serialize(cfg));

    public static Config Load(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new ConfigException($"Config file not found: {path}");

        Config cfg;
        try
        {
            cfg = Yaml.Deserialize<Config>(File.ReadAllText(full)) ?? new Config();
        }
        catch (Exception ex)
        {
            throw new ConfigException($"Failed to parse {path}: {ex.Message}");
        }

        cfg.ConfigDir = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
        Validate(cfg);
        return cfg;
    }

    private static void Validate(Config c)
    {
        var errors = new List<string>();

        if (c.Screens.Count == 0)
            errors.Add("screens: at least one screen is required");
        for (int i = 0; i < c.Screens.Count; i++)
            if (string.IsNullOrWhiteSpace(c.Screens[i].Image))
                errors.Add($"screens[{i}]: image path is required");

        if (c.Targets.Count == 0)
            errors.Add("targets: at least one target is required");

        if (!Enums.IsValid<OutputFormat>(c.Output.Format))
            errors.Add($"output.format must be png|jpeg (got '{c.Output.Format}')");
        if (!Enums.IsValid<BackgroundType>(c.Style.Background.Type))
            errors.Add($"style.background.type must be gradient|flat (got '{c.Style.Background.Type}')");
        if (!Enums.IsValid<FrameType>(c.Style.Frame.Type))
            errors.Add($"style.frame.type must be stylized|none|realistic (got '{c.Style.Frame.Type}')");
        if (!Enums.IsValid<IslandType>(c.Style.Frame.Island))
            errors.Add($"style.frame.island must be dynamic|notch|none (got '{c.Style.Frame.Island}')");

        CheckColor("style.title_color", c.Style.TitleColor, errors);
        CheckColor("style.subtitle_color", c.Style.SubtitleColor, errors);
        CheckColor("style.background.center", c.Style.Background.Center, errors);
        CheckColor("style.background.edge", c.Style.Background.Edge, errors);
        CheckColor("style.background.color", c.Style.Background.Color, errors);
        CheckColor("style.frame.bezel_color", c.Style.Frame.BezelColor, errors);

        var L = c.Style.Layout;
        CheckFraction("style.layout.top_margin_pct", L.TopMarginPct, errors);
        CheckFraction("style.layout.text_block_gap_pct", L.TextBlockGapPct, errors);
        CheckFraction("style.layout.title_to_device_gap_pct", L.TitleToDeviceGapPct, errors);
        CheckFraction("style.layout.device_width_pct", L.DeviceWidthPct, errors);
        CheckFraction("style.title.max_size_pct", c.Style.Title.MaxSizePct, errors);

        CheckFraction("cleanup.status_bar_pct", c.Cleanup.StatusBarPct, errors, allowZero: true);
        CheckFraction("cleanup.debug_banner_pct", c.Cleanup.DebugBannerPct, errors, allowZero: true);
        if (!c.Cleanup.FillIsAuto && !SKColor.TryParse(c.Cleanup.Fill, out _))
            errors.Add($"cleanup.fill must be 'auto' or a #RRGGBB color (got '{c.Cleanup.Fill}')");

        if (errors.Count > 0)
            throw new ConfigException("Invalid config:\n  - " + string.Join("\n  - ", errors));
    }

    private static void CheckColor(string field, string value, List<string> errors)
    {
        if (!SKColor.TryParse(value, out _))
            errors.Add($"{field}: '{value}' is not a valid color (use #RRGGBB)");
    }

    private static void CheckFraction(string field, double value, List<string> errors, bool allowZero = false)
    {
        double min = allowZero ? 0 : double.Epsilon;
        if (value < min || value >= 1.0)
            errors.Add($"{field}: {value} must be {(allowZero ? "in [0,1)" : "in (0,1)")}");
    }
}
