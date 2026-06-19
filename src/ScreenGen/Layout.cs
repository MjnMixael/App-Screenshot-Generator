using SkiaSharp;

namespace ScreenGen;

public sealed class LinePlacement
{
    public required string Text { get; init; }
    public required float Width { get; init; }
    public required float Baseline { get; init; }
}

/// <summary>Fully-resolved geometry for one canvas; the Renderer just draws it.</summary>
public sealed class LayoutResult
{
    public required int W { get; init; }
    public required int H { get; init; }

    public float TitleSize { get; init; }
    public List<LinePlacement> TitleLines { get; init; } = new();

    public float SubtitleSize { get; init; }
    public List<LinePlacement> SubtitleLines { get; init; } = new();

    public SKRect DeviceRect { get; init; }   // outer device/bezel rect (may extend below canvas)
    public SKRect ScreenRect { get; init; }   // inner screen rect where the screenshot is fit
    public float Bezel { get; init; }
    public float CornerRadius { get; init; }  // outer device corner radius

    public bool TextDeviceOverlap { get; init; }
    public float DeviceTop { get; init; }
    public float MinDeviceTop { get; init; }
}

/// <summary>
/// Computes title/subtitle sizing + wrapping and the device/screen rects from the
/// config layout fractions. All math is in fractions of canvas W/H, so one config
/// drives every resolution.
/// </summary>
public static class Layout
{
    private const float MaxTextLineWidthFrac = 0.86f;
    private const int MaxTitleLines = 2;

    /// <param name="screenAspect">Device screen aspect (w/h), taken from the target's
    /// pixel dimensions so the frame looks like that device (e.g. iPad 4:3) at any size.</param>
    public static LayoutResult Compute(Config cfg, Target target, SKTypeface titleFace, SKTypeface subtitleFace,
        string title, string? subtitle, float screenAspect)
    {
        int W = target.Width, H = target.Height;
        var L = cfg.Style.Layout;
        float maxLineW = MaxTextLineWidthFrac * W;

        // --- Title ---
        string titleText = cfg.Style.Title.Uppercase ? title.ToUpperInvariant() : title;
        using var titleFont = new SKFont(titleFace) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        float capRatio = TextShaper.CapRatio(titleFont);
        float targetCap = (float)(cfg.Style.Title.MaxSizePct * H);
        float titleSize = targetCap / capRatio;

        List<string> titleLines = FitTitle(titleFont, titleText, cfg.Style.Title.LetterSpacing, maxLineW, ref titleSize);
        titleFont.Size = titleSize;
        float titleTracking = TextShaper.Tracking(titleSize, cfg.Style.Title.LetterSpacing);
        float titleCap = capRatio * titleSize;
        float titleLineHeight = titleSize * 1.12f;

        // --- Subtitle ---
        bool hasSub = !string.IsNullOrWhiteSpace(subtitle);
        float subSize = titleSize * 0.40f;
        List<string> subLines = new();
        using var subFont = new SKFont(subtitleFace) { Edging = SKFontEdging.SubpixelAntialias, Subpixel = true };
        float subCapRatio = TextShaper.CapRatio(subFont);
        float subTracking = 0f;
        float subLineHeight = 0f;
        if (hasSub)
        {
            subLines = FitSubtitle(subFont, subtitle!, maxLineW, ref subSize);
            subFont.Size = subSize;
            subTracking = TextShaper.Tracking(subSize, 0.01);
            subLineHeight = subSize * 1.30f;
        }

        // --- Vertical placement of the text block ---
        float top = (float)(L.TopMarginPct * H);
        var titlePlacements = new List<LinePlacement>();
        float firstTitleBaseline = top + titleCap;
        for (int i = 0; i < titleLines.Count; i++)
        {
            float baseline = firstTitleBaseline + i * titleLineHeight;
            titlePlacements.Add(new LinePlacement
            {
                Text = titleLines[i],
                Width = TextShaper.LineWidth(titleFont, titleLines[i], titleTracking),
                Baseline = baseline,
            });
        }
        float titleVisualBottom = firstTitleBaseline + (titleLines.Count - 1) * titleLineHeight + titleSize * 0.18f;

        var subPlacements = new List<LinePlacement>();
        float textBlockBottom = titleVisualBottom;
        if (hasSub)
        {
            float subCap = subCapRatio * subSize;
            float firstSubBaseline = titleVisualBottom + (float)(L.TextBlockGapPct * H) + subCap;
            for (int i = 0; i < subLines.Count; i++)
            {
                float baseline = firstSubBaseline + i * subLineHeight;
                subPlacements.Add(new LinePlacement
                {
                    Text = subLines[i],
                    Width = TextShaper.LineWidth(subFont, subLines[i], subTracking),
                    Baseline = baseline,
                });
            }
            textBlockBottom = firstSubBaseline + (subLines.Count - 1) * subLineHeight + subSize * 0.22f;
        }

        // --- Device + screen rects ---
        // The device screen takes the TARGET device's aspect (from its pixel dims),
        // so an iPad target stays iPad-shaped at any size; the shot fills it via
        // cover (uniform scale, keep aspect, crop overflow). device_width_pct scales
        // the WHOLE device. It's anchored just below the text and extends downward:
        // at large sizes it bleeds off the bottom edge (signature look); shrink it
        // and the device fits fully inside.
        bool framed = DeviceFrameFactory.Resolve(cfg, target) != FrameType.None;
        float k = framed ? (float)cfg.Style.Frame.BezelWidthPct : 0f;  // bezel as fraction of device width

        float deviceWidth = (float)(L.DeviceWidthPct * W);
        float deviceH = deviceWidth * ((1f - 2f * k) / screenAspect + 2f * k);
        float deviceLeft = (W - deviceWidth) / 2f;
        float bezel = k * deviceWidth;

        float deviceTop = textBlockBottom + (float)(L.TitleToDeviceGapPct * H);
        float deviceBottom = deviceTop + deviceH;
        bool overlap = false;
        float minDeviceTop = deviceTop;

        var deviceRect = new SKRect(deviceLeft, deviceTop, deviceLeft + deviceWidth, deviceBottom);
        var screenRect = new SKRect(deviceLeft + bezel, deviceTop + bezel, deviceLeft + deviceWidth - bezel, deviceBottom - bezel);
        float cornerRadius = (float)(cfg.Style.Frame.CornerRadiusPct * deviceWidth);

        return new LayoutResult
        {
            W = W,
            H = H,
            TitleSize = titleSize,
            TitleLines = titlePlacements,
            SubtitleSize = subSize,
            SubtitleLines = subPlacements,
            DeviceRect = deviceRect,
            ScreenRect = screenRect,
            Bezel = bezel,
            CornerRadius = cornerRadius,
            TextDeviceOverlap = overlap,
            DeviceTop = deviceTop,
            MinDeviceTop = minDeviceTop,
        };
    }

