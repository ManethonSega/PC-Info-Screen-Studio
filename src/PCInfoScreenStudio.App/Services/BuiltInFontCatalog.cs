namespace PCInfoScreenStudio.Services;

public static class BuiltInFontCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Files =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bungee"] = "Bungee-Regular.ttf",
            ["Fredoka"] = "Fredoka-Variable.ttf",
            ["Monoton"] = "Monoton-Regular.ttf",
            ["Orbitron"] = "Orbitron-Variable.ttf"
        };

    public static IReadOnlyList<string> Names { get; } =
        ["Bungee", "Fredoka", "Monoton", "Orbitron"];

    public static bool TryGetPath(string? fontName, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(fontName) || !Files.TryGetValue(fontName, out var file))
            return false;

        var candidate = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", file);
        if (!File.Exists(candidate))
            return false;

        path = candidate;
        return true;
    }
}
