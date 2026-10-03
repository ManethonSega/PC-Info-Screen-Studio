using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class ThemePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ThemeWorkspace CreateNewWorkspace()
    {
        var root = CreateTempDirectory();
        var document = new ThemeDocument();
        return new ThemeWorkspace(document, root);
    }

    public async Task SaveAsync(ThemeWorkspace workspace, string filePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var tempFile = filePath + ".tmp";
        if (File.Exists(tempFile)) File.Delete(tempFile);

        await using (var file = File.Create(tempFile))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
        {
            var manifest = new ThemeManifest
            {
                Format = "PCInfoScreenStudio",
                FormatVersion = workspace.Document.FormatVersion,
                MinimumAppVersion = workspace.Document.MinimumAppVersion,
                ThemeName = workspace.Document.Name
            };

            await WriteJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken);
            await WriteJsonEntryAsync(archive, "theme.json", workspace.Document, cancellationToken);

            foreach (var asset in workspace.Document.Assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = workspace.GetAbsolutePath(asset);
                if (!File.Exists(source))
                    continue;

                var safeEntryName = NormalizePackagePath(asset.PackagePath);
                ValidateAssetExtension(asset, safeEntryName);
                var entry = archive.CreateEntry(safeEntryName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var sourceStream = File.OpenRead(source);
                await sourceStream.CopyToAsync(entryStream, cancellationToken);
            }
        }

        File.Move(tempFile, filePath, overwrite: true);
        workspace.FilePath = filePath;
        workspace.IsDirty = false;
    }

    public async Task<ThemeWorkspace> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var root = CreateTempDirectory();
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var themeEntry = archive.GetEntry("theme.json")
                ?? throw new InvalidDataException("This file does not contain theme.json.");

            ThemeDocument? document;
            await using (var stream = themeEntry.Open())
                document = await JsonSerializer.DeserializeAsync<ThemeDocument>(stream, JsonOptions, cancellationToken);

            if (document is null)
                throw new InvalidDataException("The theme document could not be read.");
            if (document.FormatVersion > ThemeDocument.CurrentFormatVersion)
                throw new InvalidDataException($"Theme format {document.FormatVersion} is newer than this app supports.");

            var indexedAssets = document.Assets.ToDictionary(
                a => NormalizePackagePath(a.PackagePath),
                StringComparer.OrdinalIgnoreCase);

            const long maxExpandedBytes = 1024L * 1024L * 1024L; // 1 GiB defensive ceiling
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName is "theme.json" or "manifest.json" || string.IsNullOrEmpty(entry.Name))
                    continue;

                var normalized = NormalizePackagePath(entry.FullName);
                if (!indexedAssets.TryGetValue(normalized, out var asset))
                    continue; // Ignore unreferenced files, including executables/scripts.

                ValidateAssetExtension(asset, normalized);
                expandedBytes += entry.Length;
                if (expandedBytes > maxExpandedBytes)
                    throw new InvalidDataException("Theme assets exceed the 1 GiB safety limit.");

                var destination = GetSafeExtractionPath(root, normalized);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            var workspace = new ThemeWorkspace(document, root) { FilePath = filePath, IsDirty = false };
            foreach (var asset in document.Assets)
            {
                var normalized = NormalizePackagePath(asset.PackagePath);
                ValidateAssetExtension(asset, normalized);
                var path = GetSafeExtractionPath(root, normalized);
                asset.LocalPath = File.Exists(path) ? path : null;
            }
            return workspace;
        }
        catch
        {
            try { Directory.Delete(root, true); } catch { }
            throw;
        }
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string name, T value, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "PCInfoScreenStudio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string NormalizePackagePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(p => p is ".." or "."))
            throw new InvalidDataException("Unsafe path in theme package.");
        return normalized;
    }

    private static string GetSafeExtractionPath(string root, string packagePath)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, packagePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Theme package attempted to write outside its workspace.");
        return fullPath;
    }

    private static void ValidateAssetExtension(ThemeAsset asset, string packagePath)
    {
        var ext = Path.GetExtension(packagePath).ToLowerInvariant();
        var allowed = asset.Kind switch
        {
            ThemeAssetKind.Image => ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp",
            ThemeAssetKind.Gif => ext == ".gif",
            ThemeAssetKind.Video => ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv",
            ThemeAssetKind.Font => ext is ".ttf" or ".otf",
            _ => false
        };

        if (!allowed)
            throw new InvalidDataException($"Asset '{asset.OriginalFileName}' has a file type that is not allowed for {asset.Kind}.");
    }

    private sealed class ThemeManifest
    {
        public string Format { get; set; } = "PCInfoScreenStudio";
        public int FormatVersion { get; set; }
        public string MinimumAppVersion { get; set; } = string.Empty;
        public string ThemeName { get; set; } = string.Empty;
    }
}
