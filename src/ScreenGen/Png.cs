namespace ScreenGen;

/// <summary>Minimal PNG IHDR reader — width/height/bit-depth/colour-type straight
/// from the header. Colour type 2 = RGB, 6 = RGBA, 0 = grey, 4 = grey+alpha,
/// 3 = palette. Used to assert store compliance (exact dims, no alpha).</summary>
public readonly record struct PngInfo(int Width, int Height, int BitDepth, int ColorType)
{
    public bool HasAlpha => ColorType is 4 or 6;
    public bool IsTruecolorRgb => ColorType == 2;
}

public static class Png
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static PngInfo Read(ReadOnlySpan<byte> b)
    {
        if (b.Length < 26 || !b[..8].SequenceEqual(Signature))
            throw new InvalidDataException("not a PNG file");
        // 8 sig | 4 len | 4 'IHDR' | 4 width | 4 height | 1 bitdepth | 1 colortype
        int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        int h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        return new PngInfo(w, h, b[24], b[25]);
    }

    public static PngInfo Read(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> head = stackalloc byte[26];
        if (fs.Read(head) < 26) throw new InvalidDataException($"file too short to be a PNG: {path}");
        return Read(head);
    }
}
