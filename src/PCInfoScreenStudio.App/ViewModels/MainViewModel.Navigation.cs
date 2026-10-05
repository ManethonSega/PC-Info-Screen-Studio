using System.Collections;
using System.Windows;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ThemeSessionController _themeSessions = new();
    private ScreenMode _lastThemeMode = ScreenMode.InfoScreen;
    private bool _isHardwarePage;
    private bool _isDisposed;
    private Task<IReadOnlyDictionary<string, MetricValue>>? _hardwareInventoryTask;
    public HardwareDashboardViewModel HardwareDashboard { get; } = new();
    public bool IsEditorPage => !_isHardwarePage;
    public bool IsHardwarePage => _isHardwarePage;
    public bool IsCanvasActive => IsEditorActive && IsEditorPage;
    private bool IsHardwarePageVisible => IsEditorActive && IsHardwarePage;
    public Visibility EditorPageVisibility => IsEditorPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HardwarePageVisibility => IsHardwarePage ? Visibility.Visible : Visibility.Collapsed;
    public RelayCommand ShowEditorPageCommand { get; private set; } = null!;
    public RelayCommand ShowHardwarePageCommand { get; private set; } = null!;

    public void ShowPage(bool hardware)
    {
        if (_isHardwarePage == hardware) return;
        _isHardwarePage = hardware;
        RaisePropertyChanged(nameof(IsEditorPage));
        RaisePropertyChanged(nameof(IsHardwarePage));
        RaisePropertyChanged(nameof(IsCanvasActive));
        RaisePropertyChanged(nameof(EditorPageVisibility));
        RaisePropertyChanged(nameof(HardwarePageVisibility));
        RaiseHistoryCommandStates();
        RaiseCommandStates();
        NewCommand.RaiseCanExecuteChanged(); OpenCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged(); SaveAsCommand.RaiseCanExecuteChanged();
        ToggleEditorModeCommand.RaiseCanExecuteChanged();
        EditorActivityChanged?.Invoke(this, EventArgs.Empty);
        if (hardware) _ = RefreshHardwarePageAsync();
        else ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshHardwarePageAsync()
    {
        _hardwareInventoryTask ??= Task.Run(HardwareInventoryService.Read);
        var inventory = await _hardwareInventoryTask;
        if (_isDisposed || !IsHardwarePageVisible) return;
        HardwareDashboard.SetInventory(inventory);
        await RefreshRuntimeDataAsync();
    }

    public IEnumerable ActiveThemes => Document.Mode == ScreenMode.InfoScreen ? Themes : ModeThemes;
    public object? ActiveThemeSelection
    {
        get => Document.Mode == ScreenMode.InfoScreen ? SelectedTheme : SelectedModeTheme;
        set
        {
            if (Document.Mode == ScreenMode.InfoScreen) SelectedTheme = value as ThemeLibraryItem;
            else SelectedModeTheme = value as ModeThemeLibraryItem;
        }
    }
    private string? ActiveThemePath => Document.Mode == ScreenMode.InfoScreen ? Workspace.FilePath : _themeSessions.PathFor(Document.Mode);
    public string CurrentThemeName => Document.Mode == ScreenMode.InfoScreen ? Document.Name
        : ActiveThemePath is string path ? Path.GetFileNameWithoutExtension(path) : "Untitled settings";
    public string ThemeSummary => Document.Mode == ScreenMode.InfoScreen ? "Layout, widgets and media · .t3theme" : ModeThemeSummary;
    private bool CanManageActiveTheme => IsEditorPage && (Document.Mode == ScreenMode.InfoScreen ? SelectedTheme is { IsBuiltIn: false } : SelectedModeTheme is not null);

    private void NotifyThemeState()
    {
        RaisePropertyChanged(nameof(ActiveThemes));
        RaisePropertyChanged(nameof(ActiveThemeSelection));
        RaisePropertyChanged(nameof(CurrentThemeName));
        RaisePropertyChanged(nameof(ThemeSummary));
        RaisePropertyChanged(nameof(IsDirty));
        RaisePropertyChanged(nameof(HasUnsavedChanges));
        RaisePropertyChanged(nameof(WindowTitle));
        LoadThemeCommand?.RaiseCanExecuteChanged();
        DuplicateThemeCommand?.RaiseCanExecuteChanged();
        RenameThemeCommand?.RaiseCanExecuteChanged();
        DeleteThemeCommand?.RaiseCanExecuteChanged();
    }

    private void SetActiveThemeLoaded(string? path)
    {
        _themeSessions.Loaded(Document.Mode, path);
        Workspace.IsDirty = _themeSessions.HasUnsavedChanges;
        NotifyThemeState();
        if (!HasUnsavedChanges) DeleteRecoveryFile();
    }

    private void NewModeTheme()
    {
        if (!ConfirmDiscardIfNeeded()) return;
        _suppressDirty = true;
        try
        {
            ApplyGlobalPhotoSettings(new PhotoFrameSettings());
            if (Document.Mode == ScreenMode.Hybrid)
            { Document.HybridWidgets.Clear(); SelectedWidget = null; }
        }
        finally { _suppressDirty = false; }
        SetActiveThemeLoaded(null);
        CaptureHistoryNow();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
    }

    public void LoadModeThemeFile(string path)
    {
        try
        {
            var preset = _modeThemeService.Load(path);
            if (preset.Mode != Document.Mode || Document.Mode == ScreenMode.InfoScreen)
                throw new InvalidDataException("Switch to the matching screen mode before opening this settings theme.");
            if (!ConfirmDiscardIfNeeded()) return;
            string? folderWarning;
            _suppressDirty = true;
            try
            {
                ModeThemeService.Apply(Document, preset);
                folderWarning = _photos.RestoreThemeFolder();
                if (Document.Mode == ScreenMode.Hybrid)
                    SelectedWidget = Document.HybridWidgets.OrderBy(widget => widget.ZIndex).FirstOrDefault();
            }
            finally { _suppressDirty = false; }
            SetActiveThemeLoaded(path);
            RefreshModeThemes();
            SelectedModeTheme = ModeThemes.FirstOrDefault(t => t.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            CaptureHistoryNow();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            if (LivePreview) RequestLiveFrame?.Invoke(this, EventArgs.Empty);
            DeviceStatus = folderWarning is null ? $"{CurrentThemeName} loaded" : $"{CurrentThemeName} loaded. {folderWarning}";
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "Could not open settings theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SaveModeThemeFile(bool saveAs)
    {
        var path = saveAs || ActiveThemePath is null ? _dialogs.SaveModeTheme(Document.Mode, CurrentThemeName) : ActiveThemePath;
        if (path is null) return;
        try
        {
            _modeThemeService.SaveToPath(Document, Document.Mode, path);
            SetActiveThemeLoaded(path);
            RefreshModeThemes();
            SelectedModeTheme = ModeThemes.FirstOrDefault(t => t.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            DeviceStatus = $"{CurrentThemeName} saved";
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "Could not save settings theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ManageModeTheme(bool rename)
    {
        if (SelectedModeTheme is not { } theme) return;
        var name = rename ? Controls.TextPromptDialog.Show(Application.Current.MainWindow, "Rename settings theme", "Theme name", theme.DisplayName) : theme.DisplayName + " copy";
        if (string.IsNullOrWhiteSpace(name)) return;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var destination = Path.Combine(Path.GetDirectoryName(theme.FilePath)!, name.Trim() + ModeThemeService.ExtensionFor(Document.Mode));
        if (destination.Equals(theme.FilePath, StringComparison.OrdinalIgnoreCase)) return;
        if (!rename)
        {
            var index = 2;
            while (File.Exists(destination)) destination = Path.Combine(Path.GetDirectoryName(theme.FilePath)!, name.Trim() + " " + index++ + ModeThemeService.ExtensionFor(Document.Mode));
        }
        try
        {
            if (rename) { File.Move(theme.FilePath, destination); _themeSessions.MovePath(theme.FilePath, destination); }
            else File.Copy(theme.FilePath, destination);
            RefreshModeThemes();
            SelectedModeTheme = ModeThemes.FirstOrDefault(t => t.FilePath.Equals(destination, StringComparison.OrdinalIgnoreCase));
            NotifyThemeState();
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "Could not update settings theme", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
