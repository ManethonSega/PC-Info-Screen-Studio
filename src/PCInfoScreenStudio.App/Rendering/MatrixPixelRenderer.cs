using PCInfoScreenStudio.Models;
using SkiaSharp;

namespace PCInfoScreenStudio.Rendering;

/// <summary>The same bounded, binary pixel masks feed the editor and HID output.</summary>
public static class MatrixPixelRenderer
{
    private sealed record Glyph(bool[] Ink, int Left, int Right, int Top, int Bottom);
    private sealed record Mask(int Width, int Height, bool[] Ink);
    private static readonly Dictionary<char, Glyph> Glyphs = [];
    private static readonly Dictionary<(string, int, MatrixFiveDotFont), Mask> Masks = [];
    private static readonly object CacheGate = new();

    public static MatrixSettings Render(MatrixSettings settings, Func<string, int?> reading, DateTime utc, long scrollStep = 0)
    {
        var output = settings.ValidatedCopy();
        for (int i = 0; i < output.Count; i++)
        {
            var original = output.Tiles[i]; var tile = original;
            var native = MatrixSettings.Region(output.Layout, i); var content = output.ContentRegion(i);
            var rgb = new byte[2700];
            if (original.Content == MatrixContent.PixelArt)
            {
                for (int y = content.Y - native.Y; y < content.Y - native.Y + content.Height; y++)
                    Array.Copy(original.Pixels, y * native.Width * 3, rgb, y * native.Width * 3, native.Width * 3);
            }
            else
            {
                int? value = original.Content == MatrixContent.Sensor ? reading(original.Source) : null;
                string text = original.Content switch
                {
                    MatrixContent.Clock => utc.ToString("HH:mm"),
                    MatrixContent.Sensor => value is int number ? number + (original.Source.EndsWith("Usage") ? "%" : "°C") : "N/A",
                    _ => original.Text
                };
                var colour = original.Content == MatrixContent.Sensor && original.Alarm && value is int alarmValue && alarmValue >= original.AlarmThreshold
                    ? SKColors.Red : SKColor.Parse(original.Colour);
                var mask = TextMask(text, content.Height, settings.FiveDotFont);
                int step = original.Content == MatrixContent.Text && original.Scroll && mask.Width > content.Width
                    ? (int)(Math.Max(0, scrollStep) % (mask.Width + 3)) : 0;
                for (int y = 0; y < content.Height; y++) for (int x = 0; x < content.Width; x++)
                {
                    int sx = step == 0 ? x : (x + step) % (mask.Width + 3);
                    if (sx >= mask.Width || !mask.Ink[y * mask.Width + sx]) continue;
                    int p = ((y + content.Y - native.Y) * native.Width + x) * 3;
                    rgb[p] = colour.Red; rgb[p + 1] = colour.Green; rgb[p + 2] = colour.Blue;
                }
            }
            tile.Content = MatrixContent.PixelArt; tile.Pixels = rgb;
        }
        return output;
    }
    public static byte[] Compose(MatrixSettings rendered)
    {
        var frame = new byte[2700];
        for (int i = 0; i < rendered.Count; i++)
        {
            var region = MatrixSettings.Region(rendered.Layout, i);
            for (int y = 0; y < region.Height; y++)
                Array.Copy(rendered.Tiles[i].Pixels, y * region.Width * 3, frame, ((region.Y + y) * 90 + region.X) * 3, region.Width * 3);
        }
        return frame;
    }
    private static Mask TextMask(string input, int height, MatrixFiveDotFont choice)
    {
        // Collapse repeated spaces so the actual dark gap between words never exceeds three LEDs.
        string text = string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        lock (CacheGate)
        {
            if (Masks.TryGetValue((text, height, choice), out var cached)) return cached;
            if (height == 5)
            {
                var native = MatrixBitmapFonts.Render(text, choice);
                var result = new Mask(native.Width, 5, native.Ink);
                if (Masks.Count >= 32) Masks.Clear();
                Masks[(text, height, choice)] = result; return result;
            }
            var glyphs = text.Where(c => c != ' ').Select(GetGlyph).ToArray();
            if (glyphs.Length == 0) return new(0, height, []);
            int top = glyphs.Min(g => g.Top), bottom = glyphs.Max(g => g.Bottom), lineHeight = bottom - top + 1;
            int Width(Glyph g) => Math.Max(1, (int)Math.Ceiling((g.Right - g.Left + 1) * height / (double)lineHeight));
            int width = 0; bool space = false, first = true;
            foreach (char c in text)
            {
                if (c == ' ') { space = true; continue; }
                if (!first) width += space ? 3 : 1;
                width += Width(GetGlyph(c)); space = false; first = false;
            }
            var mask = new Mask(width, height, new bool[width * height]);
            int cursor = 0; space = false; first = true;
            foreach (char c in text)
            {
                if (c == ' ') { space = true; continue; }
                if (!first) cursor += space ? 3 : 1;
                var glyph = GetGlyph(c); int glyphWidth = Width(glyph);
                for (int y = 0; y < height; y++) for (int x = 0; x < glyphWidth; x++)
                {
                    // Coverage sampling keeps thin strokes when reducing to four or five rows.
                    int y0 = top + y * lineHeight / height, y1 = top + (y + 1) * lineHeight / height;
                    int x0 = glyph.Left + x * (glyph.Right - glyph.Left + 1) / glyphWidth;
                    int x1 = glyph.Left + (x + 1) * (glyph.Right - glyph.Left + 1) / glyphWidth;
                    for (int sy = y0; sy < Math.Max(y0 + 1, y1); sy++) for (int sx = x0; sx < Math.Max(x0 + 1, x1); sx++)
                        if (glyph.Ink[sy * 40 + sx]) mask.Ink[y * width + cursor + x] = true;
                }
                cursor += glyphWidth; space = false; first = false;
            }
            if (Masks.Count >= 32) Masks.Clear();
            Masks[(text, height, choice)] = mask;
            return mask;
        }
    }
    private static Glyph GetGlyph(char input)
    {
        // Input is limited to ASCII; the degree sign is generated by the sensor formatter.
        char c = input is >= '!' and <= '~' or '°' ? input : '?';
        if (Glyphs.TryGetValue(c, out var cached)) return cached;
        using var bitmap = new SKBitmap(40, 48); bitmap.Erase(SKColors.Black);
        using var canvas = new SKCanvas(bitmap); using var face = SKTypeface.FromFamilyName("Consolas");
        using var font = new SKFont(face, 32) { Edging = SKFontEdging.Alias };
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = false };
        canvas.DrawText(c.ToString(), 4, 36, font, paint);
        var ink = new bool[40 * 48]; int left = 39, right = 0, top = 47, bottom = 0;
        for (int y = 0; y < 48; y++) for (int x = 0; x < 40; x++)
            if (bitmap.GetPixel(x, y).Red > 0)
            { ink[y * 40 + x] = true; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < left) { left = right = 0; top = bottom = 0; }
        var glyph = new Glyph(ink, left, right, top, bottom); Glyphs[c] = glyph; return glyph;
    }
}
