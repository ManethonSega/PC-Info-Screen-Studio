using System.Text.Json;

namespace PCInfoScreenStudio.Rendering;

public sealed record MatrixArtVariant(int Height, int Width, string[] Palette, string[] Rows);
public sealed record MatrixArtPreset(string Key, string Label, MatrixArtVariant[] Variants);
public sealed record MatrixArtImage(int Width, int Height, byte[] Pixels);

public static class MatrixArtBank
{
    public static IReadOnlyList<MatrixArtPreset> All { get; } = Load();
    private static MatrixArtPreset[] Load()
    {
        using var stream = typeof(MatrixArtBank).Assembly.GetManifestResourceStream("PCInfoScreenStudio.Assets.MatrixArtBank.json")!;
        return JsonSerializer.Deserialize<MatrixArtPreset[]>(stream)!;
    }
    public static MatrixArtImage Get(string key, int height)
    {
        var variant = (All.FirstOrDefault(a => a.Key == key) ?? All[0]).Variants.Single(v => v.Height == height);
        var pixels = new byte[2700];
        for (int y = 0; y < height; y++) for (int x = 0; x < variant.Width; x++)
        {
            char index = variant.Rows[y][x]; if (index == '.') continue;
            int colour = Convert.ToInt32(variant.Palette[index <= '9' ? index - '0' : index - 'A' + 10], 16), p = (y * 90 + x) * 3;
            pixels[p] = (byte)(colour >> 16); pixels[p + 1] = (byte)(colour >> 8); pixels[p + 2] = (byte)colour;
        }
        return new(variant.Width, height, pixels);
    }
}