    /// <summary>Wrap the title to &lt;= 2 lines within width, shrinking the font if needed.</summary>
    private static List<string> FitTitle(SKFont font, string text, double letterSpacing, float maxWidth, ref float size)
    {
        float minSize = 8f;
        List<string> lines = new() { text };
        for (int guard = 0; guard < 40; guard++)
        {
            font.Size = size;
            float tracking = TextShaper.Tracking(size, letterSpacing);
            lines = TextShaper.WrapGreedy(font, text, tracking, maxWidth);
            bool tooMany = lines.Count > MaxTitleLines;
            bool tooWide = lines.Any(l => TextShaper.LineWidth(font, l, tracking) > maxWidth);
            if (!tooMany && !tooWide) return lines;
            size *= 0.92f;
            if (size < minSize) { size = minSize; break; }
        }
        font.Size = size;
        return lines;
    }

    /// <summary>Wrap the subtitle to fit width, shrinking only if a single word overflows.</summary>
    private static List<string> FitSubtitle(SKFont font, string text, float maxWidth, ref float size)
    {
        float minSize = 6f;
        List<string> lines;
        for (int guard = 0; guard < 40; guard++)
        {
            font.Size = size;
            float tracking = TextShaper.Tracking(size, 0.01);
            lines = TextShaper.WrapGreedy(font, text, tracking, maxWidth);
            if (lines.All(l => TextShaper.LineWidth(font, l, tracking) <= maxWidth)) return lines;
            size *= 0.94f;
            if (size < minSize) { size = minSize; break; }
        }
        font.Size = size;
        return TextShaper.WrapGreedy(font, text, TextShaper.Tracking(size, 0.01), maxWidth);
    }
}
