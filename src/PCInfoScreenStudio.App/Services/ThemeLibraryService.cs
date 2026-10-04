using System.Windows.Media;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class ThemeLibraryItem : ObservableObject
{
    private ImageSource? _thumbnail;

    public ThemeLibraryItem(string displayName, string? filePath = null, string? builtInId = null)
    {
        DisplayName = displayName;
        FilePath = filePath;
        BuiltInId = builtInId;
    }

    public string DisplayName { get; }
    public string? FilePath { get; }
    public string? BuiltInId { get; }
    public bool IsBuiltIn => !string.IsNullOrWhiteSpace(BuiltInId);
    public ImageSource? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }
}

public sealed class ThemeLibraryService
{
    public string UserThemesDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "PC Info Screen Studio",
        "Themes");

    public IReadOnlyList<ThemeLibraryItem> GetThemes(string? currentThemePath = null)
    {
        Directory.CreateDirectory(UserThemesDirectory);

        var items = new List<ThemeLibraryItem>
        {
            new("Starter Amber", builtInId: "starter-amber"),
            new("Blank theme", builtInId: "blank")
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(UserThemesDirectory, "*.t3theme", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.CurrentCultureIgnoreCase))
        {
            var full = Path.GetFullPath(file);
            if (seen.Add(full))
                items.Add(new ThemeLibraryItem(Path.GetFileNameWithoutExtension(file), full));
        }

        if (!string.IsNullOrWhiteSpace(currentThemePath) && File.Exists(currentThemePath))
        {
            var full = Path.GetFullPath(currentThemePath);
            if (seen.Add(full))
                items.Add(new ThemeLibraryItem(Path.GetFileNameWithoutExtension(full), full));
        }

        return items;
    }

    public string Duplicate(string sourcePath)
    {
        Directory.CreateDirectory(UserThemesDirectory);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath) + " copy";
        var destination = UniquePath(baseName);
        File.Copy(sourcePath, destination);
        return destination;
    }

    public string Rename(string sourcePath, string requestedName)
    {
        var safeName = string.Join("_", requestedName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidDataException("Enter a valid theme name.");

        var destination = Path.Combine(Path.GetDirectoryName(sourcePath) ?? UserThemesDirectory, safeName + ".t3theme");
        if (File.Exists(destination))
            throw new IOException("A theme with that name already exists.");

        File.Move(sourcePath, destination);
        return destination;
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private string UniquePath(string baseName)
    {
        var path = Path.Combine(UserThemesDirectory, baseName + ".t3theme");
        var index = 2;
        while (File.Exists(path))
            path = Path.Combine(UserThemesDirectory, $"{baseName} {index++}.t3theme");
        return path;
    }
}
