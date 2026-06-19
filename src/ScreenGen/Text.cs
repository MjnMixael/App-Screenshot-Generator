using SkiaSharp;

namespace ScreenGen;

/// <summary>
/// Letter-spacing aware text measurement + drawing. We lay out glyphs manually
/// (advance + tracking between characters) so the signature wide-tracked title
/// matches the house style. Latin uppercase titles don't need complex shaping,
/// so per-character placement is exact and avoids SKTextBlob API churn.
/// </summary>
public static class TextShaper
{
    /// <summary>Tracking in pixels for a given font size and em-fraction.</summary>
    public static float Tracking(float fontSize, double letterSpacingEm) => (float)(fontSize * letterSpacingEm);

    /// <summary>Total advance width of a line including tracking between characters.</summary>
    public static float LineWidth(SKFont font, string text, float trackingPx)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float w = 0f;
        foreach (var ch in text)
            w += font.MeasureText(ch.ToString());
        return w + trackingPx * (text.Length - 1);
    }

    /// <summary>Draw <paramref name="text"/> centered at <paramref name="centerX"/> on the given baseline.</summary>
    public static void DrawCentered(SKCanvas canvas, SKFont font, SKPaint paint,
        string text, float centerX, float baseline, float trackingPx)
    {
        if (string.IsNullOrEmpty(text)) return;
        float width = LineWidth(font, text, trackingPx);
        float x = centerX - width / 2f;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            canvas.DrawText(s, x, baseline, font, paint);
            x += font.MeasureText(s) + trackingPx;
        }
    }

    /// <summary>Cap-height-to-font-size ratio for a typeface (fraction of em).</summary>
    public static float CapRatio(SKFont font)
    {
        float prev = font.Size;
        font.Size = 100f;
        font.GetFontMetrics(out var m);
        font.Size = prev;
        float cap = Math.Abs(m.CapHeight);
        return cap > 1f ? cap / 100f : 0.70f; // fall back if the font omits cap height
    }

    /// <summary>Greedy word-wrap to fit <paramref name="maxWidth"/> at the font's current size.</summary>
    public static List<string> WrapGreedy(SKFont font, string text, float trackingPx, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        if (words.Length == 0) return lines;

        var current = words[0];
        for (int i = 1; i < words.Length; i++)
        {
            var candidate = current + " " + words[i];
            if (LineWidth(font, candidate, trackingPx) <= maxWidth)
                current = candidate;
            else { lines.Add(current); current = words[i]; }
        }
        lines.Add(current);
        return lines;
    }
}
