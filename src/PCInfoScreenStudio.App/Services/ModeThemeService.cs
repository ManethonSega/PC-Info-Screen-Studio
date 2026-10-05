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

    private readonly string _appRoot;
    public ModeThemeService(string? appRoot = null)
        => _appRoot = appRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PC Info Screen Studio");

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
        return SaveToPath(document, mode, Path.Combine(DirectoryFor(mode), name + ExtensionFor(mode)));
    }

    public string SaveToPath(ThemeDocument document, ScreenMode mode, string path)
    {
        if (mode == ScreenMode.InfoScreen || !Path.GetExtension(path).Equals(ExtensionFor(mode), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose the settings theme format for the active mode.");
        var name = Path.GetFileNameWithoutExtension(path);
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

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(preset, Options));
        return path;
    }

    public ModeThemePreset Load(string path)
        => JsonSerializer.Deserialize<ModeThemePreset>(File.ReadAllText(path), Options)
           ?? throw new InvalidDataException("The mode theme could not be read.");

    public static void Apply(ThemeDocument document, ModeThemePreset preset)
    {
        if (document.Mode == ScreenMode.InfoScreen || preset.Mode != document.Mode)
            throw new InvalidDataException("This settings theme belongs to a different screen mode.");
        ApplySettings(document.PhotoFrame, preset.PhotoFrame);
        if (document.Mode == ScreenMode.Hybrid)
        {
            document.HybridWidgets.Clear();
            foreach (var widget in preset.HybridWidgets) document.HybridWidgets.Add(widget.Clone());
        }
    }

    public static void ApplySettings(PhotoFrameSettings target, PhotoFrameSettings source)
    {
        target.DefaultDurationSeconds = source.DefaultDurationSeconds;
        target.TransitionDurationSeconds = source.TransitionDurationSeconds;
        target.Transition = source.Transition == PhotoTransition.KenBurns ? PhotoTransition.Crossfade : source.Transition;
        target.Fit = source.Fit;
        target.Loop = source.Loop;
        target.Shuffle = source.Shuffle;
        target.EmbedImportedPhotos = false;
        target.BackgroundMode = PhotoBackgroundMode.SolidColor;
        target.BackgroundColor = source.BackgroundColor;
        target.ShowCaptions = source.ShowCaptions;
        target.CaptionFontSize = source.CaptionFontSize;
        target.CaptionColor = source.CaptionColor;
        target.CaptionOutlineColor = source.CaptionOutlineColor;
        target.CaptionOutlineThickness = source.CaptionOutlineThickness;
        target.CaptionMode = source.CaptionMode;
        target.CustomCaption = source.CustomCaption;
        target.WatchFolderEnabled = source.WatchFolderEnabled;
        target.WatchedFolder = source.WatchedFolder;
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public string DirectoryFor(ScreenMode mode)
        => Path.Combine(_appRoot, mode == ScreenMode.Hybrid ? "Hybrid Themes" : "Photo Frame Themes");

    public static string ExtensionFor(ScreenMode mode)
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

