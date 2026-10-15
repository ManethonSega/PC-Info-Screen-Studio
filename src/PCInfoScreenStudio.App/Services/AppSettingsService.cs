using System.Text.Json;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class AppSettingsService
{
    private readonly string _settingsPath;
    public string RecoveryNotice { get; private set; } = string.Empty;

    public AppSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCInfoScreenStudio",
            "settings.json");
    }

    public AppSettings Load()
    {
        var gate = RecoveryFileStore.Gate(_settingsPath);
        gate.Wait();
        try
        {
            RecoveryNotice = string.Empty;
            var settings = ReadValid(_settingsPath);
            if (settings is null)
            {
                settings = ReadValid(_settingsPath + ".bak");
                if (settings is not null)
                {
                    RecoveryNotice = "Settings were restored from the last good backup. Any unreadable file was kept for diagnosis.";
                    try { RecoveryFileStore.Restore(_settingsPath); }
                    catch { RecoveryNotice = "Settings were loaded from backup. The original file could not be repaired; the backup is safe."; }
                }
                else
                {
                    if (File.Exists(_settingsPath) || File.Exists(_settingsPath + ".bak"))
                        RecoveryNotice = "Saved settings could not be read. Defaults are in use; existing files are kept for diagnosis.";
                    return new AppSettings();
                }
            }
            var oldVersion = settings.SettingsVersion;
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
            }
            if (settings.SettingsVersion < 3)
            {
                // Existing users already know the application; the assistant
                // is reserved for genuinely new installations.
                settings.SettingsVersion = 3;
                settings.FirstRunCompleted = true;
            }
            if (settings.SettingsVersion < 4)
            {
                settings.SettingsVersion = 4;
                settings.StartWithWindows = true;
                settings.AutoStartDisplay = true;
            }
            if (settings.SettingsVersion < 5)
            {
                settings.SettingsVersion = 5;
            }
            if (oldVersion != settings.SettingsVersion)
                try { SaveCore(settings); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                { RecoveryNotice = "Settings were loaded, but their upgrade could not be saved. Existing files were preserved."; }
            return settings;
        }
        finally { gate.Release(); }
    }

    public void Save(AppSettings settings)
    {
        var gate = RecoveryFileStore.Gate(_settingsPath);
        gate.Wait();
        try { SaveCore(settings); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { RecoveryNotice = "Settings could not be saved. Your changes are active for this session; stored settings and backups were preserved."; throw; }
        finally { gate.Release(); }
    }

    private void SaveCore(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var pending = RecoveryFileStore.Pending(_settingsPath);
        try
        {
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file))
            {
                writer.Write(json); writer.Flush(); file.Flush(flushToDisk: true);
            }
            if (ReadValid(pending) is null) throw new InvalidDataException("Settings could not be validated; existing settings and backup were preserved.");
            RecoveryFileStore.Commit(pending, _settingsPath, ReadValid(_settingsPath) is not null, ReadValid(_settingsPath + ".bak") is not null);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private static AppSettings? ReadValid(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
            var settings = json.RootElement.Deserialize<AppSettings>();
            if (settings is null || settings.SettingsVersion is < 0 or > 5 ||
                !Enum.IsDefined(settings.DisplayProtocol) || !Enum.IsDefined(settings.DisplayColorMode) ||
                (settings.PreferredCanvasSize.HasValue && !Enum.IsDefined(settings.PreferredCanvasSize.Value)) ||
                !double.IsFinite(settings.CanvasZoom) || settings.WeatherCity is null) return null;
            settings.CanvasZoom = Math.Clamp(settings.CanvasZoom, .1, 3);
            settings.WorkspaceLayout = (settings.WorkspaceLayout ?? new()).ValidatedCopy();
            settings.SerialBrightness = Math.Clamp(settings.SerialBrightness, 0, 100);
            settings.UsbBrightness = Math.Clamp(settings.UsbBrightness, 0, 100);
            settings.LastPhotoIndex = Math.Max(0, settings.LastPhotoIndex);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return null; }
    }
}

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 5;
    public bool FirstRunCompleted { get; set; }
    public string WeatherCity { get; set; } = string.Empty;
    public DisplayProtocolProfile DisplayProtocol { get; set; } = DisplayProtocolProfile.Auto;
    public DisplayColorMode DisplayColorMode { get; set; } = DisplayColorMode.Auto;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool AutoStartDisplay { get; set; } = true;
    public string? LastDisplayPort { get; set; }
    public string? LastDisplayHardwareId { get; set; }
    public string? LastDisplayFriendlyName { get; set; }
    public DisplayCanvasSize? PreferredCanvasSize { get; set; }
    public int SerialBrightness { get; set; } = 50;
    public int UsbBrightness { get; set; } = 50;
    public bool ClearDisplayOnExit { get; set; }
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
    public bool IsCanvasFit { get; set; } = true;
    public EditorWorkspaceLayout WorkspaceLayout { get; set; } = new();
    public int LastPhotoIndex { get; set; }
    public bool PhotoFramePlaying { get; set; } = true;
}
