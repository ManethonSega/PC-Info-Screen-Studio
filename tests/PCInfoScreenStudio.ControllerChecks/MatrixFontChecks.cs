using System.Text.Json;
using System.Text.Json.Nodes;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

internal static class MatrixFontChecks
{
    public static void Check()
    {
        CheckTileAlignment();
        CheckPerTileFonts();
        CheckLegacyFontMigration();
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
        settings.LegacyFourDotFont = MatrixFourDotFont.Able4; settings.LegacyFiveDotFont = MatrixFiveDotFont.Able5; settings.LegacyTenDotFont = MatrixTenDotFont.Micro3x10;
        var roundTrip = JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(settings))!.ValidatedCopy();
        Require(roundTrip.LegacyFourDotFont == settings.LegacyFourDotFont && roundTrip.LegacyFiveDotFont == settings.LegacyFiveDotFont && roundTrip.LegacyTenDotFont == settings.LegacyTenDotFont, "All three font selections survive a restart snapshot.");
        var invalid = new MatrixSettings { LegacyFourDotFont = (MatrixFourDotFont)99, LegacyFiveDotFont = (MatrixFiveDotFont)99, LegacyTenDotFont = (MatrixTenDotFont)99 }.ValidatedCopy();
        Require(invalid.LegacyFourDotFont == MatrixFourDotFont.Mem && invalid.LegacyFiveDotFont == MatrixFiveDotFont.TomThumb && invalid.LegacyTenDotFont == MatrixTenDotFont.Pixel5x5Double, "Invalid font fields independently recover to included defaults.");
        Console.WriteLine("PASS: 14 font choices, native bitmap bounds, exact 3-dot word spacing, condensed proportions, 4+4/5+5/4+5/5+4 text and artwork geometry, preservation and settings round trips.");
    }
    private static void CheckTileAlignment()
    {
        foreach (int height in new[] { 4, 5, 10 })
        {
            var settings = new MatrixSettings { Layout = height == 10 ? 3 : 9, RowHeight = height == 5 ? 5 : 4 };
            foreach (var tile in settings.Tiles) { tile.Content = MatrixContent.Text; tile.Text = "H"; tile.Scroll = false; }
            var before = Frame(settings, DateTime.UnixEpoch);
            foreach (int alignment in new[] { 0, 1, 2 })
            {
                var json = JsonNode.Parse(JsonSerializer.Serialize(settings))!;
                json["Tiles"]![0]!["Alignment"] = alignment;
                var aligned = JsonSerializer.Deserialize<MatrixSettings>(json.ToJsonString())!.ValidatedCopy();
                aligned = JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(aligned))!.ValidatedCopy();
                var mask = MatrixBitmapFonts.Render("H", height, height == 4 ? "FourMem" : height == 5 ? "FiveTomThumb" : "TenPixel5x5Double");
                var region = aligned.ContentRegion(0); int left = alignment == 1 ? (region.Width - mask.Width) / 2 : alignment == 2 ? region.Width - mask.Width : 0;
                var rgb = Frame(aligned, DateTime.UnixEpoch);
                for (int y = 0; y < 10; y++) for (int x = 0; x < 90; x++)
                {
                    int offset = (y * 90 + x) * 3;
                    if (x < region.X || x >= region.X + region.Width || y < region.Y || y >= region.Y + height)
                    { Require(before.AsSpan(offset, 3).SequenceEqual(rgb.AsSpan(offset, 3)), "Alignment edits cannot change another tile."); continue; }
                    int sx = x - region.X - left;
                    bool lit = sx >= 0 && sx < mask.Width && mask.Ink[(y - region.Y) * mask.Width + sx];
                    Require(rgb[offset] == (lit ? 33 : 0), $"Tile-local alignment {alignment} must place {height}-dot text at the expected horizontal position after reload.");
                }
            }
        }
        var sample = new MatrixSettings { Layout = 3 };
        foreach (var tile in sample.Tiles) { tile.Content = MatrixContent.Text; tile.Text = ""; }
        foreach (var kind in new[] { MatrixContent.Sensor, MatrixContent.Clock })
        {
            sample.Tiles[0].Content = kind;
            sample.Tiles[0].TenDotFont = MatrixTenDotFont.Micro3x10;
            var left = Frame(sample, DateTime.UnixEpoch);
            var region = sample.ContentRegion(0);
            var lit = Enumerable.Range(0, 30).Where(x => Enumerable.Range(0, 10).Any(y => left[(y * 90 + x) * 3] != 0)).ToArray();
            int textWidth = lit[^1] + 1;
            sample.Tiles[0].Alignment = MatrixAlignment.Right;
            var right = Frame(sample, DateTime.UnixEpoch);
            Require(right.Any(b => b != 0), "Sensor/clock alignment retains visible output.");
            for (int y = 0; y < 10; y++) for (int x = 0; x < 30; x++)
            {
                int sx = x - (30 - textWidth);
                Require(right[(y * 90 + x) * 3] == (sx >= 0 ? left[(y * 90 + sx) * 3] : 0), "Right alignment applies equally to generated sensor values and UTC time.");
            }
            sample.Tiles[0].Alignment = MatrixAlignment.Left;
        }
        sample.Tiles[0].Content = MatrixContent.Text; sample.Tiles[0].Text = new string('W', 50); sample.Tiles[0].Scroll = true;
        foreach (long step in new long[] { 0, 1, 17, 100 })
        {
            sample.Tiles[0].Alignment = MatrixAlignment.Left;
            var left = MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(sample, _ => 42, DateTime.UnixEpoch, step));
            foreach (var alignment in new[] { MatrixAlignment.Centre, MatrixAlignment.Right })
            {
                sample.Tiles[0].Alignment = alignment;
                var rgb = MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(sample, _ => 42, DateTime.UnixEpoch, step));
                Require(rgb.SequenceEqual(left), "Long messages scroll consistently from the first frame regardless of alignment.");
            }
        }
        var mono = new MatrixSettings { Layout = 9, RowHeight = 4 };
        foreach (var tile in mono.Tiles) { tile.Content = MatrixContent.Text; tile.Text = ""; }
        mono.Tiles[0].FourDotFont = MatrixFourDotFont.MemMono; mono.Tiles[0].Text = "."; mono.Tiles[0].Scroll = false;
        mono.Tiles[0].Alignment = MatrixAlignment.Right;
        var punctuation = Frame(mono, DateTime.UnixEpoch);
        Require(Enumerable.Range(0, 4).Any(y => punctuation[(y * 90 + 29) * 3] != 0), "Right alignment places visible punctuation at the edge even when the font cell has empty side columns.");
        sample.Tiles[0].Alignment = (MatrixAlignment)99;
        Require(sample.ValidatedCopy().Tiles[0].Alignment == MatrixAlignment.Left, "Invalid alignment recovers without changing valid tile data.");
        Console.WriteLine("PASS: per-tile left/centre/right alignment, exact pixel offsets and restart persistence.");
    }
    private static void CheckLegacyFontMigration()
    {
        const string legacyJson = """
            {"Layout":9,"RowHeight":45,"FourDotFont":2,"FiveDotFont":4,"TenDotFont":2,"Tiles":[
              {"Text":"KEEP ME"},
              {"FourDotFont":0,"FiveDotFont":0,"TenDotFont":0},
              {"FourDotFont":99,"FiveDotFont":1,"TenDotFont":99}
            ]}
            """;
        var migrated = JsonSerializer.Deserialize<MatrixSettings>(legacyJson)!.ValidatedCopy();
        Require(migrated.Tiles[0].FourDotFont == MatrixFourDotFont.Able4 && migrated.Tiles[0].FiveDotFont == MatrixFiveDotFont.Able5 && migrated.Tiles[0].TenDotFont == MatrixTenDotFont.Micro3x10 && migrated.Tiles[0].Text == "KEEP ME", "Older global font choices seed each tile while valid content survives.");
        Require(migrated.Tiles[1].FourDotFont == MatrixFourDotFont.Mem && migrated.Tiles[1].FiveDotFont == MatrixFiveDotFont.TomThumb && migrated.Tiles[1].TenDotFont == MatrixTenDotFont.Pixel5x5Double, "An explicitly chosen default font is retained instead of being mistaken for an old missing field.");
        Require(migrated.Tiles[2].FourDotFont == MatrixFourDotFont.Able4 && migrated.Tiles[2].FiveDotFont == MatrixFiveDotFont.Pixel5x5Thin && migrated.Tiles[2].TenDotFont == MatrixTenDotFont.Micro3x10, "Corrupt tile font fields recover independently, preserving valid choices on the same tile.");
        Require(migrated.Tiles[5].FiveDotFont == MatrixFiveDotFont.Able5, "Missing and inactive tiles also inherit legacy choices.");
        var copy = migrated.ValidatedCopy(); copy.Tiles[0].FiveDotFont = MatrixFiveDotFont.Petty5H;
        Require(migrated.Tiles[0].FiveDotFont == MatrixFiveDotFont.Able5 && copy.Tiles[1].FiveDotFont == MatrixFiveDotFont.TomThumb, "Validated drafts clone tile preferences without sharing mutable tile objects.");
        foreach (int layout in new[] { 1, 3, 9 }) foreach (int rows in new[] { 4, 5, 45, 54 })
        {
            copy.Layout = layout; copy.RowHeight = rows; copy = copy.ValidatedCopy();
            Require(copy.Tiles[0].FiveDotFont == MatrixFiveDotFont.Petty5H && copy.Tiles[1].FiveDotFont == MatrixFiveDotFont.TomThumb && copy.Tiles[0].FourDotFont == MatrixFourDotFont.Able4 && copy.Tiles[0].TenDotFont == MatrixTenDotFont.Micro3x10, "Layout and row-height changes retain each tile's separate font choice for every height.");
        }
        Console.WriteLine("PASS: legacy font migration, explicit default preservation, independent invalid-field recovery and inactive-tile preferences.");
    }
    private static void CheckPerTileFonts()
    {
        foreach (var (height, property, first, second, otherTile, firstKey, secondKey) in new[]
        {
            (4, "FourDotFont", 2, 1, 2, "FourAble4", "FourMemMono"),
            (5, "FiveDotFont", 1, 3, 1, "FivePixel5x5Thin", "FivePetty5x5"),
            (10, "TenDotFont", 2, 1, 1, "TenMicro3x10", "TenPixel5x10Condensed")
        })
        {
            var settings = new MatrixSettings { Layout = height == 10 ? 3 : 9, RowHeight = height == 5 ? 5 : 4 };
            foreach (var tile in settings.Tiles) { tile.Content = MatrixContent.Text; tile.Text = "H"; tile.Scroll = false; }
            var json = JsonNode.Parse(JsonSerializer.Serialize(settings))!;
            json["Tiles"]![0]![property] = first;
            json["Tiles"]![otherTile]![property] = second;
            var loaded = JsonSerializer.Deserialize<MatrixSettings>(json.ToJsonString())!.ValidatedCopy();
            CheckTile(loaded, 0, height, firstKey);
            CheckTile(loaded, otherTile, height, secondKey);
            var roundTrip = JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(loaded))!.ValidatedCopy();
            CheckTile(roundTrip, 0, height, firstKey);
            CheckTile(roundTrip, otherTile, height, secondKey);
            var before = Frame(roundTrip, DateTime.UnixEpoch);
            var change = JsonNode.Parse(JsonSerializer.Serialize(roundTrip))!;
            change["Tiles"]![0]![property] = second;
            var edited = JsonSerializer.Deserialize<MatrixSettings>(change.ToJsonString())!.ValidatedCopy();
            var after = Frame(edited, DateTime.UnixEpoch);
            bool targetChanged = false;
            var target = edited.ContentRegion(0);
            for (int y = 0; y < 10; y++) for (int x = 0; x < 90; x++) for (int c = 0; c < 3; c++)
            {
                int offset = (y * 90 + x) * 3 + c;
                if (before[offset] == after[offset]) continue;
                Require(x >= target.X && x < target.X + target.Width && y >= target.Y && y < target.Y + target.Height, "Changing one tile font leaves every other RGB pixel unchanged.");
                targetChanged = true;
            }
            Require(targetChanged, "Editing a tile font changes the target's rendered pixels.");
        }
        Console.WriteLine("PASS: per-tile 4-/5-/10-dot fonts, independent rendered pixels and restart persistence.");
    }
    private static void CheckTile(MatrixSettings settings, int index, int height, string key)
    {
        var rgb = Frame(settings, DateTime.UnixEpoch); var region = settings.ContentRegion(index);
        var mask = MatrixBitmapFonts.Render("H", height, key);
        for (int y = 0; y < height; y++) for (int x = 0; x < region.Width; x++)
        {
            bool expected = x < mask.Width && mask.Ink[y * mask.Width + x];
            int offset = ((region.Y + y) * 90 + region.X + x) * 3;
            Require(rgb[offset] == (expected ? 33 : 0) && rgb[offset + 1] == (expected ? 230 : 0) && rgb[offset + 2] == (expected ? 232 : 0), $"Tile {index} must render its own {height}-dot font ({key}).");
        }
    }
    private static byte[] Frame(MatrixSettings s, DateTime time) => MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(s, _ => 42, time));
    private static bool RowLit(byte[] rgb, int row) => rgb.Skip(row * 270).Take(270).Any(b => b != 0);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
