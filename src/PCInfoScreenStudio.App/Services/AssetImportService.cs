using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class AssetImportService
{
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
