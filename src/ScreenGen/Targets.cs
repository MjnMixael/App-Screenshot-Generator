using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ScreenGen;

public enum Store { Apple, Google }
public enum DeviceClass { Phone, Tablet }
public enum FrameType { Stylized, None, Realistic }
public enum ScreenFit { Cover, Contain, Fill }

/// <summary>A single, fully-resolved render target. Immutable record of data.</summary>
public sealed class Target
{
    public required string Name { get; init; }
    public required Store Store { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required DeviceClass Class { get; init; }
    public required string OutputSubfolder { get; init; }

    /// <summary>Per-target frame override. Null means "use the project default".</summary>
    public FrameType? Frame { get; init; }

    /// <summary>Per-target screen-fit override. Null means "default by class".</summary>
    public ScreenFit? ScreenFit { get; init; }

    /// <summary>Fit used to draw the shot into the screen rect (default cover).
    /// The screen rect already matches the shot aspect, so cover fills it exactly.</summary>
    public ScreenFit EffectiveFit => ScreenFit ?? ScreenGen.ScreenFit.Cover;
}

/// <summary>Raw YAML shape for one target; all fields nullable so overrides can be partial.</summary>
public sealed class TargetDto
{
    public string? Store { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Class { get; set; }
    public string? OutputSubfolder { get; set; }
    public string? Frame { get; set; }
    public string? ScreenFit { get; set; }
}

internal sealed class TargetsFile
{
    public Dictionary<string, TargetDto> Targets { get; set; } = new();
}

/// <summary>Loads/validates the target matrix from the seed file + inline overrides.</summary>
public static class TargetCatalog
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Resolve the effective set of targets. <paramref name="seedPath"/> is the
    /// editable seed matrix; <paramref name="overrides"/> are inline target_defs
    /// (field-by-field merge over seed; a name absent from the seed defines a new
    /// target that must be complete). Throws <see cref="ConfigException"/> on any
    /// validation failure (collected and reported together).
    /// </summary>
    public static IReadOnlyDictionary<string, Target> Load(string seedPath, IReadOnlyDictionary<string, TargetDto>? overrides)
    {
        if (!File.Exists(seedPath))
            throw new ConfigException($"Target seed file not found: {seedPath}");

        Dictionary<string, TargetDto> raw;
        try
        {
            var file = Yaml.Deserialize<TargetsFile>(File.ReadAllText(seedPath)) ?? new TargetsFile();
            raw = file.Targets ?? new();
        }
        catch (Exception ex)
        {
            throw new ConfigException($"Failed to parse {seedPath}: {ex.Message}");
        }

        // Merge overrides field-by-field.
        if (overrides is not null)
        {
            foreach (var (name, ov) in overrides)
            {
                if (raw.TryGetValue(name, out var baseDto))
                    raw[name] = Merge(baseDto, ov);
                else
                    raw[name] = ov;
            }
        }

        var errors = new List<string>();
        var result = new Dictionary<string, Target>(StringComparer.Ordinal);

        foreach (var (name, dto) in raw)
        {
            var target = TryBuild(name, dto, errors);
            if (target is not null)
                result[name] = target;
        }

        if (errors.Count > 0)
            throw new ConfigException("Invalid target definition(s):\n  - " + string.Join("\n  - ", errors));

        return result;
    }

    private static TargetDto Merge(TargetDto b, TargetDto o) => new()
    {
        Store = o.Store ?? b.Store,
        Width = o.Width ?? b.Width,
        Height = o.Height ?? b.Height,
        Class = o.Class ?? b.Class,
        OutputSubfolder = o.OutputSubfolder ?? b.OutputSubfolder,
        Frame = o.Frame ?? b.Frame,
        ScreenFit = o.ScreenFit ?? b.ScreenFit,
    };

    private static Target? TryBuild(string name, TargetDto dto, List<string> errors)
    {
        var ok = true;
        void Err(string m) { errors.Add($"{name}: {m}"); ok = false; }

        if (dto.Width is not int w || w <= 0) { Err("width must be a positive integer"); w = 0; }
        else if (w % 2 != 0) Err($"width {w} must be even");

        if (dto.Height is not int h || h <= 0) { Err("height must be a positive integer"); h = 0; }
        else if (h % 2 != 0) Err($"height {h} must be even");

        if (!TryEnum<Store>(dto.Store, out var store)) Err($"store must be apple|google (got '{dto.Store}')");
        if (!TryEnum<DeviceClass>(dto.Class, out var cls)) Err($"class must be phone|tablet (got '{dto.Class}')");

        if (string.IsNullOrWhiteSpace(dto.OutputSubfolder)) Err("output_subfolder is required");

        FrameType? frame = null;
        if (dto.Frame is not null)
        {
            if (TryEnum<FrameType>(dto.Frame, out var f)) frame = f;
            else Err($"frame must be stylized|none|realistic (got '{dto.Frame}')");
        }

        ScreenFit? fit = null;
        if (dto.ScreenFit is not null)
        {
            if (TryEnum<ScreenFit>(dto.ScreenFit, out var sf)) fit = sf;
            else Err($"screen_fit must be cover|contain (got '{dto.ScreenFit}')");
        }

        // Google requires the longest side to be at most twice the shortest (1:2..2:1).
        if (ok && store == Store.Google && w > 0 && h > 0)
        {
            int lo = Math.Min(w, h), hi = Math.Max(w, h);
            if (hi > lo * 2)
                Err($"google aspect out of range: {w}x{h} has ratio {(double)hi / lo:0.000}:1 (max 2:1)");
        }

        if (!ok) return null;

        return new Target
        {
            Name = name,
            Store = store,
            Width = w,
            Height = h,
            Class = cls,
            OutputSubfolder = dto.OutputSubfolder!.Trim(),
            Frame = frame,
            ScreenFit = fit,
        };
    }

    private static bool TryEnum<T>(string? s, out T value) where T : struct, Enum
    {
        value = default;
        return !string.IsNullOrWhiteSpace(s) && Enum.TryParse(s.Trim(), ignoreCase: true, out value);
    }
}
