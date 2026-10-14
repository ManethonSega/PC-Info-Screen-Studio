using System.IO.Compression;
using System.Text.Json;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Services;

public sealed class SessionState
{
    public int Version { get; set; } = 1;
    public string? InfoThemePath { get; set; }
    public bool IsLiveMode { get; set; }
    public bool IsHardwarePage { get; set; }
    public ThemeSessionState Themes { get; set; } = new();
}

public sealed record RestoredSession(ThemeWorkspace Workspace, SessionState State);

/// <summary>Private resume storage, separate from reusable theme files. Photos remain linked to the PC.</summary>
public sealed class SessionStateService
{
    private readonly string _path;
    public string RecoveryNotice { get; private set; } = string.Empty;
    public SessionStateService(string? directory = null)
        => _path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCInfoScreenStudio", "Session"), "last-session.t3theme");

    public Task SaveAsync(ThemeWorkspace workspace, SessionState state, CancellationToken cancellationToken = default)
    {
        var retainedFiles = workspace.RetainFiles();
        try
        {
            // Snapshot on the editor thread; zip/image IO runs without blocking that thread.
            var document = new DocumentHistoryService().Restore(new DocumentHistoryService().Capture(workspace.Document));
            foreach (var asset in document.Assets)
            {
                var original = workspace.Document.Assets.First(a => a.Id == asset.Id);
                asset.LocalPath = workspace.GetAbsolutePath(original);
            }
            var metadata = JsonSerializer.Serialize(state);
            var frozenState = JsonSerializer.Deserialize<SessionState>(metadata)!;
            ValidateState(frozenState);
            var photoAssetIds = GetPhotoAssetIds(frozenState);
            var gate = RecoveryFileStore.Gate(_path);
            var entered = gate.WaitAsync(cancellationToken);
            return Task.Run(async () => {
                using var retained = retainedFiles;
                await entered.ConfigureAwait(false);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    var pending = RecoveryFileStore.Pending(_path);
                    var root = Path.Combine(Path.GetTempPath(), "PCInfoScreenStudio", Guid.NewGuid().ToString("N"));
                    using var snapshot = new ThemeWorkspace(document, root);
                    try
                    {
                        await new ThemePackageService().SaveCopyAsync(snapshot, pending, cancellationToken, additionalAssetIds: photoAssetIds).ConfigureAwait(false);
                        using (var archive = ZipFile.Open(pending, ZipArchiveMode.Update))
                        using (var writer = new StreamWriter(archive.CreateEntry("session.json").Open()))
                            await writer.WriteAsync(metadata).ConfigureAwait(false);
                        using (var file = new FileStream(pending, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(flushToDisk: true);
                        await ValidateArchiveAsync(pending, cancellationToken).ConfigureAwait(false);
                        var primaryValid = await IsValidAsync(_path, cancellationToken).ConfigureAwait(false);
                        var backupValid = await IsValidAsync(_path + ".bak", cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        RecoveryFileStore.Commit(pending, _path, primaryValid, backupValid);
                    }
                    finally { if (File.Exists(pending)) File.Delete(pending); }
                }
                finally { gate.Release(); }
            });
        }
        catch { retainedFiles.Dispose(); throw; }
    }

    public async Task<RestoredSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var gate = RecoveryFileStore.Gate(_path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RecoveryNotice = string.Empty;
            if (!File.Exists(_path) && !File.Exists(_path + ".bak")) return null;
            cancellationToken.ThrowIfCancellationRequested();
            try { return await LoadCandidateAsync(_path, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsUnreadable(ex))
            {
                try
                {
                    var saved = await LoadCandidateAsync(_path + ".bak", cancellationToken).ConfigureAwait(false);
                    RecoveryNotice = "Your last good session was restored from backup. It may be older than your latest work. The unreadable file was kept for diagnosis.";
                    try { RecoveryFileStore.Restore(_path); }
                    catch { RecoveryNotice = "Your last session was loaded from backup. The original file could not be repaired; the backup is safe."; }
                    return saved;
                }
                catch (Exception backupError) when (IsUnreadable(backupError))
                {
                    RecoveryNotice = "The saved session and backup could not be read. Existing files were kept; open a saved theme or use the editor recovery copy.";
                    return null;
                }
            }
        }
        finally { gate.Release(); }
    }

    private static async Task<RestoredSession> LoadCandidateAsync(string path, CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(path, cancellationToken).ConfigureAwait(false);
        var workspace = await new ThemePackageService().LoadAsync(path, cancellationToken, additionalAssetIds: GetPhotoAssetIds(state)).ConfigureAwait(false);
        workspace.FilePath = state.InfoThemePath;
        workspace.IsDirty = state.Themes.DirtyModes.Count > 0;
        return new RestoredSession(workspace, state);
    }

    private static async Task<SessionState> ReadStateAsync(string path, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("session.json") ?? throw new InvalidDataException("Saved session metadata is missing.");
        if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Saved session metadata is too large.");
        await using var stream = entry.Open();
        using var data = new MemoryStream();
        var buffer = new byte[81920]; int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (data.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("Saved session metadata is too large.");
            data.Write(buffer, 0, count);
        }
        data.Position = 0;
        var state = await JsonSerializer.DeserializeAsync<SessionState>(data, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Saved session could not be read.");
        ValidateState(state);
        return state;
    }

    private static void ValidateState(SessionState state)
    {
        if (state.Version != 1 || state.Themes is null || state.Themes.Paths is null || state.Themes.DirtyModes is null ||
            state.Themes.PhotoSettings is null || state.Themes.PhotoPositions is null ||
            state.Themes.Paths.Keys.Concat(state.Themes.DirtyModes).Concat(state.Themes.PhotoSettings.Keys).Concat(state.Themes.PhotoPositions.Keys).Any(m => !Enum.IsDefined(m)) ||
            state.Themes.Paths.Values.Any(p => p is null) || state.Themes.PhotoSettings.Values.Any(p => p is null || p.Photos is null || p.Photos.Any(i => i is null)))
            throw new InvalidDataException("Saved session metadata is invalid or unsupported.");
    }

    private static async Task ValidateArchiveAsync(string path, CancellationToken ct)
    {
        var state = await ReadStateAsync(path, ct).ConfigureAwait(false);
        await new ThemePackageService().ValidateAsync(path, ct, GetPhotoAssetIds(state)).ConfigureAwait(false);
    }

    private static async Task<bool> IsValidAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return false;
        try { await ValidateArchiveAsync(path, ct).ConfigureAwait(false); return true; }
        catch (Exception ex) when (IsUnreadable(ex)) { return false; }
    }

    private static bool IsUnreadable(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException;
    private static Guid[] GetPhotoAssetIds(SessionState state) => state.Themes.PhotoSettings.Values
        .SelectMany(settings => settings.Photos).Where(photo => photo.AssetId.HasValue).Select(photo => photo.AssetId!.Value).Distinct().ToArray();
}
