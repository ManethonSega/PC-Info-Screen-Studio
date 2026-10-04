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
    AnalogClock,
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

public enum ScreenMode
{
    InfoScreen,
    PhotoFrame,
    Hybrid
}

public enum RuntimeScreenMode
{
    InfoScreen,
    PhotoFrame,
    Hybrid,
    Off
}

public enum PhotoTransition
{
    Crossfade,
    Slide,
    Zoom,
    KenBurns,
    Instant,
    Random
}

public enum PhotoBackgroundMode
{
    SolidColor,
    BlurredImage
}

public enum PhotoCaptionMode
{
    None,
    FileName,
    DateTaken,
    Location,
    Custom
}
