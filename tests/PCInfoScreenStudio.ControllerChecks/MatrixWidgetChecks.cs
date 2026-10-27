using System.Text.Json;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

internal static class MatrixWidgetChecks
{
    public static void Check()
    {
        var utc = new DateTime(2026, 10, 27, 12, 34, 0, DateTimeKind.Utc);
        foreach (int layout in new[] { 1, 3, 9 }) foreach (int rows in new[] { 4, 5, 45, 54 })
        {
            var legacy = new MatrixSettings { Layout = layout, RowHeight = rows };
            for (int i = 0; i < 6; i++)
            {
                var tile = legacy.Tiles[i]; tile.Content = (MatrixContent)(i % 4); tile.Text = "HI"; tile.Scroll = false;
                tile.FourDotFont = (MatrixFourDotFont)(i % 4); tile.FiveDotFont = (MatrixFiveDotFont)(i % 6); tile.TenDotFont = (MatrixTenDotFont)(i % 4);
                tile.Alignment = (MatrixAlignment)(i % 3);
                for (int p = 0; p < tile.Pixels.Length; p++) tile.Pixels[p] = (byte)(p % 127);
            }
            var expected = MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(legacy, _ => 42, utc));
            var migrated = legacy.ToWidgetWorkspace();
            var actual = MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(migrated, _ => 42, utc));
            Require(actual.SequenceEqual(expected), "Every existing panel/column/row layout migrates to pixel-identical layers, including artwork, fonts and alignment.");
            Require(migrated.Widgets!.Count == legacy.Count && legacy.Widgets is null && migrated.Tiles[0].Pixels.SequenceEqual(legacy.Tiles[0].Pixels), "Migration preserves the old data and does not mutate its source.");
            var loaded = JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(migrated))!.ToWidgetWorkspace();
            Require(loaded.Widgets!.Select(w => w.Id).SequenceEqual(migrated.Widgets.Select(w => w.Id)), "Migrated layer IDs and order survive restart without remigration.");
        }
        var back = new MatrixWidgetSettings { Content = MatrixContent.PixelArt, Width = 1, Height = 4, X = 4, Y = 3 };
        back.Pixels[0] = 255;
        var front = new MatrixWidgetSettings { Content = MatrixContent.PixelArt, Width = 1, Height = 4, X = 4, Y = 3 };
        front.Pixels[2] = 255;
        var document = new MatrixSettings { Widgets = [back, front] }.ValidatedCopy();
        int offset = (3 * 90 + 4) * 3;
        var frame = MatrixLayerRenderer.Compose(document, _ => null, utc);
        Require(frame[offset] == 0 && frame[offset + 2] == 255, "The last layer draws above the first at the exact LED-grid coordinate.");
        document.Widgets![1].Visible = false; frame = MatrixLayerRenderer.Compose(document, _ => null, utc);
        Require(frame[offset] == 255 && frame[offset + 2] == 0, "Hiding a layer reveals the layer underneath.");
        document.Widgets.Reverse(); document.Widgets[0].Visible = true;
        frame = MatrixLayerRenderer.Compose(document, _ => null, utc);
        Require(frame[offset] == 255, "Changing layer order changes overlapping pixels.");
        var invalid = new MatrixSettings { Widgets = [new() { Id = Guid.Empty, X = -10, Y = 99, Width = 900, Height = 8 }] }.ValidatedCopy().Widgets![0];
        Require(invalid.X == 0 && invalid.Y == 5 && invalid.Width == 90 && invalid.Height == 5 && invalid.Id != Guid.Empty, "Corrupt geometry recovers inside the 90 by 10 grid.");
        var empty = JsonSerializer.Deserialize<MatrixSettings>("{\"Widgets\":[]}")!.ToWidgetWorkspace();
        Require(empty.Widgets!.Count == 0 && MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(empty, _ => null, utc)).All(b => b == 0), "An intentionally empty canvas stays empty after restart and outputs a black panel.");
        var ids = new MatrixSettings { Widgets = [back, back] }.ValidatedCopy().Widgets!;
        Require(ids[0].Id != ids[1].Id && !ReferenceEquals(ids[0].Pixels, ids[1].Pixels), "Copied layers retain independent IDs and artwork buffers.");
        Require(MatrixWidgetSettings.Limit("A👨‍👩‍👧‍👦B", 2) == "A👨‍👩‍👧‍👦", "Text limits preserve complete multi-codepoint emoji graphemes.");
        Console.WriteLine("PASS: Matrix layer migration, exact legacy pixel preservation, order/visibility, grid bounds, empty-canvas recovery and Unicode-safe text.");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
