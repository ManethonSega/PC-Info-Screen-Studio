using System.Text.Json;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class AppSettingsService
{
    private readonly string _settingsPath;

    public AppSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCInfoScreenStudio",
            "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (settings.SettingsVersion < 2)
            {
                settings.SettingsVersion = 2;
                settings.AdvancedDisplayExpanded = true;
                settings.PositionPanelExpanded = true;
                settings.DataPanelExpanded = true;
                settings.TypographyPanelExpanded = true;
                settings.GraphPanelExpanded = true;
                settings.GaugePanelExpanded = true;
                settings.MediaPanelExpanded = true;
                settings.ShapePanelExpanded = true;
                settings.ColoursPanelExpanded = true;
                Save(settings);
            }
            if (settings.SettingsVersion < 3)
            {
                // Existing users already know the application; the assistant
                // is reserved for genuinely new installations.
                settings.SettingsVersion = 3;
                settings.FirstRunCompleted = true;
                Save(settings);
            }
            if (settings.SettingsVersion < 4)
            {
                settings.SettingsVersion = 4;
                settings.StartWithWindows = true;
                settings.AutoStartDisplay = true;
                Save(settings);
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsPath, json);
    }
}

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 4;
    public bool FirstRunCompleted { get; set; }
    public string WeatherCity { get; set; } = string.Empty;
    public DisplayProtocolProfile DisplayProtocol { get; set; } = DisplayProtocolProfile.Auto;
    public DisplayColorMode DisplayColorMode { get; set; } = DisplayColorMode.Auto;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool AutoStartDisplay { get; set; } = true;
    public string? LastDisplayPort { get; set; }
    public bool ShowAdvancedSensors { get; set; }
    public bool RequestAdministratorAtStartup { get; set; } = true;
    public bool AdvancedDisplayExpanded { get; set; } = true;
    public bool PositionPanelExpanded { get; set; } = true;
    public bool DataPanelExpanded { get; set; } = true;
    public bool TypographyPanelExpanded { get; set; } = true;
    public bool GraphPanelExpanded { get; set; } = true;
    public bool GaugePanelExpanded { get; set; } = true;
    public bool MediaPanelExpanded { get; set; } = true;
    public bool ShapePanelExpanded { get; set; } = true;
    public bool ColoursPanelExpanded { get; set; } = true;
    public double CanvasZoom { get; set; } = 1.0;
    public int LastPhotoIndex { get; set; }
    public bool PhotoFramePlaying { get; set; } = true;
}
