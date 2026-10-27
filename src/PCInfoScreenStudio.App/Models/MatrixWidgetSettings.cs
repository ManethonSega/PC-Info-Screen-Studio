using System.Globalization;
namespace PCInfoScreenStudio.Models;

public sealed record MatrixWidgetSizeOption(string Label, int Value);
public readonly record struct MatrixWidgetReading(double? Numeric, string Text)
{
    public static MatrixWidgetReading Unavailable => new(null, "N/A");
    public static MatrixWidgetReading Legacy(string source, int? value) => value is int n
        ? new(n, n + (source.EndsWith("Usage") ? "%" : "°C")) : Unavailable;
}

/// <summary>A software layer on the 90 by 10 LED canvas. Pixels use a fixed 90-pixel source stride.</summary>
public sealed class MatrixWidgetSettings : MatrixTileSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Text";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 30;
    public int Height { get; set; } = 5;
    public bool AutoWidth { get; set; } = true;
    public string Prefix { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public string KindLabel => Content switch { MatrixContent.Sensor => "Value", MatrixContent.Clock => "Clock (UTC)", MatrixContent.PixelArt => "Pixel art", _ => "Text / emoji" };
    public string DisplayName => Content == MatrixContent.Sensor && !string.IsNullOrWhiteSpace(Prefix) ? Prefix.Trim() : Name;
    internal MatrixWidgetSettings CopyWidget(MatrixSettings legacy)
    {
        var c = Copy(legacy); int height = Height is 4 or 5 or 10 ? Height : 5, width = Math.Clamp(Width, 1, 90);
        return new()
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id, Name = Limit(Name, 60),
            X = Math.Clamp(X, 0, 90 - width), Y = Math.Clamp(Y, 0, 10 - height), Width = width, Height = height,
            Visible = Visible, Locked = Locked, AutoWidth = AutoWidth, Prefix = Limit(Prefix, 200),
            Content = c.Content, Text = Limit(Text, 200), Source = string.IsNullOrWhiteSpace(Source) ? c.Source : Limit(Source, 512),
            Colour = c.Colour, Scroll = c.Scroll, Alarm = c.Alarm, AlarmThreshold = Math.Clamp(AlarmThreshold, -1000000, 1000000),
            FourDotFont = c.FourDotFont, FiveDotFont = c.FiveDotFont, TenDotFont = c.TenDotFont,
            Alignment = c.Alignment, Pixels = c.Pixels
        };
    }
    public string FontKey => Height switch
    { 4 => "Four" + (FourDotFont ?? MatrixFourDotFont.Mem), 5 => "Five" + (FiveDotFont ?? MatrixFiveDotFont.TomThumb), _ => "Ten" + (TenDotFont ?? MatrixTenDotFont.Pixel5x5Double) };
    public static string Limit(string? text, int maximum)
    {
        text ??= ""; var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Length <= maximum ? text : text[..starts[maximum]];
    }
    public static List<MatrixWidgetSettings> Migrate(MatrixSettings source)
    {
        var settings = source.ValidatedCopy(); var result = new List<MatrixWidgetSettings>();
        for (int i = 0; i < settings.Count; i++)
        {
            var tile = settings.Tiles[i]; var r = settings.ContentRegion(i); var native = MatrixSettings.Region(settings.Layout, i);
            var w = new MatrixWidgetSettings
            {
                Name = tile.Content switch { MatrixContent.Sensor => "Value", MatrixContent.Clock => "Clock", MatrixContent.PixelArt => "Pixel art", _ => "Text" } + " " + (i + 1),
                Content = tile.Content, Text = tile.Text, Source = tile.Source, Colour = tile.Colour, Scroll = tile.Content == MatrixContent.Text && tile.Scroll,
                Alarm = tile.Alarm, AlarmThreshold = tile.AlarmThreshold, FourDotFont = tile.FourDotFont,
                FiveDotFont = tile.FiveDotFont, TenDotFont = tile.TenDotFont, Alignment = tile.Alignment,
                X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, AutoWidth = false
            };
            // Keep visible artwork first, followed by the previously hidden source row.
            int offset = r.Y - native.Y;
            for (int y = 0; y < native.Height; y++)
            {
                int destY = (y - offset + native.Height) % native.Height;
                Array.Copy(tile.Pixels, y * native.Width * 3, w.Pixels, destY * 90 * 3, native.Width * 3);
            }
            result.Add(w.CopyWidget(settings));
        }
        return result;
    }
}
