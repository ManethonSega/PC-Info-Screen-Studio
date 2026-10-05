using System.Windows;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio.ViewModels;

public sealed partial class MainViewModel
{
    private readonly WindowsStartupService _windowsStartup = new();
    private bool _changingWindowsStartup;
    private string _windowsStartupStatus = "Starts in the tray at sign-in. Windows asks once to authorize startup with sensor access.";
    public bool CanChangeWindowsStartup => !_changingWindowsStartup;
    public string WindowsStartupStatus
    {
        get => _windowsStartupStatus;
        private set => SetProperty(ref _windowsStartupStatus, value);
    }
    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set { if (value != _settings.StartWithWindows) _ = ChangeWindowsStartupAsync(value); }
    }

    public async Task InitializeWindowsStartupAsync()
    {
        if (!_settings.StartWithWindows)
        {
            WindowsStartupStatus = "Windows startup is off. Your workspace still reopens when you launch the app.";
            return;
        }
        if (await Task.Run(() => _windowsStartup.IsConfiguredForCurrentLocation()))
        {
            WindowsStartupStatus = "Enabled for this Windows account. Starts in the tray with the approved sensor permissions.";
            return;
        }
        await ChangeWindowsStartupAsync(true);
    }

    private async Task ChangeWindowsStartupAsync(bool enabled)
    {
        if (_changingWindowsStartup) return;
        _changingWindowsStartup = true;
        RaisePropertyChanged(nameof(CanChangeWindowsStartup));
        WindowsStartupStatus = "Configuring Windows startup...";
        try
        {
            if (await _windowsStartup.SetEnabledAsync(enabled))
            {
                _settings.StartWithWindows = enabled;
                WindowsStartupStatus = enabled
                    ? "Enabled for this Windows account. Starts in the tray with the approved sensor permissions."
                    : "Windows startup is off. Your workspace still reopens when you launch the app.";
            }
            else
            {
                // A canceled first-time setup must not advertise startup as successfully enabled.
                if (enabled && !_windowsStartup.IsConfiguredForCurrentLocation()) _settings.StartWithWindows = false;
                WindowsStartupStatus = "Windows approval was canceled. The startup task was not changed.";
            }
        }
        catch (Exception ex)
        {
            if (enabled && !_windowsStartup.IsConfiguredForCurrentLocation()) _settings.StartWithWindows = false;
            WindowsStartupStatus = "Could not configure startup: " + ex.Message;
        }
        finally
        {
            _changingWindowsStartup = false;
            RaisePropertyChanged(nameof(StartWithWindows));
            RaisePropertyChanged(nameof(CanChangeWindowsStartup));
        }
    }

    public async Task<bool> RestoreLastSessionAsync()
    {
        try
        {
            var saved = await _sessionService.LoadAsync();
            if (saved is null) return false;
            ReplaceWorkspace(saved.Workspace);
            _themeSessions.Restore(saved.State.Themes);
            Workspace.IsDirty = _themeSessions.HasUnsavedChanges;
            IsLiveMode = saved.State.IsLiveMode;
            ShowPage(saved.State.IsHardwarePage);
            RefreshThemes();
            SelectedTheme = Themes.FirstOrDefault(t => string.Equals(t.FilePath, Workspace.FilePath, StringComparison.OrdinalIgnoreCase)) ?? SelectedTheme;
            RefreshModeThemes();
            NotifyThemeState();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            DeviceStatus = "Could not restore the last session: " + ex.Message + ". Open a saved theme to continue.";
            return false;
        }
    }

    // Returns the IO task directly so session-ending can also wait without a dispatcher deadlock.
    public Task SaveLastSessionAsync()
    {
        var state = new SessionState
        {
            InfoThemePath = Workspace.FilePath,
            IsLiveMode = IsLiveMode,
            IsHardwarePage = IsHardwarePage,
            Themes = _themeSessions.Capture(Document.Mode, Document.PhotoFrame)
        };
        return _sessionService.SaveAsync(Workspace, state);
    }
}
