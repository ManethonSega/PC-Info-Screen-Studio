using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

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
            var saved = new PhotoFrameSettings();
            ModeThemeService.ApplySettings(saved, current);
            _settings[previous] = saved;
        }
        if (next != ScreenMode.InfoScreen && _settings.TryGetValue(next, out var target))
            ModeThemeService.ApplySettings(current, target);
    }
    public ThemeSessionState Capture(ScreenMode activeMode, PhotoFrameSettings current)
    {
        if (activeMode != ScreenMode.InfoScreen)
        {
            var copy = new PhotoFrameSettings();
            ModeThemeService.ApplySettings(copy, current);
            _settings[activeMode] = copy;
        }
        return new ThemeSessionState
        {
            Paths = new Dictionary<ScreenMode, string>(_paths),
            DirtyModes = _dirty.ToList(),
            PhotoSettings = new Dictionary<ScreenMode, PhotoFrameSettings>(_settings)
        };
    }
    public void Restore(ThemeSessionState state)
    {
        Reset();
        foreach (var item in state.Paths) _paths[item.Key] = item.Value;
        foreach (var mode in state.DirtyModes) _dirty.Add(mode);
        foreach (var item in state.PhotoSettings) _settings[item.Key] = item.Value;
    }
    public void Reset() { _paths.Clear(); _dirty.Clear(); _settings.Clear(); }
}

public sealed class ThemeSessionState
{
    public Dictionary<ScreenMode, string> Paths { get; set; } = [];
    public List<ScreenMode> DirtyModes { get; set; } = [];
    public Dictionary<ScreenMode, PhotoFrameSettings> PhotoSettings { get; set; } = [];
}
