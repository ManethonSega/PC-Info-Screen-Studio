using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class AssetImportService
{
    public const long MaximumVideoBytes = 512L * 1024 * 1024;
    public async Task<ThemeAsset> PrepareAssetAsync(ThemeWorkspace workspace, string sourceFile, ThemeAssetKind kind,
        CancellationToken ct = default, IProgress<string>? progress = null)
    {
        using var retained = workspace.RetainFiles();
        var packagePath = $"assets/{kind.ToString().ToLowerInvariant()}/{Guid.NewGuid():N}{Path.GetExtension(sourceFile).ToLowerInvariant()}";
        var destination = Path.Combine(workspace.RootDirectory, packagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            await using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            if (kind == ThemeAssetKind.Video && source.Length > MaximumVideoBytes) throw new InvalidDataException("Use a video smaller than 512 MiB.");
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920]; long copied = 0; int previous = -1, count;
            while ((count = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                copied += count;
                var percent = source.Length == 0 ? 100 : (int)Math.Min(100, copied * 100 / source.Length);
                if (percent != previous) { progress?.Report($"Importing {kind.ToString().ToLowerInvariant()} {percent}%..."); previous = percent; }
            }
            ct.ThrowIfCancellationRequested();
            return new ThemeAsset { Kind = kind, DisplayName = Path.GetFileNameWithoutExtension(sourceFile), OriginalFileName = Path.GetFileName(sourceFile), PackagePath = packagePath, LocalPath = destination };
        }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
    }
    // Prepare off the UI thread without touching ObservableCollections.
    public async Task<ThemeAsset> PrepareVideoAsync(ThemeWorkspace workspace, string sourceFile, CancellationToken cancellationToken = default, bool linkOnly = false)
    {
        using var retained = workspace.RetainFiles();
        if (linkOnly) { cancellationToken.ThrowIfCancellationRequested(); return DescribeLinkedVideo(sourceFile); }
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

    public ThemeAsset DescribeLinkedVideo(string sourceFile)
    {
        var path = Path.GetFullPath(sourceFile);
        if (!File.Exists(path)) throw new FileNotFoundException("Video file not found.");
        if (new FileInfo(path).Length > MaximumVideoBytes) throw new InvalidDataException("Use a video smaller than 512 MiB.");
        return new ThemeAsset { Kind = ThemeAssetKind.Video, DisplayName = Path.GetFileNameWithoutExtension(path), OriginalFileName = Path.GetFileName(path), LocalPath = path };
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
