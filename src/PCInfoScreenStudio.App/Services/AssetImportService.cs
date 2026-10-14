using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class AssetImportService
{
    public const long MaximumVideoBytes = 512L * 1024 * 1024;
    // Prepare off the UI thread without touching ObservableCollections.
    public async Task<ThemeAsset> PrepareVideoAsync(ThemeWorkspace workspace, string sourceFile, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
        var packagePath = $"assets/video/{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(workspace.RootDirectory, packagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            using (var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                if (source.Length > MaximumVideoBytes) throw new InvalidDataException("Use a video smaller than 512 MiB.");
                using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ThemeAsset { Kind = ThemeAssetKind.Video, DisplayName = Path.GetFileNameWithoutExtension(sourceFile),
                OriginalFileName = Path.GetFileName(sourceFile), PackagePath = packagePath, LocalPath = destination };
        }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
    }

    public ThemeAsset Import(ThemeWorkspace workspace, string sourceFile, ThemeAssetKind kind)
    {
        if (!File.Exists(sourceFile))
            throw new FileNotFoundException("Asset not found.", sourceFile);

        var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
        var kindFolder = kind.ToString().ToLowerInvariant();
        var packagePath = $"assets/{kindFolder}/{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(workspace.RootDirectory, packagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourceFile, destination, overwrite: true);

        var asset = new ThemeAsset
        {
            Kind = kind,
            DisplayName = Path.GetFileNameWithoutExtension(sourceFile),
            OriginalFileName = Path.GetFileName(sourceFile),
            PackagePath = packagePath,
            LocalPath = destination
        };

        workspace.Document.Assets.Add(asset);
        workspace.IsDirty = true;
        return asset;
    }
}
