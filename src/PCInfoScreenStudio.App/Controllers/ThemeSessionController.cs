using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio.Controllers;

/// <summary>Tracks each mode's loaded file and unsaved edits independently.</summary>
public sealed class ThemeSessionController
{
    private readonly Dictionary<ScreenMode, string> _paths = [];
    private readonly HashSet<ScreenMode> _dirty = [];
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
    public void Reset() { _paths.Clear(); _dirty.Clear(); }
}
