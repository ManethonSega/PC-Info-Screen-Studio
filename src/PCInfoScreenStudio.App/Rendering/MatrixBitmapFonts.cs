using System.Text.Json;

namespace PCInfoScreenStudio.Rendering;

internal static class MatrixBitmapFonts
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> Fonts = Load();
    private static Dictionary<string, Dictionary<string, string[]>> Load()
    {
        using var stream = typeof(MatrixBitmapFonts).Assembly.GetManifestResourceStream("PCInfoScreenStudio.Assets.MatrixFonts.json")
            ?? throw new InvalidOperationException("Matrix bitmap font resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(stream)!;
    }

    public static (int Width, bool[] Ink) Render(string text, int height, string key)
    {
        var font = Fonts[key];
        var glyphs = new List<(string[] Rows, int X)>(); int width = 0; bool space = false;
        foreach (char c in text)
        {
            if (c == ' ') { space = true; continue; }
            var rows = font.GetValueOrDefault(c.ToString(), font.GetValueOrDefault(char.ToUpperInvariant(c).ToString(), font["?"]));
            if (glyphs.Count != 0)
            {
                width += space ? 3 : 1;
                if (space)
                {
                    // Fixed-width cells can include empty side columns. Word gaps count dark LEDs, not cell edges.
                    var previous = glyphs[^1].Rows;
                    width -= Enumerable.Range(0, previous[0].Length).Reverse().TakeWhile(x => previous.All(row => row[x] == '0')).Count();
                    width -= Enumerable.Range(0, rows[0].Length).TakeWhile(x => rows.All(row => row[x] == '0')).Count();
                }
            }
            glyphs.Add((rows, width)); width += rows[0].Length; space = false;
        }
        var ink = new bool[width * height];
        foreach (var glyph in glyphs)
        {
            int top = Math.Max(0, height - glyph.Rows.Length);
            for (int y = 0; y < Math.Min(height, glyph.Rows.Length); y++) for (int x = 0; x < glyph.Rows[y].Length; x++)
                if (glyph.Rows[y][x] == '1') ink[(top + y) * width + glyph.X + x] = true;
        }
        return (width, ink);
    }
}
