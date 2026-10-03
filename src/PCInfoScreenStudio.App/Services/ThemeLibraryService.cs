namespace PCInfoScreenStudio.Services;

public sealed record ThemeLibraryItem(
    string DisplayName,
    string? FilePath = null,
    string? BuiltInId = null)
{
    public bool IsBuiltIn => !string.IsNullOrWhiteSpace(BuiltInId);
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
            new("Starter Amber", BuiltInId: "starter-amber"),
            new("Blank theme", BuiltInId: "blank")
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
}
