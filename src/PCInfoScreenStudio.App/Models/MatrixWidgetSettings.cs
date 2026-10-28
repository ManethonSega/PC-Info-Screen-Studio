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
    public MatrixBarStyle BarStyle { get; set; }
    public bool UseSensor { get; set; } = true;
    public double RangeMinimum { get; set; }
    public double RangeMaximum { get; set; } = 100;
    public double ManualValue { get; set; } = 65;
    public string ArtKey { get; set; } = "";
    public int ArtWidth { get; set; } = 10;
    public MatrixAnimationStyle AnimationStyle { get; set; }
    public MatrixFlightDirection FlightDirection { get; set; }
    public int FlightSpeed { get; set; } = 5;
    public bool AnimationPlaying { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public string KindLabel => Content switch { MatrixContent.Sensor => "Value", MatrixContent.Clock => "Clock (UTC)", MatrixContent.PixelArt => "Pixel art", MatrixContent.ProgressBar => "Progress bar", MatrixContent.FlyingArt => "Animation · " + MatrixAnimationPresets.Get(AnimationStyle).Label, _ => "Text / emoji" };
    public string DisplayName => (Content is MatrixContent.Sensor or MatrixContent.ProgressBar) && !string.IsNullOrWhiteSpace(Prefix) ? Prefix.Trim() : Name;
    internal MatrixWidgetSettings CopyWidget(MatrixSettings legacy)
    {
        var c = Copy(legacy); int height = Height is 4 or 5 or 6 or 10 ? Height : 5, width = Math.Clamp(Width, 1, 90);
        if (Content == MatrixContent.ProgressBar) width = Nearest(width, 30, 45, 60);
        if (Content == MatrixContent.FlyingArt || Content == MatrixContent.Text && Scroll) width = width <= 60 ? 60 : 90;
        double minimum = double.IsFinite(RangeMinimum) ? Math.Clamp(RangeMinimum,-1000000,999999) : 0;
        double maximum = double.IsFinite(RangeMaximum) ? Math.Clamp(RangeMaximum,-999999,1000000) : 100;
        if (maximum <= minimum) maximum = minimum + 1;
        return new()
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id, Name = Limit(Name, 60),
            X = Math.Clamp(X, 0, 90 - width), Y = Math.Clamp(Y, 0, 10 - height), Width = width, Height = height,
            Visible = Visible, Locked = Locked, AutoWidth = Content == MatrixContent.Text && Scroll ? false : AutoWidth, Prefix = Limit(Prefix, 200),
            BarStyle = Enum.IsDefined(BarStyle) ? BarStyle : MatrixBarStyle.Segments, UseSensor = UseSensor,
            RangeMinimum = minimum, RangeMaximum = maximum,
            ManualValue = double.IsFinite(ManualValue) ? Math.Clamp(ManualValue,-1000000,1000000) : 0,
            ArtKey = Limit(ArtKey,60), ArtWidth = Math.Clamp(ArtWidth,1,90),
            AnimationStyle = Enum.IsDefined(AnimationStyle) ? AnimationStyle : MatrixAnimationStyle.Rocket,
            FlightDirection = Enum.IsDefined(FlightDirection) ? FlightDirection : MatrixFlightDirection.RightToLeft,
            FlightSpeed = Math.Clamp(FlightSpeed,1,20), AnimationPlaying = AnimationPlaying,
            Content = Enum.IsDefined(Content) ? Content : c.Content, Text = Limit(Text, 200), Source = string.IsNullOrWhiteSpace(Source) ? c.Source : Limit(Source, 512),
            Colour = c.Colour, Scroll = c.Scroll, Alarm = c.Alarm, AlarmThreshold = Math.Clamp(AlarmThreshold, -1000000, 1000000),
            FourDotFont = c.FourDotFont, FiveDotFont = c.FiveDotFont, TenDotFont = c.TenDotFont,
            Alignment = c.Alignment, Pixels = c.Pixels
        };
    }
    public string FontKey => Height switch
    { 4 => "Four" + (FourDotFont ?? MatrixFourDotFont.Mem), 5 or 6 => "Five" + (FiveDotFont ?? MatrixFiveDotFont.TomThumb), _ => "Ten" + (TenDotFont ?? MatrixTenDotFont.Pixel5x5Double) };
    private static int Nearest(int value, params int[] allowed) => allowed.MinBy(candidate => Math.Abs(candidate - value));
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
