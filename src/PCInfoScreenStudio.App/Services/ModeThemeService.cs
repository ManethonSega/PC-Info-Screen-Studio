using System.Text.Json;
using System.Text.Json.Serialization;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed record ModeThemeLibraryItem(string DisplayName, string FilePath);

public sealed class ModeThemePreset
{
    public string Name { get; set; } = "Mode theme";
    public ScreenMode Mode { get; set; }
    public PhotoFrameSettings PhotoFrame { get; set; } = new();
    public List<WidgetModel> HybridWidgets { get; set; } = [];
}

public sealed class ModeThemeService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _appRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "PC Info Screen Studio");

    public IReadOnlyList<ModeThemeLibraryItem> GetThemes(ScreenMode mode)
    {
        if (mode == ScreenMode.InfoScreen) return [];
        var directory = DirectoryFor(mode);
        Directory.CreateDirectory(directory);
        MigrateLegacyThemes(mode, directory);
        return Directory.EnumerateFiles(directory, "*" + ExtensionFor(mode), SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.CurrentCultureIgnoreCase)
            .Select(path => new ModeThemeLibraryItem(Path.GetFileNameWithoutExtension(path), path))
            .ToArray();
    }

    public string Save(ThemeDocument document, ScreenMode mode, string requestedName)
    {
        if (mode == ScreenMode.InfoScreen)
            throw new InvalidOperationException("Info Screen uses the main theme library.");

        var name = SanitizeName(requestedName);
        var settings = Clone(document.PhotoFrame);
        settings.Photos.Clear();
        settings.EmbedImportedPhotos = false;
        settings.BackgroundMode = PhotoBackgroundMode.SolidColor;
        if (settings.Transition == PhotoTransition.KenBurns)
            settings.Transition = PhotoTransition.Crossfade;

        var preset = new ModeThemePreset
        {
            Name = name,
            Mode = mode,
            PhotoFrame = settings,
            HybridWidgets = mode == ScreenMode.Hybrid
                ? document.HybridWidgets.Select(widget => widget.Clone()).ToList()
                : []
        };

        var directory = DirectoryFor(mode);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ExtensionFor(mode));
        File.WriteAllText(path, JsonSerializer.Serialize(preset, Options));
        return path;
    }

    public ModeThemePreset Load(string path)
        => JsonSerializer.Deserialize<ModeThemePreset>(File.ReadAllText(path), Options)
           ?? throw new InvalidDataException("The mode theme could not be read.");

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private string DirectoryFor(ScreenMode mode)
        => Path.Combine(_appRoot, mode == ScreenMode.Hybrid ? "Hybrid Themes" : "Photo Frame Themes");

    private static string ExtensionFor(ScreenMode mode)
        => mode == ScreenMode.Hybrid ? ".pchybrid" : ".pcphoto";

    private void MigrateLegacyThemes(ScreenMode mode, string destinationDirectory)
    {
        var legacyDirectory = Path.Combine(
            _appRoot,
            "Mode Themes",
            mode == ScreenMode.Hybrid ? "Hybrid" : "Photo Frame");
        if (!Directory.Exists(legacyDirectory)) return;

        foreach (var oldPath in Directory.EnumerateFiles(legacyDirectory, "*.pcmode", SearchOption.TopDirectoryOnly))
        {
            var newPath = Path.Combine(
                destinationDirectory,
                Path.GetFileNameWithoutExtension(oldPath) + ExtensionFor(mode));
            if (!File.Exists(newPath))
                File.Copy(oldPath, newPath);
        }
    }

    private static PhotoFrameSettings Clone(PhotoFrameSettings source)
        => JsonSerializer.Deserialize<PhotoFrameSettings>(JsonSerializer.Serialize(source, Options), Options)
           ?? new PhotoFrameSettings();

    private static string SanitizeName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        var result = value.Trim();
        return string.IsNullOrWhiteSpace(result) ? "Mode theme" : result;
    }
}
