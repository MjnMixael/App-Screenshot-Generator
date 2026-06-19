using SkiaSharp;

namespace ScreenGen;

/// <summary>One target × screen unit of work, with its resolved layout + paths.</summary>
public sealed class PlanItem
{
    public required Target Target { get; init; }
    public required int Index { get; init; }          // 1-based -> NN.png
    public required string Title { get; init; }
    public required string? Subtitle { get; init; }
    public required string SourcePath { get; init; }
    public required bool SourceExists { get; init; }
    public required string OutputPath { get; init; }
    public required FrameType Frame { get; init; }
    public required LayoutResult Layout { get; init; }
}

/// <summary>
/// Builds layout plans and renders them, either to opaque no-alpha image files
/// (<see cref="Render"/>) or to an in-memory <see cref="SKImage"/> for live
/// preview (<see cref="RenderToImage"/>). Typefaces and decoded source images are
/// cached across calls so repeated previews are cheap. Config is passed per call
/// so a UI can mutate it freely between renders.
/// </summary>
public sealed class Renderer : IDisposable
{
    private const string DefaultFaceKey = "__default__";

    private readonly Action<string> _warn;
    private readonly Dictionary<string, SKTypeface> _faceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SKImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    public Renderer(Action<string>? warn = null) => _warn = warn ?? (_ => { });

    /// <summary>Compute geometry + paths for one unit of work without rendering.</summary>
    public PlanItem BuildPlan(Config cfg, Target target, ScreenSpec screen, int index, string outputDir)
    {
        var titleFace = Face(cfg.Style.Font.Title, cfg.ConfigDir);
        var subtitleFace = Face(cfg.Style.Font.Subtitle, cfg.ConfigDir);
        string source = _cfgResolve(cfg, screen.Image);
        // The device frame keeps the TARGET device's aspect (from its pixel dims),
        // so an iPad target looks like an iPad at any size. The shot fills it via
        // cover (uniform scale, keep aspect, crop overflow).
        float deviceAspect = (float)target.Width / target.Height;
        var layout = Layout.Compute(cfg, target, titleFace, subtitleFace, screen.Title, screen.Subtitle, deviceAspect);
        string ext = cfg.Output.FormatEnum == OutputFormat.Jpeg ? "jpg" : "png";
        string outPath = Path.Combine(outputDir, target.OutputSubfolder, $"{index:D2}.{ext}");

        return new PlanItem
        {
            Target = target,
            Index = index,
            Title = screen.Title,
            Subtitle = screen.Subtitle,
            SourcePath = source,
            SourceExists = !string.IsNullOrWhiteSpace(screen.Image) && File.Exists(source),
            OutputPath = outPath,
            Frame = DeviceFrameFactory.Resolve(cfg, target),
            Layout = layout,
        };
    }

    /// <summary>Render to file with full store-compliance assertions.</summary>
    public void Render(Config cfg, PlanItem item)
    {
        if (!item.SourceExists)
            throw new ConfigException($"source image not found: {item.SourcePath}");

        using var image = Compose(cfg, item, LoadImage(item.SourcePath));
        Save(cfg, image, item);
    }

    /// <summary>Render to an in-memory image for preview. Missing source -> placeholder.
    /// Caller owns the returned <see cref="SKImage"/>.</summary>
    public SKImage RenderToImage(Config cfg, PlanItem item)
    {
        SKImage screenshot = item.SourceExists ? LoadImage(item.SourcePath) : Placeholder();
        return Compose(cfg, item, screenshot);
    }

    private SKImage Compose(Config cfg, PlanItem item, SKImage screenshot)
    {
        var layout = item.Layout;

        // Opaque RGB surface — no alpha channel ever reaches the output.
        var info = new SKImageInfo(layout.W, layout.H, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        if (surface is null) throw new InvalidOperationException("failed to create render surface");
        var canvas = surface.Canvas;

        DrawBackground(cfg, canvas, layout.W, layout.H);
        DeviceFrameFactory.Create(item.Frame).Render(canvas, screenshot, layout, cfg, item.Target);
        DrawText(cfg, canvas, layout);

        canvas.Flush();
        return surface.Snapshot();
    }

    private static void DrawBackground(Config cfg, SKCanvas canvas, int w, int h)
    {
        var bg = cfg.Style.Background;
        if (bg.TypeEnum == BackgroundType.Flat)
        {
            canvas.Clear(Util.Color(bg.Color));
            return;
        }

        canvas.Clear(Util.Color(bg.Center));
        var center = new SKPoint(w / 2f, h / 2f);
        float radius = (float)Math.Sqrt(center.X * center.X + center.Y * center.Y);
        using var shader = SKShader.CreateRadialGradient(
            center, radius,
            new[] { Util.Color(bg.Center), Util.Color(bg.Edge) },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, w, h), paint);
    }

