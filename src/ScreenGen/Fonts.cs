using SkiaSharp;

namespace ScreenGen;

/// <summary>
/// Loads a typeface following the fallback chain: configured path -> bundled
/// font (next to the exe) -> SKTypeface.Default (with a warning).
///
/// Note: SkiaSharp 3.x dropped the variable-font axis API (SKFontArguments), so
/// the variable font is loaded at its default instance. Weight is approximated at
/// draw time via <see cref="SKFont.Embolden"/> for headings (see Renderer).
/// </summary>
public static class FontLoader
{
    private const string BundledFont = "Inter-VariableFont_opsz,wght.ttf";

    /// <summary>The configured value may be a font-file path OR an installed
    /// font-family name (e.g. "Inter", "Helvetica Neue").</summary>
    public static SKTypeface Load(string? configured, string configDir, Action<string> warn)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // 1. A font file (absolute or relative to the config dir).
            var full = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(configDir, configured));
            if (File.Exists(full))
            {
                var tf = SKTypeface.FromFile(full);
                if (tf is not null) return tf;
            }
            // 2. An installed font-family name.
            else if (!LooksLikePath(configured))
            {
                var match = SKFontManager.Default.MatchFamily(configured);
                if (match is not null &&
                    string.Equals(match.FamilyName, configured, StringComparison.OrdinalIgnoreCase))
                    return match;
            }
            warn($"font '{configured}' not found as a file or installed family, trying bundled font");
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", BundledFont);
        if (File.Exists(bundled))
        {
            var tf = SKTypeface.FromFile(bundled);
            if (tf is not null) return tf;
        }

        warn("falling back to the system default typeface");
        return SKTypeface.Default;
    }

    private static bool LooksLikePath(string s) =>
        s.Contains('/') || s.Contains('\\') || s.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
        || s.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);
}
