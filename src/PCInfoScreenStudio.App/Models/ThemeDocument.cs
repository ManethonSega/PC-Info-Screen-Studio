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

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string MinimumAppVersion { get; set; } = "0.1.0";

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

    public ObservableCollection<WidgetModel> Widgets { get; set; } = [];
    public ObservableCollection<ThemeAsset> Assets { get; set; } = [];
}