    private void DrawText(Config cfg, SKCanvas canvas, LayoutResult layout)
    {
        float centerX = layout.W / 2f;
        var titleFace = Face(cfg.Style.Font.Title, cfg.ConfigDir);
        var subtitleFace = Face(cfg.Style.Font.Subtitle, cfg.ConfigDir);

        using var titleFont = new SKFont(titleFace, layout.TitleSize)
            { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true, Embolden = cfg.Style.Font.TitleWeight >= 600 };
        using var titlePaint = new SKPaint { Color = Util.Color(cfg.Style.TitleColor), IsAntialias = true };
        float titleTracking = TextShaper.Tracking(layout.TitleSize, cfg.Style.Title.LetterSpacing);
        foreach (var line in layout.TitleLines)
            TextShaper.DrawCentered(canvas, titleFont, titlePaint, line.Text, centerX, line.Baseline, titleTracking);

        if (layout.SubtitleLines.Count > 0)
        {
            using var subFont = new SKFont(subtitleFace, layout.SubtitleSize)
                { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true, Embolden = cfg.Style.Font.SubtitleWeight >= 600 };
            using var subPaint = new SKPaint { Color = Util.Color(cfg.Style.SubtitleColor), IsAntialias = true };
            float subTracking = TextShaper.Tracking(layout.SubtitleSize, 0.01);
            foreach (var line in layout.SubtitleLines)
                TextShaper.DrawCentered(canvas, subFont, subPaint, line.Text, centerX, line.Baseline, subTracking);
        }
    }

    private static void Save(Config cfg, SKImage image, PlanItem item)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(item.OutputPath)!);

        var fmt = cfg.Output.FormatEnum;
        var encoded = fmt == OutputFormat.Jpeg
            ? image.Encode(SKEncodedImageFormat.Jpeg, 92)
            : image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null) throw new InvalidOperationException($"failed to encode {item.OutputPath}");

        var bytes = encoded.ToArray();

        // Compliance assertions on the actual bytes we are about to write.
        if (fmt == OutputFormat.Png)
        {
            var png = Png.Read(bytes);
            if (png.Width != item.Target.Width || png.Height != item.Target.Height)
                throw new InvalidOperationException(
                    $"{item.Target.Name}: encoded {png.Width}x{png.Height} != target {item.Target.Width}x{item.Target.Height}");
            if (png.HasAlpha)
                throw new InvalidOperationException(
                    $"{item.Target.Name}: output PNG has an alpha channel (color type {png.ColorType}); stores reject transparency");
        }

        File.WriteAllBytes(item.OutputPath, bytes);
    }

    private static string _cfgResolve(Config cfg, string image) =>
        string.IsNullOrWhiteSpace(image) ? "" : cfg.ResolvePath(image);

    private SKTypeface Face(string? configuredPath, string configDir)
    {
        string key = string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultFaceKey
            : (Path.IsPathRooted(configuredPath) ? configuredPath : Path.GetFullPath(Path.Combine(configDir, configuredPath)));
        if (_faceCache.TryGetValue(key, out var cached)) return cached;
        var face = FontLoader.Load(configuredPath, configDir, _warn);
        _faceCache[key] = face;
        return face;
    }

    private SKImage LoadImage(string path)
    {
        if (_imageCache.TryGetValue(path, out var cached)) return cached;
        using var data = SKData.Create(path);
        var img = data is null ? null : SKImage.FromEncodedData(data);
        if (img is null) throw new ConfigException($"could not decode image: {path}");
        _imageCache[path] = img;
        return img;
    }

    private SKImage? _placeholder;
    private SKImage Placeholder()
    {
        if (_placeholder is not null) return _placeholder;
        var info = new SKImageInfo(600, 1300, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        var canvas = surface!.Canvas;
        canvas.Clear(new SKColor(0x33, 0x33, 0x36));
        using (var paint = new SKPaint { Color = new SKColor(0x88, 0x88, 0x8C), IsAntialias = true })
        using (var font = new SKFont(SKTypeface.Default, 48))
        {
            const string msg = "no image";
            float w = font.MeasureText(msg);
            canvas.DrawText(msg, (600 - w) / 2f, 650, font, paint);
        }
        canvas.Flush();
        _placeholder = surface.Snapshot();
        return _placeholder;
    }

    public void Dispose()
    {
        foreach (var f in _faceCache.Values) f.Dispose();
        foreach (var img in _imageCache.Values) img.Dispose();
        _placeholder?.Dispose();
        _faceCache.Clear();
        _imageCache.Clear();
    }
}
