using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using System.Text.Json;

namespace PCInfoScreenStudio.Controllers;

/// <summary>Tracks each mode's loaded file and unsaved edits independently.</summary>
public sealed class ThemeSessionController
{
    private readonly Dictionary<ScreenMode, string> _paths = [];
    private readonly HashSet<ScreenMode> _dirty = [];
    private readonly Dictionary<ScreenMode, PhotoFrameSettings> _settings = [];
    public bool HasUnsavedChanges => _dirty.Count > 0;
    public bool IsDirty(ScreenMode mode) => _dirty.Contains(mode);
    public string? PathFor(ScreenMode mode) => _paths.GetValueOrDefault(mode);
    public void MarkDirty(ScreenMode mode) => _dirty.Add(mode);
    public void Loaded(ScreenMode mode, string? path)
    {
        if (path is null) _paths.Remove(mode); else _paths[mode] = path;
        _dirty.Remove(mode);
    }
    public void MovePath(string oldPath, string? newPath)
    {
        foreach (var mode in _paths.Where(p => p.Value.Equals(oldPath, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray())
        {
            if (newPath is null) { _paths.Remove(mode); _dirty.Add(mode); }
            else _paths[mode] = newPath;
        }
    }
    public void SwitchSettings(ScreenMode previous, ScreenMode next, PhotoFrameSettings current)
    {
        if (previous != ScreenMode.InfoScreen)
        {
            _settings[previous] = Clone(current);
        }
        if (next == ScreenMode.InfoScreen) return;
        // Older sessions stored global options without playlists. Keep the
        // initial legacy list once; subsequent new modes start independently.
        var target = _settings.TryGetValue(next, out var savedTarget) ? savedTarget
            : previous == ScreenMode.InfoScreen && _settings.Count == 0 ? Clone(current) : new PhotoFrameSettings();
        var copy = Clone(target);
        ModeThemeService.ApplySettings(current, copy);
        current.Photos.Clear();
        foreach (var photo in copy.Photos) current.Photos.Add(photo);
        current.RuntimeCurrentIndex = copy.RuntimeCurrentIndex;
    }
    public ThemeSessionState Capture(ScreenMode activeMode, PhotoFrameSettings current)
    {
        if (activeMode != ScreenMode.InfoScreen)
        {
            _settings[activeMode] = Clone(current);
        }
        return new ThemeSessionState
        {
            Paths = new Dictionary<ScreenMode, string>(_paths),
            DirtyModes = _dirty.ToList(),
            PhotoSettings = _settings.ToDictionary(item => item.Key, item => Clone(item.Value)),
            PhotoPositions = _settings.ToDictionary(item => item.Key, item => item.Value.RuntimeCurrentIndex)
        };
    }
    public void Restore(ThemeSessionState state)
    {
        Reset();
        foreach (var item in state.Paths) _paths[item.Key] = item.Value;
        foreach (var mode in state.DirtyModes) _dirty.Add(mode);
        foreach (var item in state.PhotoSettings)
        {
            var copy = Clone(item.Value);
            copy.RuntimeCurrentIndex = state.PhotoPositions.GetValueOrDefault(item.Key);
            _settings[item.Key] = copy;
        }
    }
    public void Reset() { _paths.Clear(); _dirty.Clear(); _settings.Clear(); }

    private static PhotoFrameSettings Clone(PhotoFrameSettings source)
    {
        var copy = JsonSerializer.Deserialize<PhotoFrameSettings>(JsonSerializer.Serialize(source))!;
        copy.RuntimeCurrentIndex = source.RuntimeCurrentIndex;
        return copy;
    }
}

public sealed class ThemeSessionState
{
    public Dictionary<ScreenMode, string> Paths { get; set; } = [];
    public List<ScreenMode> DirtyModes { get; set; } = [];
    public Dictionary<ScreenMode, PhotoFrameSettings> PhotoSettings { get; set; } = [];
    public Dictionary<ScreenMode, int> PhotoPositions { get; set; } = [];
}
