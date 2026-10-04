using System.Text.Json;
using System.Text.Json.Serialization;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class PhotoAlbumPresetService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Save(ThemeWorkspace workspace, string presetPath)
    {
        var settings = CloneSettings(workspace.Document.PhotoFrame);
        settings.Photos.Clear();
        foreach (var sourceItem in workspace.Document.PhotoFrame.Photos)
        {
            var source = ResolvePath(workspace, sourceItem);
            if (source is null || !File.Exists(source)) continue;
            settings.Photos.Add(CloneItem(sourceItem, Path.GetFullPath(source)));
        }

        var preset = new PhotoAlbumPreset
        {
            Name = Path.GetFileNameWithoutExtension(presetPath),
            Settings = settings
        };
        File.WriteAllText(presetPath, JsonSerializer.Serialize(preset, Options));
    }

    public PhotoFrameSettings Load(string presetPath)
    {
        var preset = JsonSerializer.Deserialize<PhotoAlbumPreset>(File.ReadAllText(presetPath), Options)
            ?? throw new InvalidDataException("The album preset could not be read.");
        foreach (var photo in preset.Settings.Photos)
        {
            photo.AssetId = null;
            if (!Path.IsPathRooted(photo.SourcePath))
                photo.SourcePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(presetPath)!, photo.SourcePath));
        }
        return preset.Settings;
    }

    private static string? ResolvePath(ThemeWorkspace workspace, PhotoFrameItem item)
    {
        if (item.AssetId is Guid assetId)
        {
            var asset = workspace.Document.Assets.FirstOrDefault(a => a.Id == assetId);
            return asset is null ? null : workspace.GetAbsolutePath(asset);
        }
        return item.SourcePath;
    }

    private static PhotoFrameSettings CloneSettings(PhotoFrameSettings source)
        => JsonSerializer.Deserialize<PhotoFrameSettings>(JsonSerializer.Serialize(source, Options), Options) ?? new PhotoFrameSettings();

    private static PhotoFrameItem CloneItem(PhotoFrameItem source, string path)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            SourcePath = path,
            DateTaken = source.DateTaken,
            Location = source.Location
        };
}
