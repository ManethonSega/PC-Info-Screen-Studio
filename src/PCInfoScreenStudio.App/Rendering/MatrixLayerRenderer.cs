using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Rendering;

/// <summary>Composites software widgets before the controller receives one ordinary 90x10 image.</summary>
public static class MatrixLayerRenderer
{
    private sealed record Raster(int Width, int Height, byte[] Rgba);
    private static readonly object Gate = new();
    private static readonly Dictionary<(string, int, string, uint), Raster> TextCache = [];
    private static readonly Dictionary<(string, int), Raster> EmojiCache = [];
    public static bool IsEmoji(string text) => text.EnumerateRunes().Any(r => r.Value is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or 0xFE0F or 0x20E3 or 0x2764 or 0x2B50);
    public static byte[] Compose(MatrixSettings settings, Func<string, int?> reading, DateTime utc, long step = 0)
        => ComposeReadings(settings, source => MatrixWidgetReading.Legacy(source, reading(source)), utc, step);

    public static byte[] ComposeReadings(MatrixSettings settings, Func<string, MatrixWidgetReading> reading, DateTime utc, long step = 0)
    {
        var frame = new byte[2700];
        foreach (var w in settings.Widgets ?? [])
        {
            if (!w.Visible) continue;
            if (w.Content is MatrixContent.PixelArt or MatrixContent.ProgressBar or MatrixContent.FlyingArt)
            {
                var source = w.Content == MatrixContent.PixelArt ? w.Pixels : MatrixGraphicsRenderer.Render(w,w.Content==MatrixContent.ProgressBar && w.UseSensor ? reading(w.Source) : MatrixWidgetReading.Unavailable,step);
                for (int y = 0; y < w.Height; y++) for (int x = 0; x < w.Width; x++)
                {
                    int p = (y * 90 + x) * 3, d = ((w.Y + y) * 90 + w.X + x) * 3;
                    if (source[p] == 0 && source[p + 1] == 0 && source[p + 2] == 0) continue;
                    Array.Copy(source, p, frame, d, 3);
                }
                continue;
            }
            var raster = WidgetRaster(w, w.Content == MatrixContent.Sensor ? reading(w.Source) : MatrixWidgetReading.Unavailable, utc);
            var (left, right) = InkBounds(raster);
            int width = w.AutoWidth ? Math.Clamp(right - left + 1, 1, 90) : w.Width;
            int originX = Math.Min(w.X, 90 - width);
            bool movingText = w.Content == MatrixContent.Text && w.Scroll;
            bool scroll = movingText || w.Scroll && (w.AutoWidth ? right - left + 1 : raster.Width) > width;
            int phase = scroll && !movingText ? (int)(Math.Max(0, step) % (raster.Width + 3)) : 0;
            int movingOrigin = movingText ? MatrixGraphicsRenderer.MotionOrigin(width, raster.Width, w.FlightDirection, w.FlightSpeed, true, step) : 0;
            int offset = scroll ? 0 : w.Alignment == MatrixAlignment.Centre ? (width - (right - left + 1)) / 2 - left : w.Alignment == MatrixAlignment.Right ? width - 1 - right : w.AutoWidth ? -left : 0;
            for (int y = 0; y < w.Height; y++) for (int x = 0; x < width; x++)
            {
                int sx = movingText ? x - movingOrigin : scroll ? (x + phase) % (raster.Width + 3) : x - offset;
                if (sx < 0 || sx >= raster.Width) continue;
                int p = (y * raster.Width + sx) * 4, d = ((w.Y + y) * 90 + originX + x) * 3, alpha = raster.Rgba[p + 3];
                for (int channel = 0; channel < 3; channel++) frame[d + channel] = (byte)((raster.Rgba[p + channel] * alpha + frame[d + channel] * (255 - alpha) + 127) / 255);
            }
        }
        return frame;
    }
    public static void FitWidths(MatrixSettings settings, Func<string, MatrixWidgetReading> reading, DateTime utc)
    {
        foreach (var w in settings.Widgets ?? [])
        {
            if (!w.AutoWidth || w.Content is MatrixContent.PixelArt or MatrixContent.ProgressBar or MatrixContent.FlyingArt || w.Content == MatrixContent.Text && w.Scroll) continue;
            var bounds = InkBounds(WidgetRaster(w, w.Content == MatrixContent.Sensor ? reading(w.Source) : MatrixWidgetReading.Unavailable, utc));
            w.Width = Math.Clamp(bounds.Right - bounds.Left + 1, 1, 90);
            w.X = Math.Min(w.X, 90 - w.Width);
        }
    }
    public static string ContentText(MatrixWidgetSettings widget, MatrixWidgetReading reading, DateTime utc) => widget.Content switch
    {
        MatrixContent.Clock => utc.ToString("HH:mm"),
        MatrixContent.Sensor => widget.Prefix + (widget.Prefix.Length > 0 && !char.IsWhiteSpace(widget.Prefix[^1]) ? " " : "") + reading.Text,
        _ => widget.Text
    };
    private static Raster WidgetRaster(MatrixWidgetSettings w, MatrixWidgetReading reading, DateTime utc)
    {
        string text = ContentText(w, reading, utc);
        var colour = w.Content == MatrixContent.Sensor && w.Alarm && reading.Numeric is double v && v >= w.AlarmThreshold ? SKColors.Red : SKColor.Parse(w.Colour);
        lock (Gate)
        {
            var key = (text, w.Height, w.FontKey, (uint)colour);
            if (TextCache.TryGetValue(key, out var raster)) return raster;
            raster = BuildText(text, w.Height, w.FontKey, colour);
            if (TextCache.Count >= 32) TextCache.Clear();
            TextCache[key] = raster; return raster;
        }
    }
    internal static (int Width, byte[] Rgba) RenderLabel(MatrixWidgetSettings w, string text, SKColor colour)
    {
        lock (Gate)
        {
            var key = (text, w.Height, w.FontKey, (uint)colour);
            if (!TextCache.TryGetValue(key, out var raster))
            {
                raster = BuildText(text, w.Height, w.FontKey, colour);
                if (TextCache.Count >= 32) TextCache.Clear();
                TextCache[key] = raster;
            }
            return (raster.Width, raster.Rgba);
        }
    }
    private static (int Left, int Right) InkBounds(Raster raster)
    {
        int left = raster.Width, right = -1;
        for (int x = 0; x < raster.Width; x++) for (int y = 0; y < raster.Height; y++)
            if (raster.Rgba[(y * raster.Width + x) * 4 + 3] != 0) { left = Math.Min(left, x); right = x; break; }
        return right < 0 ? (0, -1) : (left, right);
    }
    private static Raster BuildText(string text, int height, string key, SKColor colour)
    {
        // Keep ordinary bitmap text byte-identical to the approved font renderer.
        if (!IsEmoji(text)) return BitmapText(text, height, key, colour);
        var parts = new List<(Raster Raster, int X)>(); int width = 0; bool space = false;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (string.IsNullOrWhiteSpace(element)) { space = true; continue; }
            var raster = IsEmoji(element) ? Emoji(element, height) : BitmapText(element, height, key, colour);
            // Isolated selectors and unsupported emoji can shape to no visible ink.
            // Keep them visible and prevent whitespace trimming consuming a whole raster.
            if (!Enumerable.Range(0, raster.Width * height).Any(i => raster.Rgba[i * 4 + 3] != 0))
                raster = BitmapText("?", height, key, colour);
            if (parts.Count > 0)
            {
                width += space ? 3 : 1;
                if (space)
                {
                    var previous = parts[^1].Raster;
                    bool Dark(Raster r, int x) => Enumerable.Range(0, height).All(y => r.Rgba[(y * r.Width + x) * 4 + 3] == 0);
                    width -= Enumerable.Range(0, previous.Width).Reverse().TakeWhile(x => Dark(previous, x)).Count();
                    width -= Enumerable.Range(0, raster.Width).TakeWhile(x => Dark(raster, x)).Count();
                }
            }
            parts.Add((raster, width)); width += raster.Width; space = false;
        }
        var bytes = new byte[width * height * 4];
        foreach (var (raster, position) in parts) for (int y = 0; y < height; y++) for (int x = 0; x < raster.Width; x++)
            if (raster.Rgba[(y * raster.Width + x) * 4 + 3] != 0)
                Array.Copy(raster.Rgba, (y * raster.Width + x) * 4, bytes, (y * width + position + x) * 4, 4);
        return new(width, height, bytes);
    }
    private static Raster BitmapText(string text, int height, string key, SKColor colour)
    {
        var mask = MatrixBitmapFonts.Render(text, height, key); var bytes = new byte[mask.Width * height * 4];
        for (int i = 0; i < mask.Ink.Length; i++) if (mask.Ink[i])
        { bytes[i * 4] = colour.Red; bytes[i * 4 + 1] = colour.Green; bytes[i * 4 + 2] = colour.Blue; bytes[i * 4 + 3] = 255; }
        return new(mask.Width, height, bytes);
    }
    private static Raster Emoji(string element, int height)
    {
        if (EmojiCache.TryGetValue((element, height), out var cached)) return cached;
        using var face = SKTypeface.FromFamilyName("Segoe UI Emoji");
        using var font = new SKFont(face, 64); using var shaper = new SKShaper(face);
        var shape = shaper.Shape(element, font);
        using var builder = new SKTextBlobBuilder();
        var run = builder.AllocateRawPositionedRun(font, shape.Codepoints.Length, null);
        for (int i = 0; i < shape.Codepoints.Length; i++) { run.Glyphs[i] = (ushort)shape.Codepoints[i]; run.Positions[i] = shape.Points[i]; }
        using var blob = builder.Build();
        if (blob is null || blob.Bounds.IsEmpty) return new(height, height, new byte[height * height * 4]);
        var bounds = blob.Bounds;
        using var bitmap = new SKBitmap(Math.Clamp((int)Math.Ceiling(bounds.Width) + 8, 8, 256), Math.Clamp((int)Math.Ceiling(bounds.Height) + 8, 8, 256));
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap)) using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
            canvas.DrawText(blob, 4 - bounds.Left, 4 - bounds.Top, paint);
        int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) if (bitmap.GetPixel(x, y).Alpha > 8)
        { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
        if (maxX < minX) return new(height, height, new byte[height * height * 4]);
        using var crop = new SKBitmap(maxX - minX + 1, maxY - minY + 1);
        bitmap.ExtractSubset(crop, new SKRectI(minX, minY, maxX + 1, maxY + 1));
        int width = Math.Clamp((int)Math.Round(crop.Width * height / (double)crop.Height), 1, 20);
        using var small = crop.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear))!;
        var bytes = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { var c = small.GetPixel(x, y); int p = (y * width + x) * 4; bytes[p] = c.Red; bytes[p + 1] = c.Green; bytes[p + 2] = c.Blue; bytes[p + 3] = c.Alpha; }
        var result = new Raster(width, height, bytes);
        if (EmojiCache.Count >= 128) EmojiCache.Clear();
        EmojiCache[(element, height)] = result; return result;
    }
}
