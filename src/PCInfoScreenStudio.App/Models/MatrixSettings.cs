using System.Text.Json.Serialization;

namespace PCInfoScreenStudio.Models;

public enum DisplayTarget { Serial35, Turing52, Matrix900 }
public sealed record DisplayTargetOption(string Label, DisplayTarget Value);
public enum MatrixFourDotFont { Mem, MemMono, Able4, LimitdotH4 }
public enum MatrixFiveDotFont { TomThumb, Pixel5x5Thin, Petty5H, Petty5x5, Able5, Mem5x6 }
public enum MatrixTenDotFont { Pixel5x5Double, Pixel5x10Condensed, Micro3x10, Piconano3x10 }
public sealed record MatrixFontOption(string Label, MatrixFiveDotFont Value);
public sealed record MatrixFourDotFontOption(string Label, MatrixFourDotFont Value);
public sealed record MatrixTenDotFontOption(string Label, MatrixTenDotFont Value);
public enum MatrixAlignment { Left, Centre, Right }
public sealed record MatrixAlignmentOption(string Label, MatrixAlignment Value);
public enum MatrixContent { Text, Sensor, Clock, PixelArt }
public sealed record MatrixContentOption(string Label, MatrixContent Value);
public sealed record MatrixLayoutOption(string Label, int Value);
public sealed record MatrixRowHeightOption(string Label, int Value);
public sealed record MatrixSourceOption(string Label, string Key, int FirmwareId);

public sealed class MatrixTileSettings
{
    public MatrixContent Content { get; set; }
    public string Text { get; set; } = "PC INFO SCREEN";
    public string Source { get; set; } = "CPU.Temperature";
    public string Colour { get; set; } = "#21E6E8";
    public bool Scroll { get; set; } = true;
    public bool Alarm { get; set; }
    public int AlarmThreshold { get; set; } = 80;
    // Null distinguishes older files with only workspace-wide fonts from an explicit default choice.
    public MatrixFourDotFont? FourDotFont { get; set; }
    public MatrixFiveDotFont? FiveDotFont { get; set; }
    public MatrixTenDotFont? TenDotFont { get; set; }
    public MatrixAlignment Alignment { get; set; }
    public byte[] Pixels { get; set; } = new byte[2700];
    internal MatrixTileSettings Copy(MatrixSettings legacy) => new()
    {
        Content = Enum.IsDefined(Content) ? Content : MatrixContent.Text,
        Text = (Text ?? string.Empty)[..Math.Min(Text?.Length ?? 0, 200)],
        Source = Sources.Any(s => s.Key == Source) ? Source : Sources[0].Key,
        Colour = System.Text.RegularExpressions.Regex.IsMatch(Colour ?? "", "^#[0-9a-fA-F]{6}$") ? Colour! : "#21E6E8",
        Scroll = Scroll, Alarm = Alarm, AlarmThreshold = Math.Clamp(AlarmThreshold, 0, 100),
        FourDotFont = ValidFont(FourDotFont, legacy.LegacyFourDotFont),
        FiveDotFont = ValidFont(FiveDotFont, legacy.LegacyFiveDotFont),
        TenDotFont = ValidFont(TenDotFont, legacy.LegacyTenDotFont),
        Alignment = Enum.IsDefined(Alignment) ? Alignment : MatrixAlignment.Left,
        Pixels = Pixels?.Length == 2700 ? (byte[])Pixels.Clone() : new byte[2700]
    };
    private static T ValidFont<T>(T? selected, T legacy) where T : struct, Enum =>
        selected is { } font && Enum.IsDefined(font) ? font : Enum.IsDefined(legacy) ? legacy : default;
    public static IReadOnlyList<MatrixSourceOption> Sources { get; } =
    [ new("CPU temperature", "CPU.Temperature", 1), new("GPU temperature", "GPU.Temperature", 2),
      new("CPU load", "CPU.Usage", 27), new("GPU load", "GPU.Usage", 30) ];
}

public sealed class MatrixSettings
{
    public bool Selected { get; set; }
    public int Layout { get; set; } = 3;
    public int Brightness { get; set; } = 30;
    public int RowHeight { get; set; } = 4;
    // Read the old JSON names only as migration defaults; editor controls use tile fields.
    [JsonPropertyName("FourDotFont")]
    public MatrixFourDotFont LegacyFourDotFont { get; set; }
    [JsonPropertyName("FiveDotFont")]
    public MatrixFiveDotFont LegacyFiveDotFont { get; set; } = MatrixFiveDotFont.TomThumb;
    [JsonPropertyName("TenDotFont")]
    public MatrixTenDotFont LegacyTenDotFont { get; set; }
    public List<MatrixTileSettings> Tiles { get; set; } = Enumerable.Range(0, 6).Select(i => new MatrixTileSettings
        { Content = i == 1 ? MatrixContent.Sensor : i == 2 ? MatrixContent.Clock : MatrixContent.Text }).ToList();
    public MatrixSettings ValidatedCopy() => new()
    {
        Selected = Selected, Layout = Layout is 1 or 3 or 9 ? Layout : 3, Brightness = Math.Clamp(Brightness, 0, 100),
        RowHeight = RowHeight is 4 or 5 or 45 or 54 ? RowHeight : 4,
        LegacyFourDotFont = Enum.IsDefined(LegacyFourDotFont) ? LegacyFourDotFont : MatrixFourDotFont.Mem,
        LegacyFiveDotFont = Enum.IsDefined(LegacyFiveDotFont) ? LegacyFiveDotFont : MatrixFiveDotFont.TomThumb,
        LegacyTenDotFont = Enum.IsDefined(LegacyTenDotFont) ? LegacyTenDotFont : MatrixTenDotFont.Pixel5x5Double,
        Tiles = Enumerable.Range(0, 6).Select(i => (Tiles is not null && i < Tiles.Count && Tiles[i] is not null
            ? Tiles[i] : new MatrixTileSettings()).Copy(this)).ToList()
    };
    public static (int X, int Y, int Width, int Height) Region(int layout, int index) => layout switch
    {
        1 => (0, 0, 90, 10),
        3 => (index * 30, 0, 30, 10),
        9 => (index / 2 * 30, index % 2 * 5, 30, 5),
        _ => throw new ArgumentOutOfRangeException(nameof(layout))
    };
    public int Count => Layout == 1 ? 1 : Layout == 3 ? 3 : 6;
    public int TopRowHeight => RowHeight is 5 or 54 ? 5 : 4;
    public int BottomRowHeight => RowHeight is 5 or 45 ? 5 : 4;
    public string FontKey(int height, int index) => height switch
    {
        4 => "Four" + (Tiles[index].FourDotFont ?? LegacyFourDotFont),
        5 => "Five" + (Tiles[index].FiveDotFont ?? LegacyFiveDotFont),
        _ => "Ten" + (Tiles[index].TenDotFont ?? LegacyTenDotFont)
    };
    public (int X, int Y, int Width, int Height) ContentRegion(int index)
    {
        var region = Region(Layout, index);
        if (Layout != 9) return region;
        int height = index % 2 == 0 ? TopRowHeight : BottomRowHeight;
        return (region.X, index % 2 == 0 ? 0 : 10 - height, region.Width, height);
    }
    public static int TileAt(int layout, int x, int y) => x is < 0 or >= 90 || y is < 0 or >= 10 ? -1
        : layout == 1 ? 0 : layout == 3 ? x / 30 : layout == 9 ? x / 30 * 2 + y / 5 : -1;
}
