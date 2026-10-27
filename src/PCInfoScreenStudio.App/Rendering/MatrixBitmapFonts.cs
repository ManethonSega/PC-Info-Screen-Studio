using System.Text.Json;
using PCInfoScreenStudio.Models;
namespace PCInfoScreenStudio.Rendering;
internal static class MatrixBitmapFonts
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> Fonts = Load();
    private static Dictionary<string, Dictionary<string, string[]>> Load()
    {
        using var stream = typeof(MatrixBitmapFonts).Assembly.GetManifestResourceStream("PCInfoScreenStudio.Assets.MatrixFiveDotFonts.json")
            ?? throw new InvalidOperationException("Matrix bitmap font resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(stream)!;
    }
    public static (int Width, bool[] Ink) Render(string text, MatrixFiveDotFont choice)
    {
        var font = Fonts[choice.ToString()];
        var glyphs = new List<(string[] Rows, int X)>(); int width = 0; bool space = false;
        foreach (char c in text)
        {
            if (c == ' ') { space = true; continue; }
            if (glyphs.Count != 0) width += space ? 3 : 1;
            var rows = font.GetValueOrDefault(c.ToString(), font["?"]);
            glyphs.Add((rows, width)); width += rows[0].Length; space = false;
        }
        var ink = new bool[width * 5];
        foreach (var glyph in glyphs)
            for (int y = 0; y < 5; y++) for (int x = 0; x < glyph.Rows[y].Length; x++)
                ink[y * width + glyph.X + x] = glyph.Rows[y][x] == '1';
        return (width, ink);
    }
}
