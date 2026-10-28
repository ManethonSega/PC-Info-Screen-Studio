using PCInfoScreenStudio.Models;
using SkiaSharp;

namespace PCInfoScreenStudio.Rendering;

/// <summary>The same bounded, binary pixel masks feed the editor and HID output.</summary>
public static class MatrixPixelRenderer
{
    private sealed record Mask(int Width, int Height, bool[] Ink, int Left, int Right);
    private static readonly Dictionary<(string, int, string), Mask> Masks = [];
    private static readonly object CacheGate = new();

    public static MatrixSettings Render(MatrixSettings settings, Func<string, int?> reading, DateTime utc, long scrollStep = 0, Func<string, MatrixWidgetReading>? widgetReading = null)
    {
        var output = settings.ValidatedCopy();
        if (output.Widgets is not null)
        {
            var frame = widgetReading is null ? MatrixLayerRenderer.Compose(output, reading, utc, scrollStep)
                : MatrixLayerRenderer.ComposeReadings(output, widgetReading, utc, scrollStep);
            return new MatrixSettings { Layout = 1, Brightness = output.Brightness, Tiles = [new() { Content = MatrixContent.PixelArt, Pixels = frame }] }.ValidatedCopy();
        }
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
                var mask = TextMask(text, content.Height, output.FontKey(content.Height, i));
                bool scrolling = original.Content == MatrixContent.Text && original.Scroll && mask.Width > content.Width;
                int step = scrolling ? (int)(Math.Max(0, scrollStep) % (mask.Width + 3)) : 0;
                int left = scrolling ? 0 : original.Alignment switch
                {
                    MatrixAlignment.Centre => (content.Width - (mask.Right - mask.Left + 1)) / 2 - mask.Left,
                    MatrixAlignment.Right => content.Width - 1 - mask.Right,
                    _ => 0
                };
                for (int y = 0; y < content.Height; y++) for (int x = 0; x < content.Width; x++)
                {
                    int sx = scrolling ? (x + step) % (mask.Width + 3) : x - left;
                    if (sx < 0 || sx >= mask.Width || !mask.Ink[y * mask.Width + sx]) continue;
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
    private static Mask TextMask(string input, int height, string key)
    {
        string text = string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        lock (CacheGate)
        {
            var cacheKey = (text, height, key);
            if (Masks.TryGetValue(cacheKey, out var cached)) return cached;
            var native = MatrixBitmapFonts.Render(text, height, key);
            // Align visible pixels, excluding a monospaced font's empty edge columns.
            var columns = Enumerable.Range(0, native.Width).Where(x => Enumerable.Range(0, height).Any(y => native.Ink[y * native.Width + x])).ToArray();
            var result = new Mask(native.Width, height, native.Ink, columns.Length == 0 ? 0 : columns[0], columns.Length == 0 ? -1 : columns[^1]);
            if (Masks.Count >= 32) Masks.Clear();
            Masks[cacheKey] = result;
            return result;
        }
    }
}
