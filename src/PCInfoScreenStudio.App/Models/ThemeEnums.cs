namespace PCInfoScreenStudio.Models;

public enum ThemeOrientation
{
    Landscape,
    Portrait
}

public enum DeviceRotation
{
    Degrees0 = 0,
    Degrees90 = 90,
    Degrees180 = 180,
    Degrees270 = 270
}

public enum WidgetType
{
    Text,
    Value,
    CircularGauge,
    BarGauge,
    Graph,
    Image,
    AnimatedImage,
    Video,
    Shape
}

public enum GraphStyle
{
    Line,
    SteppedLine,
    FilledArea,
    Blocks
}

public enum ShapeStyle
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Line
}

public enum MediaFit
{
    Fit,
    Fill,
    Stretch,
    Original
}

public enum ThemeAssetKind
{
    Image,
    Gif,
    Video,
    Font
}
