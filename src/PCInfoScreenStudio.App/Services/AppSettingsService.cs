using System.Text.Json;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class AppSettingsService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCInfoScreenStudio",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
    public string WeatherCity { get; set; } = string.Empty;
    public DisplayProtocolProfile DisplayProtocol { get; set; } = DisplayProtocolProfile.Auto;
    public DisplayColorMode DisplayColorMode { get; set; } = DisplayColorMode.Auto;
    public bool CloseToTray { get; set; } = true;
}
