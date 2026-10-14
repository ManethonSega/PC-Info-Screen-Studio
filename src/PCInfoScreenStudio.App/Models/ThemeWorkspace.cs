namespace PCInfoScreenStudio.Models;

public sealed class ThemeWorkspace : IDisposable
{
    private readonly object _lifetime = new();
    private int _readers;
    private bool _disposeRequested;
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

    public IDisposable RetainFiles()
    {
        lock (_lifetime)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _readers++;
            return new FileLease(this);
        }
    }

    private void ReleaseFiles()
    {
        lock (_lifetime)
        {
            _readers--;
            if (_disposeRequested && _readers == 0) DeleteFiles();
        }
    }

    public void Dispose()
    {
        lock (_lifetime)
        {
            _disposeRequested = true;
            if (_readers == 0) DeleteFiles();
        }
    }

    private void DeleteFiles()
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

    private sealed class FileLease(ThemeWorkspace workspace) : IDisposable
    {
        private ThemeWorkspace? _owner = workspace;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseFiles();
    }
}
