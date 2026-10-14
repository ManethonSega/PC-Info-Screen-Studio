using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class ThemePackageService
{
    public const long MaximumExpandedAssetBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumDocumentBytes = 16 * 1024 * 1024;
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

    public Task SaveAsync(ThemeWorkspace workspace, string filePath, CancellationToken cancellationToken = default)
        => SaveCoreAsync(workspace, filePath, updateWorkspace: true, cancellationToken);

    public Task SaveCopyAsync(ThemeWorkspace workspace, string filePath, CancellationToken cancellationToken = default)
        => SaveCoreAsync(workspace, filePath, updateWorkspace: false, cancellationToken);

    private async Task SaveCoreAsync(
        ThemeWorkspace workspace,
        string filePath,
        bool updateWorkspace,
        CancellationToken cancellationToken)
    {
        // Capture on the caller's editor thread. IO uses only this detached snapshot;
        // live assets/files stay available to Undo even when omitted from the package.
        cancellationToken.ThrowIfCancellationRequested();
        var originalJson = JsonSerializer.Serialize(workspace.Document, JsonOptions);
        var document = JsonSerializer.Deserialize<ThemeDocument>(originalJson, JsonOptions)!;
        KeepReferencedAssets(document);
        var sources = workspace.Document.Assets.ToDictionary(a => a.Id, workspace.GetAbsolutePath);
        foreach (var asset in document.Assets) asset.LocalPath = sources[asset.Id];
        await Task.Run(() => WritePackageAsync(document, filePath, cancellationToken), cancellationToken);
        if (updateWorkspace)
        {
            workspace.FilePath = filePath;
            // An edit made while the frozen snapshot was being saved is still unsaved.
            workspace.IsDirty = JsonSerializer.Serialize(workspace.Document, JsonOptions) != originalJson;
        }
    }

    private static async Task WritePackageAsync(ThemeDocument document, string filePath, CancellationToken ct)
    {
        var plan = new List<(string Name, string Source, long Length, ThemeAssetKind Kind)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (var asset in document.Assets)
        {
            ct.ThrowIfCancellationRequested();
            var name = NormalizePackagePath(asset.PackagePath);
            GetSafeExtractionPath(Path.GetTempPath(), name); // Reject entries the loader could never safely extract.
            ValidateAssetExtension(asset, name);
            if (!paths.Add(name)) throw new InvalidDataException("Theme contains duplicate asset paths.");
            if (!File.Exists(asset.LocalPath)) continue; // Keep missing-asset metadata for its placeholder.
            var length = new FileInfo(asset.LocalPath!).Length;
            expandedBytes = AddToBudget(expandedBytes, length);
            plan.Add((name, asset.LocalPath!, length, asset.Kind));
        }
        if (System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(document, JsonOptions)) > MaximumDocumentBytes)
            throw new InvalidDataException("Theme document exceeds the 16 MiB metadata limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var pending = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = File.Create(pending))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var manifest = new ThemeManifest { Format = "PCInfoScreenStudio", FormatVersion = document.FormatVersion,
                    MinimumAppVersion = document.MinimumAppVersion, ThemeName = document.Name };
                await WriteJsonEntryAsync(archive, "manifest.json", manifest, ct).ConfigureAwait(false);
                await WriteJsonEntryAsync(archive, "theme.json", document, ct).ConfigureAwait(false);
                foreach (var item in plan)
                {
                    ct.ThrowIfCancellationRequested();
                    // MP4 is already compressed; avoid spending minutes deflating it again.
                    var entry = archive.CreateEntry(item.Name, item.Kind == ThemeAssetKind.Video ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    await using var target = entry.Open();
                    await using var source = File.OpenRead(item.Source);
                    var copied = await CopyBoundedAsync(source, target, item.Length, ct).ConfigureAwait(false);
                    if (copied != item.Length) throw new IOException("A theme asset changed while saving. Save again.");
                }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(pending, filePath, overwrite: true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public Task<ThemeWorkspace> LoadAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.Run(() => LoadCoreAsync(filePath, cancellationToken), cancellationToken);

    private static async Task<ThemeWorkspace> LoadCoreAsync(string filePath, CancellationToken cancellationToken)
    {
        var root = CreateTempDirectory();
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var themeEntry = archive.GetEntry("theme.json")
                ?? throw new InvalidDataException("This file does not contain theme.json.");

            if (themeEntry.Length > MaximumDocumentBytes) throw new InvalidDataException("Theme document exceeds the 16 MiB metadata limit.");
            ThemeDocument? document;
            await using (var stream = themeEntry.Open())
            using (var metadata = new MemoryStream())
            {
                await CopyBoundedAsync(stream, metadata, MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
                metadata.Position = 0;
                document = await JsonSerializer.DeserializeAsync<ThemeDocument>(metadata, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            if (document is null)
                throw new InvalidDataException("The theme document could not be read.");
            if (document.FormatVersion > ThemeDocument.CurrentFormatVersion)
                throw new InvalidDataException($"Theme format {document.FormatVersion} is newer than this app supports.");

            // Older packages can contain every previous import, including removed videos.
            // Recover the authored layout without extracting those abandoned payloads.
            KeepReferencedAssets(document);
            var indexedAssets = document.Assets.ToDictionary(
                a => NormalizePackagePath(a.PackagePath),
                StringComparer.OrdinalIgnoreCase);

            long expandedBytes = 0;
            var plan = new List<(ZipArchiveEntry Entry, string Destination)>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName is "theme.json" or "manifest.json" || string.IsNullOrEmpty(entry.Name))
                    continue;

                var normalized = NormalizePackagePath(entry.FullName);
                if (!indexedAssets.TryGetValue(normalized, out var asset))
                    continue; // Ignore unreferenced files, including executables/scripts.

                ValidateAssetExtension(asset, normalized);
                if (!paths.Add(normalized)) throw new InvalidDataException("Theme contains duplicate asset entries.");
                expandedBytes = AddToBudget(expandedBytes, entry.Length);
                var destination = GetSafeExtractionPath(root, normalized);
                plan.Add((entry, destination));
            }
            foreach (var item in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                await using var source = item.Entry.Open();
                await using var target = File.Create(item.Destination);
                var copied = await CopyBoundedAsync(source, target, item.Entry.Length, cancellationToken).ConfigureAwait(false);
                if (copied != item.Entry.Length) throw new InvalidDataException("Theme asset length does not match its ZIP metadata.");
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

    private static void KeepReferencedAssets(ThemeDocument document)
    {
        var ids = document.Widgets.Concat(document.HybridWidgets).SelectMany(w => new[] { w.AssetId, w.FontAssetId })
            .Concat(document.PhotoFrame.Photos.Select(p => p.AssetId)).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        // Imported fonts remain in the editor's font palette even if no widget uses them yet.
        document.Assets = new(document.Assets.Where(a => a.Kind == ThemeAssetKind.Font || ids.Contains(a.Id)));
    }

    private static long AddToBudget(long used, long length)
    {
        if (length < 0 || length > MaximumExpandedAssetBytes - used)
            throw new InvalidDataException("Referenced theme assets exceed the 4 GiB package limit. Remove unused media or use smaller videos, then save again.");
        return used + length;
    }

    private static async Task<long> CopyBoundedAsync(Stream source, Stream target, long maximum, CancellationToken ct)
    {
        var buffer = new byte[81920]; long copied = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (count > maximum - copied) throw new InvalidDataException("Theme data exceeds its declared or allowed size.");
            await target.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            copied += count;
        }
        return copied;
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
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
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
