namespace PCInfoScreenStudio.Models;

public enum DisplayTarget { Serial35, Turing52, Matrix900 }
public sealed record DisplayTargetOption(string Label, DisplayTarget Value);
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
    public byte[] Pixels { get; set; } = new byte[2700];
    internal MatrixTileSettings Copy() => new()
    {
        Content = Enum.IsDefined(Content) ? Content : MatrixContent.Text,
        Text = (Text ?? string.Empty)[..Math.Min(Text?.Length ?? 0, 200)],
        Source = Sources.Any(s => s.Key == Source) ? Source : Sources[0].Key,
        Colour = System.Text.RegularExpressions.Regex.IsMatch(Colour ?? "", "^#[0-9a-fA-F]{6}$") ? Colour! : "#21E6E8",
        Scroll = Scroll, Alarm = Alarm, AlarmThreshold = Math.Clamp(AlarmThreshold, 0, 100),
        Pixels = Pixels?.Length == 2700 ? (byte[])Pixels.Clone() : new byte[2700]
    };
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
    public List<MatrixTileSettings> Tiles { get; set; } = Enumerable.Range(0, 6).Select(i => new MatrixTileSettings
        { Content = i == 1 ? MatrixContent.Sensor : i == 2 ? MatrixContent.Clock : MatrixContent.Text }).ToList();
    public MatrixSettings ValidatedCopy() => new()
    {
        Selected = Selected, Layout = Layout is 1 or 3 or 9 ? Layout : 3, Brightness = Math.Clamp(Brightness, 0, 100),
        RowHeight = RowHeight == 5 ? 5 : 4,
        Tiles = Enumerable.Range(0, 6).Select(i => Tiles is not null && i < Tiles.Count && Tiles[i] is not null
            ? Tiles[i].Copy() : new MatrixTileSettings()).ToList()
    };
    public static (int X, int Y, int Width, int Height) Region(int layout, int index) => layout switch
    {
        1 => (0, 0, 90, 10),
        3 => (index * 30, 0, 30, 10),
        9 => (index / 2 * 30, index % 2 * 5, 30, 5),
        _ => throw new ArgumentOutOfRangeException(nameof(layout))
    };
    public int Count => Layout == 1 ? 1 : Layout == 3 ? 3 : 6;
    public (int X, int Y, int Width, int Height) ContentRegion(int index)
    {
        var region = Region(Layout, index);
        return Layout == 9 && RowHeight == 4
            ? (region.X, region.Y + (index % 2 == 0 ? 0 : 1), region.Width, 4) : region;
    }
    public static int TileAt(int layout, int x, int y) => x is < 0 or >= 90 || y is < 0 or >= 10 ? -1
        : layout == 1 ? 0 : layout == 3 ? x / 30 : layout == 9 ? x / 30 * 2 + y / 5 : -1;
}
