namespace PCInfoScreenStudio.Models;

public sealed class ThemeWorkspace : IDisposable
{
    public ThemeWorkspace(ThemeDocument document, string rootDirectory)
    {
        Document = document;
        RootDirectory = rootDirectory;
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(AssetsDirectory);
    }

    public ThemeDocument Document { get; }
    public string RootDirectory { get; }
    public string AssetsDirectory => Path.Combine(RootDirectory, "assets");
    public string? FilePath { get; set; }
    public bool IsDirty { get; set; }

    public string GetAbsolutePath(ThemeAsset asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.LocalPath))
            return asset.LocalPath!;

        return Path.Combine(RootDirectory, asset.PackagePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
                Directory.Delete(RootDirectory, recursive: true);
        }
        catch
        {
            // A failed temp cleanup is harmless; Windows will eventually reclaim it.
        }
    }
}
