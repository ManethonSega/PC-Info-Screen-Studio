using System.Text.Json;
using System.Text.Json.Serialization;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed record ModeThemeLibraryItem(string DisplayName, string FilePath);

public sealed class ModeThemePreset
{
    public int FormatVersion { get; set; }
    public string Name { get; set; } = "Mode theme";
    public ScreenMode Mode { get; set; }
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
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
            FormatVersion = 2,
            CanvasWidth = document.CanvasWidth,
            CanvasHeight = document.CanvasHeight,
            Name = name,
            Mode = mode,
            PhotoFrame = settings,
            HybridWidgets = mode == ScreenMode.Hybrid
                ? document.HybridWidgets.Select(Clone).ToList()
                : []
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var gate = RecoveryFileStore.Gate(path);
        gate.Wait();
        var pending = RecoveryFileStore.Pending(path);
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(preset, Options));
            if (JsonSerializer.Deserialize<ModeThemePreset>(File.ReadAllText(pending), Options) is null)
                throw new InvalidDataException("The settings theme could not be validated.");
            File.Move(pending, path, overwrite: true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); gate.Release(); }
        return path;
    }

    public ModeThemePreset Load(string path)
    {
        var preset = JsonSerializer.Deserialize<ModeThemePreset>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The mode theme could not be read.");
        if (preset.FormatVersion == 0)
        {
            // Unversioned themes were saved with Duplicate's offset, suffix and layer increment.
            // Remove the save-time mutation once; Apply now preserves these values exactly.
            foreach (var widget in preset.HybridWidgets)
            {
                widget.X -= 10;
                widget.Y -= 10;
                widget.ZIndex -= 1;
                if (widget.Name.EndsWith(" copy", StringComparison.Ordinal))
                    widget.Name = widget.Name[..^5];
            }
            preset.FormatVersion = 1;
        }
        return preset;
    }

    public static void Apply(ThemeDocument document, ModeThemePreset preset) => ApplyCore(document, preset, null);

    public static void ApplyForDisplay(ThemeDocument document, ModeThemePreset preset, DisplayCanvasSize size)
        => ApplyCore(document, preset, size);

    private static void ApplyCore(ThemeDocument document, ModeThemePreset preset, DisplayCanvasSize? size)
    {
        if (document.Mode == ScreenMode.InfoScreen || preset.Mode != document.Mode)
            throw new InvalidDataException("This settings theme belongs to a different screen mode.");
        var validSize = CanvasSizing.IsSupported(preset.CanvasWidth, preset.CanvasHeight);
        var width = validSize ? preset.CanvasWidth : document.CanvasWidth;
        var height = validSize ? preset.CanvasHeight : document.CanvasHeight;
        var sourceWidth = width;
        var sourceHeight = height;
        if (size is { } selected)
        {
            var landscape = width > height;
            var longer = selected == DisplayCanvasSize.Turing52 ? 1280 : 480;
            var shorter = selected == DisplayCanvasSize.Turing52 ? 720 : 320;
            width = landscape ? longer : shorter;
            height = landscape ? shorter : longer;
        }
        // Scale the existing workspace straight to its final size, never through
        // the preset's unrelated resolution (which compounds font scaling).
        CanvasSizing.ResizeTo(document, width, height);
        var scaleX = width / (double)sourceWidth;
        var scaleY = height / (double)sourceHeight;
        ApplySettings(document.PhotoFrame, preset.PhotoFrame);
        document.PhotoFrame.CaptionFontSize *= Math.Min(scaleX, scaleY);
        if (document.Mode == ScreenMode.Hybrid)
        {
            document.HybridWidgets.Clear();
            foreach (var widget in preset.HybridWidgets)
            {
                var copy = Clone(widget);
                copy.X *= scaleX; copy.Y *= scaleY;
                copy.Width *= scaleX; copy.Height *= scaleY;
                copy.FontSize *= Math.Min(scaleX, scaleY);
                document.HybridWidgets.Add(copy);
            }
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
        target.CaptionFontFamily = source.CaptionFontFamily;
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

    // Saving/loading is an exact copy, unlike Duplicate, which offsets and renames widgets.
    private static WidgetModel Clone(WidgetModel source)
        => JsonSerializer.Deserialize<WidgetModel>(JsonSerializer.Serialize(source, Options), Options)
           ?? throw new InvalidDataException("The overlay could not be copied.");

    private static string SanitizeName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        var result = value.Trim();
        return string.IsNullOrWhiteSpace(result) ? "Mode theme" : result;
    }
}
