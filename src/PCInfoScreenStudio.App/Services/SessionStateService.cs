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
    public SessionStateService(string? directory = null)
        => _path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCInfoScreenStudio", "Session"), "last-session.t3theme");

    public Task SaveAsync(ThemeWorkspace workspace, SessionState state)
    {
        // Snapshot on the editor thread; zip/image IO runs without blocking that thread.
        var document = new DocumentHistoryService().Restore(new DocumentHistoryService().Capture(workspace.Document));
        foreach (var asset in document.Assets)
        {
            var original = workspace.Document.Assets.First(a => a.Id == asset.Id);
            asset.LocalPath = workspace.GetAbsolutePath(original);
        }
        var metadata = JsonSerializer.Serialize(state);
        return Task.Run(async () => {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var pending = _path + ".pending";
            var root = Path.Combine(Path.GetTempPath(), "PCInfoScreenStudio", Guid.NewGuid().ToString("N"));
            using var snapshot = new ThemeWorkspace(document, root);
            try
            {
                await new ThemePackageService().SaveCopyAsync(snapshot, pending).ConfigureAwait(false);
                using (var archive = ZipFile.Open(pending, ZipArchiveMode.Update))
                using (var writer = new StreamWriter(archive.CreateEntry("session.json").Open()))
                    await writer.WriteAsync(metadata).ConfigureAwait(false);
                File.Move(pending, _path, overwrite: true);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        });
    }

    public async Task<RestoredSession?> LoadAsync()
    {
        if (!File.Exists(_path)) return null;
        SessionState state;
        using (var archive = ZipFile.OpenRead(_path))
        {
            var entry = archive.GetEntry("session.json") ?? throw new InvalidDataException("Saved session metadata is missing.");
            using var reader = new StreamReader(entry.Open());
            state = JsonSerializer.Deserialize<SessionState>(await reader.ReadToEndAsync().ConfigureAwait(false))
                ?? throw new InvalidDataException("Saved session could not be read.");
        }
        if (state.Version != 1) throw new InvalidDataException("Saved session format is not supported.");
        var workspace = await new ThemePackageService().LoadAsync(_path).ConfigureAwait(false);
        workspace.FilePath = state.InfoThemePath;
        workspace.IsDirty = state.Themes.DirtyModes.Count > 0;
        return new RestoredSession(workspace, state);
    }
}
