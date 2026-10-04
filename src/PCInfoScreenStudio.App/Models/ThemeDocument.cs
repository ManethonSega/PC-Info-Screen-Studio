using System.Collections.ObjectModel;

namespace PCInfoScreenStudio.Models;

public sealed class ThemeDocument : ObservableObject
{
    public const int CurrentFormatVersion = 1;

    private string _name = "Untitled theme";
    private string _author = string.Empty;
    private string _description = string.Empty;
    private ThemeOrientation _orientation = ThemeOrientation.Landscape;
    private int _canvasWidth = 480;
    private int _canvasHeight = 320;
    private DeviceRotation _deviceRotation = DeviceRotation.Degrees0;
    private string _backgroundColor = "#FF090B0F";
    private bool _editorGridVisible;
    private bool _snapToGrid;
    private double _gridSize = 10;
    private ScreenMode _mode = ScreenMode.InfoScreen;
    private PhotoFrameSettings _photoFrame = new();
    private readonly ObservableCollection<WidgetModel> _photoFrameEditorWidgets = [];

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string MinimumAppVersion { get; set; } = "0.10.0";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Author { get => _author; set => SetProperty(ref _author, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }

    public ThemeOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (!SetProperty(ref _orientation, value)) return;
            if (value == ThemeOrientation.Landscape)
            {
                CanvasWidth = 480;
                CanvasHeight = 320;
            }
            else
            {
                CanvasWidth = 320;
                CanvasHeight = 480;
            }
        }
    }

    public int CanvasWidth { get => _canvasWidth; set => SetProperty(ref _canvasWidth, value); }
    public int CanvasHeight { get => _canvasHeight; set => SetProperty(ref _canvasHeight, value); }
    public DeviceRotation DeviceRotation { get => _deviceRotation; set => SetProperty(ref _deviceRotation, value); }
    public string BackgroundColor { get => _backgroundColor; set => SetProperty(ref _backgroundColor, value); }
    public bool EditorGridVisible { get => _editorGridVisible; set => SetProperty(ref _editorGridVisible, value); }
    public bool SnapToGrid { get => _snapToGrid; set => SetProperty(ref _snapToGrid, value); }
    public double GridSize { get => _gridSize; set => SetProperty(ref _gridSize, Math.Clamp(value, 2, 100)); }
    public ScreenMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value)) return;
            RaisePropertyChanged(nameof(EditorWidgets));
        }
    }
    public PhotoFrameSettings PhotoFrame { get => _photoFrame; set => SetProperty(ref _photoFrame, value ?? new PhotoFrameSettings()); }

    [System.Text.Json.Serialization.JsonIgnore]
    public RuntimeScreenMode RuntimeMode { get; set; } = RuntimeScreenMode.InfoScreen;

    public ObservableCollection<WidgetModel> Widgets { get; set; } = [];
    public ObservableCollection<WidgetModel> HybridWidgets { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public ObservableCollection<WidgetModel> EditorWidgets => Mode switch
    {
        ScreenMode.Hybrid => HybridWidgets,
        ScreenMode.PhotoFrame => _photoFrameEditorWidgets,
        _ => Widgets
    };
    public ObservableCollection<ThemeAsset> Assets { get; set; } = [];
}
