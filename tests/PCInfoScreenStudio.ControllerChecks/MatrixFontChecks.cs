using System.Text.Json;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

internal static class MatrixFontChecks
{
    public static void Check()
    {
        var utc = new DateTime(2026, 10, 27, 12, 34, 0, DateTimeKind.Utc);
        var settings = new MatrixSettings { Layout = 9 };
        foreach (var tile in settings.Tiles) { tile.Content = MatrixContent.Text; tile.Text = "H"; tile.Scroll = false; }
        foreach (int layout in new[] { 4, 5, 45, 54 })
        {
            settings.RowHeight = layout;
            int top = layout is 5 or 54 ? 5 : 4, bottom = layout is 5 or 45 ? 5 : 4;
            for (int i = 0; i < 6; i++)
            {
                var region = settings.ContentRegion(i);
                Require(region == (i / 2 * 30, i % 2 == 0 ? 0 : 10 - bottom, 30, i % 2 == 0 ? top : bottom), "Content geometry follows the selected top/bottom heights.");
            }
            var rgb = Frame(settings, utc);
            for (int y = 0; y < 10; y++)
                Require(RowLit(rgb, y) == (y < top || y >= 10 - bottom), "Each mixed-height layout lights the right rows and keeps its gap completely dark.");
            var loaded = JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(settings))!.ValidatedCopy();
            Require(loaded.RowHeight == layout && loaded.ContentRegion(1) == settings.ContentRegion(1), "All row layouts survive settings serialization.");
            foreach (var tile in settings.Tiles) { tile.Content = MatrixContent.PixelArt; Array.Fill(tile.Pixels, (byte)255); }
            rgb = Frame(settings, utc);
            for (int y = 0; y < 10; y++) Require(RowLit(rgb, y) == (y < top || y >= 10 - bottom), "Artwork uses the same mixed-row clipping as text.");
            Require(settings.Tiles.All(tile => tile.Pixels.All(b => b == 255)), "Layout clipping never discards source artwork.");
            foreach (var tile in settings.Tiles) tile.Content = MatrixContent.Text;
        }
        var choices = Enum.GetValues<MatrixFourDotFont>().Select(f => (4, "Four" + f))
            .Concat(Enum.GetValues<MatrixFiveDotFont>().Select(f => (5, "Five" + f)))
            .Concat(Enum.GetValues<MatrixTenDotFont>().Select(f => (10, "Ten" + f))).ToArray();
        Require(choices.Length == 14, "The complete approved shortlist is exposed.");
        foreach (var (height, key) in choices)
        {
            var h = MatrixBitmapFonts.Render("H", height, key);
            Require(h.Width > 0 && h.Ink.Length == h.Width * height, "Every installed font renders within its selected height.");
            for (int y = 0; y < height; y++) Require(h.Ink.Skip(y * h.Width).Take(h.Width).Any(v => v), "Every H fills the native height without up/down clipping.");
            var words = MatrixBitmapFonts.Render("H   H", height, key);
            var columns = Enumerable.Range(0, words.Width).Where(x => Enumerable.Range(0, height).Any(y => words.Ink[y * words.Width + x])).ToArray();
            var gaps = columns.Zip(columns.Skip(1)).Where(p => p.Second - p.First > 1).Select(p => p.Second - p.First - 1).ToArray();
            Require(gaps.SequenceEqual(new[] { 3 }), "Every font, including fixed-width mem, keeps exactly three dark word columns.");
            foreach (string text in new[] { "", "0O 1I 2Z 5S 8B", "Qqgjpy", "42°C", "N/A", "12:34", new string('W', 50) })
            {
                var mask = MatrixBitmapFonts.Render(text, height, key);
                Require(mask.Ink.Length == mask.Width * height && mask.Width <= 1600, "Symbols, descenders, time and long input remain bounded.");
            }
        }
        var native = MatrixBitmapFonts.Render("H", 5, "FivePixel5x5Thin");
        var wide = MatrixBitmapFonts.Render("H", 10, "TenPixel5x5Double");
        var narrow = MatrixBitmapFonts.Render("H", 10, "TenPixel5x10Condensed");
        Require(wide.Width == native.Width * 2 && narrow.Width == native.Width, "Condensed and doubled ten-dot variants retain their exact horizontal proportions.");
        for (int y = 0; y < 10; y++) for (int x = 0; x < wide.Width; x++)
        {
            Require(wide.Ink[y * wide.Width + x] == native.Ink[y / 2 * native.Width + x / 2], "Wide ten-dot glyphs double both axes exactly.");
            if (x < narrow.Width) Require(narrow.Ink[y * narrow.Width + x] == native.Ink[y / 2 * native.Width + x], "Condensed ten-dot glyphs double only the height.");
        }
        settings.FourDotFont = MatrixFourDotFont.Able4; settings.FiveDotFont = MatrixFiveDotFont.Able5; settings.TenDotFont = MatrixTenDotFont.Micro3x10;
        var roundTrip = JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(settings))!.ValidatedCopy();
        Require(roundTrip.FourDotFont == settings.FourDotFont && roundTrip.FiveDotFont == settings.FiveDotFont && roundTrip.TenDotFont == settings.TenDotFont, "All three font selections survive a restart snapshot.");
        var invalid = new MatrixSettings { FourDotFont = (MatrixFourDotFont)99, FiveDotFont = (MatrixFiveDotFont)99, TenDotFont = (MatrixTenDotFont)99 }.ValidatedCopy();
        Require(invalid.FourDotFont == MatrixFourDotFont.Mem && invalid.FiveDotFont == MatrixFiveDotFont.TomThumb && invalid.TenDotFont == MatrixTenDotFont.Pixel5x5Double, "Invalid font fields independently recover to included defaults.");
        Console.WriteLine("PASS: 14 font choices, native bitmap bounds, exact 3-dot word spacing, condensed proportions, 4+4/5+5/4+5/5+4 text and artwork geometry, preservation and settings round trips.");
    }
    private static byte[] Frame(MatrixSettings s, DateTime time) => MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(s, _ => 42, time));
    private static bool RowLit(byte[] rgb, int row) => rgb.Skip(row * 270).Take(270).Any(b => b != 0);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
