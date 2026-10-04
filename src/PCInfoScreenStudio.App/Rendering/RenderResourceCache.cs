using System.Collections.Concurrent;
using SkiaSharp;
using PCInfoScreenStudio.Models;

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

    // Prepared photo-frame images are already display-sized. Keep only a small
    // working set (current, previous and a few preloaded images) instead of one
    // bitmap for every file in a large album.
    private const int PreparedPhotoLimit = 12;
    private static readonly object PreparedPhotoSync = new();
    private static readonly Dictionary<string, SKBitmap> PreparedPhotos =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> PreparedPhotoLru = new();
    private static readonly Dictionary<string, LinkedListNode<string>> PreparedPhotoNodes =
        new(StringComparer.OrdinalIgnoreCase);

    public static SKBitmap? GetImage(string path)
    {
        try
        {
            return Images.GetOrAdd(path, DecodeOrientedImage);
        }
        catch
        {
            return null;
        }
    }

    public static SKBitmap? GetPreparedPhoto(
        string path,
        int width,
        int height,
        MediaFit fit,
        string backgroundColor)
    {
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
        var key = string.Join('|', path, stamp, width, height, fit, backgroundColor);
        lock (PreparedPhotoSync)
        {
            if (PreparedPhotos.TryGetValue(key, out var cached))
            {
                TouchPreparedPhoto(key);
                return cached;
            }
        }

        SKBitmap prepared;
        try { prepared = PreparePhoto(path, width, height, fit, backgroundColor); }
        catch { return null; }

        lock (PreparedPhotoSync)
        {
            if (PreparedPhotos.TryGetValue(key, out var existing))
            {
                prepared.Dispose();
                TouchPreparedPhoto(key);
                return existing;
            }

            PreparedPhotos[key] = prepared;
            PreparedPhotoNodes[key] = PreparedPhotoLru.AddFirst(key);
            while (PreparedPhotos.Count > PreparedPhotoLimit)
            {
                var oldest = PreparedPhotoLru.Last;
                if (oldest is null) break;
                PreparedPhotoLru.RemoveLast();
                PreparedPhotoNodes.Remove(oldest.Value);
                if (PreparedPhotos.Remove(oldest.Value, out var evicted))
                    evicted.Dispose();
            }
            return prepared;
        }
    }

    private static SKBitmap PreparePhoto(
        string path,
        int width,
        int height,
        MediaFit fit,
        string backgroundColor)
    {
        // Photo-frame files can be 20-50 megapixels. Decode a display-sized
        // version and dispose it after preparation; retaining the original
        // decoded bitmap was the main source of very high RAM usage.
        using var source = DecodeOrientedImage(path, width, height);
        var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(ParseColor(backgroundColor));
        var sourceRect = new SKRect(0, 0, source.Width, source.Height);
        var destination = DestinationForFit(sourceRect.Width, sourceRect.Height, width, height, fit);
        if (fit == MediaFit.Fill)
            sourceRect = CropForAspect(sourceRect, destination.Width / destination.Height, .5, .5);

        using var paint = new SKPaint { IsAntialias = true };
        using var image = SKImage.FromBitmap(source);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(image, sourceRect, destination, sampling, paint);
        return result;
    }

    private static void TouchPreparedPhoto(string key)
    {
        if (!PreparedPhotoNodes.TryGetValue(key, out var node)) return;
        PreparedPhotoLru.Remove(node);
        PreparedPhotoLru.AddFirst(node);
    }

    private static SKRect CropForAspect(SKRect source, float aspect, double focalX, double focalY)
    {
        var sourceAspect = source.Width / source.Height;
        if (sourceAspect > aspect)
        {
            var width = source.Height * aspect;
            var left = source.Left + (source.Width - width) * (float)Math.Clamp(focalX, 0, 1);
            return new SKRect(left, source.Top, left + width, source.Bottom);
        }
        var height = source.Width / aspect;
        var top = source.Top + (source.Height - height) * (float)Math.Clamp(focalY, 0, 1);
        return new SKRect(source.Left, top, source.Right, top + height);
    }

    private static SKRect DestinationForFit(float sourceWidth, float sourceHeight, int width, int height, MediaFit fit)
    {
        if (fit is MediaFit.Fill or MediaFit.Stretch)
            return new SKRect(0, 0, width, height);
        var scale = fit == MediaFit.Original
            ? Math.Min(1, Math.Min(width / sourceWidth, height / sourceHeight))
            : Math.Min(width / sourceWidth, height / sourceHeight);
        var targetWidth = sourceWidth * scale;
        var targetHeight = sourceHeight * scale;
        return new SKRect((width - targetWidth) / 2, (height - targetHeight) / 2, (width + targetWidth) / 2, (height + targetHeight) / 2);
    }

    private static SKBitmap DecodeOrientedImage(string path)
        => DecodeOrientedImage(path, null, null);

    private static SKBitmap DecodeOrientedImage(string path, int? targetWidth, int? targetHeight)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException($"Could not decode image '{path}'.");
        var decodeWidth = codec.Info.Width;
        var decodeHeight = codec.Info.Height;
        if (targetWidth is > 0 && targetHeight is > 0)
        {
            var scale = Math.Min(1f, Math.Max(
                targetWidth.Value / (float)codec.Info.Width,
                targetHeight.Value / (float)codec.Info.Height));
            var scaled = codec.GetScaledDimensions(scale);
            decodeWidth = Math.Max(1, scaled.Width);
            decodeHeight = Math.Max(1, scaled.Height);
        }

        var raw = new SKBitmap(decodeWidth, decodeHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        codec.GetPixels(raw.Info, raw.GetPixels());

        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft)
            return raw;

        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var oriented = new SKBitmap(swap ? raw.Height : raw.Width, swap ? raw.Width : raw.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(oriented);
        switch (codec.EncodedOrigin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(oriented.Width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(oriented.Width, oriented.Height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, oriented.Height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.Scale(-1, 1); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(oriented.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(oriented.Width, oriented.Height); canvas.Scale(-1, 1); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, oriented.Height); canvas.RotateDegrees(270); break;
        }
        canvas.DrawBitmap(raw, 0, 0);
        raw.Dispose();
        return oriented;
    }

    private static SKColor ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SKColors.Black;
        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb)
            ? new SKColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24))
            : SKColors.Black;
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

        lock (PreparedPhotoSync)
        {
            foreach (var image in PreparedPhotos.Values)
                image.Dispose();
            PreparedPhotos.Clear();
            PreparedPhotoLru.Clear();
            PreparedPhotoNodes.Clear();
        }

        foreach (var typeface in Typefaces.Values)
        {
            if (!ReferenceEquals(typeface, SKTypeface.Default))
                typeface.Dispose();
        }
        Typefaces.Clear();

        AnimatedGifFrameProvider.Clear();
    }
}
