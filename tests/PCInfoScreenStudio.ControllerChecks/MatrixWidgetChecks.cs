using System.Text.Json;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;

internal static class MatrixWidgetChecks
{
    public static void Check()
    {
        var utc = new DateTime(2026, 10, 27, 12, 34, 0, DateTimeKind.Utc);
        var automatic = JsonSerializer.Deserialize<MatrixSettings>("{\"Widgets\":[{\"Text\":\"HH\",\"Width\":1,\"Height\":10,\"Scroll\":false,\"AutoWidth\":true}]}")!.ValidatedCopy();
        var fullContent = new MatrixSettings { Widgets = [new() { Text = "HH", Width = 90, Height = 10, Scroll = false }] }.ValidatedCopy();
        Require(MatrixLayerRenderer.Compose(automatic, _ => null, utc).SequenceEqual(MatrixLayerRenderer.Compose(fullContent, _ => null, utc)), "Automatic widget width must show all content even when its saved box is too narrow.");
        MatrixLayerRenderer.FitWidths(automatic, _ => MatrixWidgetReading.Unavailable, utc);
        int wideWidth = automatic.Widgets![0].Width;
        automatic.Widgets[0].Text = "H";
        MatrixLayerRenderer.FitWidths(automatic, _ => MatrixWidgetReading.Unavailable, utc);
        Require(automatic.Widgets[0].Width < wideWidth, "The selection box shrinks when content becomes shorter.");
        automatic.Widgets[0].X = 89; automatic.Widgets[0].Text = "HH";
        MatrixLayerRenderer.FitWidths(automatic, _ => MatrixWidgetReading.Unavailable, utc);
        Require(automatic.Widgets[0].X + automatic.Widgets[0].Width == 90, "Growing content moves its box inside the right edge instead of clipping.");
        automatic.Widgets[0].AutoWidth = false; automatic.Widgets[0].Width = 45;
        MatrixLayerRenderer.FitWidths(automatic, _ => MatrixWidgetReading.Unavailable, utc);
        Require(automatic.Widgets[0].Width == 45, "Manual width retains its explicit scrolling/alignment area.");
        var sensor = new MatrixSettings { Widgets = [new() { Content = MatrixContent.Sensor, Source = "Sensor: GPU > Board power", Prefix = "GPU:", Height = 5, AlarmThreshold = 2000 }] }.ValidatedCopy();
        var expectedText = new MatrixSettings { Widgets = [new() { Text = "GPU: 85.5 W", Height = 5, Scroll = false }] }.ValidatedCopy();
        sensor.Widgets![0].Scroll = false;
        Require(MatrixLayerRenderer.ComposeReadings(sensor, _ => new(85.5, "85.5 W"), utc).SequenceEqual(MatrixLayerRenderer.Compose(expectedText, _ => null, utc)), "Sensor labels, decimals and actual source units reach the LED RGB frame.");
        var restored = JsonSerializer.Deserialize<MatrixSettings>(JsonSerializer.Serialize(sensor))!.ValidatedCopy().Widgets![0];
        Require(restored.Source == "Sensor: GPU > Board power" && restored.Prefix == "GPU:" && restored.AutoWidth && restored.AlarmThreshold == 2000, "Extended sources, labels, width mode and non-percentage alarm thresholds survive restart.");
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
