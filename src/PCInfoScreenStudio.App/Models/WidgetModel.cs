using System.Text.Json.Serialization;

namespace PCInfoScreenStudio.Models;

public sealed class WidgetModel : ObservableObject
{
    private string _name = "Widget";
    private WidgetType _type;
    private double _x = 20;
    private double _y = 20;
    private double _width = 120;
    private double _height = 60;
    private double _rotation;
    private int _zIndex;
    private bool _isVisible = true;
    private bool _isLocked;
    private bool _isSelected;
    private string _dataSource = "Preview.Value";
    private string _label = "Value";
    private string _valueFormat = "0";
    private string _suffix = "%";
    private double _simulatedValue = 42;
    private double? _runtimeValue;
    private string? _runtimeText;
    private string _fontFamily = "Segoe UI";
    private Guid? _fontAssetId;
    private double _fontSize = 18;
    private bool _fontBold;
    private bool _fontItalic;
    private string _foregroundColor = "#FFF2F2F2";
    private string _backgroundColor = "#00000000";
    private string _accentColor = "#FFFFB43A";
    private string _secondaryColor = "#403F4651";
    private double _opacity = 1.0;
    private bool _showLabel = true;
    private bool _showValue = true;
    private GraphStyle _graphStyle = GraphStyle.Line;
    private ShapeStyle _shapeStyle = ShapeStyle.Rectangle;
    private double _minimum = 0;
    private double _maximum = 100;
    private double _lineThickness = 3;
    private double _gaugeThickness = 8;
    private int _segmentCount = 20;
    private int _historySeconds = 60;
    private bool _autoScale;
    private bool _showGrid;
    private Guid? _assetId;
    private MediaFit _mediaFit = MediaFit.Fill;
    private bool _loop = true;
    private double _playbackSpeed = 1.0;
    private int _targetFps;
    private bool _autoOptimize = true;
    private double _cropX;
    private double _cropY;
    private double _cropZoom = 1.0;
    private double _cornerRadius = 8;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public WidgetType Type { get => _type; set => SetProperty(ref _type, value); }
    public double X { get => _x; set => SetProperty(ref _x, Math.Max(0, value)); }
    public double Y { get => _y; set => SetProperty(ref _y, Math.Max(0, value)); }
    public double Width { get => _width; set => SetProperty(ref _width, Math.Max(8, value)); }
    public double Height { get => _height; set => SetProperty(ref _height, Math.Max(8, value)); }
    public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }
    public int ZIndex { get => _zIndex; set => SetProperty(ref _zIndex, value); }
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
    public bool IsLocked { get => _isLocked; set => SetProperty(ref _isLocked, value); }

    [JsonIgnore]
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public string DataSource { get => _dataSource; set => SetProperty(ref _dataSource, value); }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public string ValueFormat { get => _valueFormat; set => SetProperty(ref _valueFormat, value); }
    public string Suffix { get => _suffix; set => SetProperty(ref _suffix, value); }
    public double SimulatedValue { get => _simulatedValue; set => SetProperty(ref _simulatedValue, value); }

    [JsonIgnore]
    public double? RuntimeValue { get => _runtimeValue; set => SetProperty(ref _runtimeValue, value); }

    [JsonIgnore]
    public string? RuntimeText { get => _runtimeText; set => SetProperty(ref _runtimeText, value); }

    [JsonIgnore]
    public double DisplayValue => RuntimeValue ?? SimulatedValue;

    [JsonIgnore]
    public List<double> RuntimeSeries { get; } = [];


    public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }
    public Guid? FontAssetId { get => _fontAssetId; set => SetProperty(ref _fontAssetId, value); }
    public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, Math.Max(6, value)); }
    public bool FontBold { get => _fontBold; set => SetProperty(ref _fontBold, value); }
    public bool FontItalic { get => _fontItalic; set => SetProperty(ref _fontItalic, value); }

    public string ForegroundColor { get => _foregroundColor; set => SetProperty(ref _foregroundColor, value); }
    public string BackgroundColor { get => _backgroundColor; set => SetProperty(ref _backgroundColor, value); }
    public string AccentColor { get => _accentColor; set => SetProperty(ref _accentColor, value); }
    public string SecondaryColor { get => _secondaryColor; set => SetProperty(ref _secondaryColor, value); }
    public double Opacity { get => _opacity; set => SetProperty(ref _opacity, Math.Clamp(value, 0, 1)); }

    public bool ShowLabel { get => _showLabel; set => SetProperty(ref _showLabel, value); }
    public bool ShowValue { get => _showValue; set => SetProperty(ref _showValue, value); }
    public GraphStyle GraphStyle { get => _graphStyle; set => SetProperty(ref _graphStyle, value); }
    public ShapeStyle ShapeStyle { get => _shapeStyle; set => SetProperty(ref _shapeStyle, value); }
    public double Minimum { get => _minimum; set => SetProperty(ref _minimum, value); }
    public double Maximum { get => _maximum; set => SetProperty(ref _maximum, value); }
    public double LineThickness { get => _lineThickness; set => SetProperty(ref _lineThickness, Math.Max(1, value)); }
    public double GaugeThickness { get => _gaugeThickness; set => SetProperty(ref _gaugeThickness, Math.Max(1, value)); }
    public int SegmentCount { get => _segmentCount; set => SetProperty(ref _segmentCount, Math.Clamp(value, 2, 100)); }
    public int HistorySeconds { get => _historySeconds; set => SetProperty(ref _historySeconds, Math.Clamp(value, 5, 3600)); }
    public bool AutoScale { get => _autoScale; set => SetProperty(ref _autoScale, value); }
    public bool ShowGrid { get => _showGrid; set => SetProperty(ref _showGrid, value); }

    public Guid? AssetId { get => _assetId; set => SetProperty(ref _assetId, value); }
    public MediaFit MediaFit { get => _mediaFit; set => SetProperty(ref _mediaFit, value); }
    public bool Loop { get => _loop; set => SetProperty(ref _loop, value); }
    public double PlaybackSpeed { get => _playbackSpeed; set => SetProperty(ref _playbackSpeed, Math.Clamp(value, 0.1, 4.0)); }
    public int TargetFps { get => _targetFps; set => SetProperty(ref _targetFps, Math.Clamp(value, 0, 60)); }
    public bool AutoOptimize { get => _autoOptimize; set => SetProperty(ref _autoOptimize, value); }
    public double CropX { get => _cropX; set => SetProperty(ref _cropX, value); }
    public double CropY { get => _cropY; set => SetProperty(ref _cropY, value); }
    public double CropZoom { get => _cropZoom; set => SetProperty(ref _cropZoom, Math.Clamp(value, 0.05, 20)); }
    public double CornerRadius { get => _cornerRadius; set => SetProperty(ref _cornerRadius, Math.Max(0, value)); }

    public WidgetModel Clone()
    {
        return new WidgetModel
        {
            Id = Guid.NewGuid(),
            Name = Name + " copy",
            Type = Type,
            X = X + 10,
            Y = Y + 10,
            Width = Width,
            Height = Height,
            Rotation = Rotation,
            ZIndex = ZIndex + 1,
            IsVisible = IsVisible,
            IsLocked = false,
            DataSource = DataSource,
            Label = Label,
            ValueFormat = ValueFormat,
            Suffix = Suffix,
            SimulatedValue = SimulatedValue,
            FontFamily = FontFamily,
            FontAssetId = FontAssetId,
            FontSize = FontSize,
            FontBold = FontBold,
            FontItalic = FontItalic,
            ForegroundColor = ForegroundColor,
            BackgroundColor = BackgroundColor,
            AccentColor = AccentColor,
            SecondaryColor = SecondaryColor,
            Opacity = Opacity,
            ShowLabel = ShowLabel,
            ShowValue = ShowValue,
            GraphStyle = GraphStyle,
            ShapeStyle = ShapeStyle,
            Minimum = Minimum,
            Maximum = Maximum,
            LineThickness = LineThickness,
            GaugeThickness = GaugeThickness,
            SegmentCount = SegmentCount,
            HistorySeconds = HistorySeconds,
            AutoScale = AutoScale,
            ShowGrid = ShowGrid,
            AssetId = AssetId,
            MediaFit = MediaFit,
            Loop = Loop,
            PlaybackSpeed = PlaybackSpeed,
            TargetFps = TargetFps,
            AutoOptimize = AutoOptimize,
            CropX = CropX,
            CropY = CropY,
            CropZoom = CropZoom,
            CornerRadius = CornerRadius
        };
    }
}
