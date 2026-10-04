using System.Collections.Concurrent;
using SkiaSharp;

namespace PCInfoScreenStudio.Rendering;

/// <summary>
/// Reuses decoded images and typefaces across preview frames. The cache is
/// cleared when a workspace is replaced or the application exits.
/// </summary>
internal static class RenderResourceCache
{
    private static readonly ConcurrentDictionary<string, SKBitmap> Images =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SKTypeface> Typefaces =
        new(StringComparer.OrdinalIgnoreCase);

    public static SKBitmap? GetImage(string path)
    {
        try
        {
            return Images.GetOrAdd(path, static p =>
                SKBitmap.Decode(p) ?? throw new InvalidDataException($"Could not decode image '{p}'."));
        }
        catch
        {
            return null;
        }
    }

    public static SKTypeface GetTypefaceFromFile(string path)
    {
        try
        {
            return Typefaces.GetOrAdd("file:" + path, static key =>
                SKTypeface.FromFile(key["file:".Length..]) ?? SKTypeface.Default);
        }
        catch
        {
            return SKTypeface.Default;
        }
    }

    public static SKTypeface GetTypefaceFromFamily(string family, SKFontStyle style)
    {
        var key = $"family:{family}|{style.Weight}|{style.Width}|{style.Slant}";
        return Typefaces.GetOrAdd(key, _ =>
            SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default);
    }

    public static void Clear()
    {
        foreach (var image in Images.Values)
            image.Dispose();
        Images.Clear();

        foreach (var typeface in Typefaces.Values)
        {
            if (!ReferenceEquals(typeface, SKTypeface.Default))
                typeface.Dispose();
        }
        Typefaces.Clear();

        AnimatedGifFrameProvider.Clear();
    }
}
