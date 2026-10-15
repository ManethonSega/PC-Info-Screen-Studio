using System.ComponentModel;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;
using WinForms = System.Windows.Forms;

namespace PCInfoScreenStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly WinForms.NotifyIcon _trayIcon;
    private bool _exitRequested;
    private bool _disposed;
    private bool _savingSession;
    private bool _startupLoaded;
    private bool _sessionSaved;
    private Task? _sessionSaveTask;
    private HwndSource? _windowSource;
    private DispatcherTimer? _deviceChangeTimer;
    public bool StartInTray { get; set; }
    private Point _photoDragStart;
    private PhotoFrameItem? _draggedPhoto;
    private bool _restoringWorkspaceLayout;

    public MainWindow() : this(new MainViewModel()) { }

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        RestoreWorkspaceLayout(_viewModel.WorkspaceLayout);
        LeftDock.IsVisibleChanged += (_, _) => ConstrainWorkspacePanels();
        RightDock.IsVisibleChanged += (_, _) => ConstrainWorkspacePanels();

        var trayMenu = new WinForms.ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            BackColor = System.Drawing.Color.FromArgb(13, 28, 50),
            ForeColor = System.Drawing.Color.FromArgb(237, 242, 247),
            ShowItemToolTips = true
        };
        trayMenu.Items.Add("Show PC Info Screen Studio", null, (_, _) => RestoreFromTray());
        var modeMenu = new WinForms.ToolStripMenuItem("Mode");
        modeMenu.DropDownItems.Add("Info Screen", null, (_, _) => SetModeFromTray(ScreenMode.InfoScreen));
        modeMenu.DropDownItems.Add("Photo Frame", null, (_, _) => SetModeFromTray(ScreenMode.PhotoFrame));
        modeMenu.DropDownItems.Add("Hybrid", null, (_, _) => SetModeFromTray(ScreenMode.Hybrid));
        modeMenu.DropDown.Renderer = trayMenu.Renderer;
        trayMenu.Items.Add(modeMenu);
        var photoMenu = new WinForms.ToolStripMenuItem("Photo frame");
        photoMenu.DropDownItems.Add("Previous photo", null, (_, _) => Dispatcher.Invoke(_viewModel.PreviousPhoto));
        photoMenu.DropDownItems.Add("Play / Pause", null, (_, _) => Dispatcher.Invoke(_viewModel.TogglePhotoPlayback));
        photoMenu.DropDownItems.Add("Next photo", null, (_, _) => Dispatcher.Invoke(_viewModel.NextPhoto));
        photoMenu.DropDown.Renderer = trayMenu.Renderer;
        trayMenu.Items.Add(photoMenu);
        trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        DrawingIcon trayDrawingIcon;
        try
        {
            var executable = Environment.ProcessPath;
            trayDrawingIcon = !string.IsNullOrWhiteSpace(executable)
                ? DrawingIcon.ExtractAssociatedIcon(executable) ?? DrawingSystemIcons.Application
                : DrawingSystemIcons.Application;
        }
        catch
        {
            trayDrawingIcon = DrawingSystemIcons.Application;
        }

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = trayDrawingIcon,
            Text = "PC Info Screen Studio",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void SetModeFromTray(ScreenMode mode)
        => Dispatcher.Invoke(() => _viewModel.Document.Mode = mode);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        _deviceChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _deviceChangeTimer.Tick += OnDeviceChangeSettled;
        var area = WinForms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        // WPF sizes are in logical pixels; the monitor work area uses physical pixels.
        var availableWidth = Math.Max(1, area.Width / dpi.DpiScaleX - 24);
        var availableHeight = Math.Max(1, area.Height / dpi.DpiScaleY - 24);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmDeviceChange = 0x0219;
        const int DeviceNodesChanged = 0x0007;
        const int DeviceArrival = 0x8000;
        const int DeviceRemovalComplete = 0x8004;

        if (message == WmDeviceChange && wParam.ToInt64() is DeviceNodesChanged or DeviceArrival or DeviceRemovalComplete)
        {
            // Windows can send several notifications for one plug/unplug action.
            // Wait briefly for COM-port enumeration to settle, then refresh once.
            _deviceChangeTimer?.Stop();
            _deviceChangeTimer?.Start();
        }

        return IntPtr.Zero;
    }

    private void OnDeviceChangeSettled(object? sender, EventArgs e)
    {
        _deviceChangeTimer?.Stop();
        _viewModel.NotifyDisplayDevicesChanged();
    }

    private void OnSourceSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (SourceSearchResults is not null && DataContext is MainViewModel vm)
            SourceSearchResults.IsDropDownOpen = vm.CanSearchSources && !string.IsNullOrWhiteSpace(SourceSearchBox.Text);
    }

    private void OnBrightnessClick(object sender, RoutedEventArgs e)
        => BrightnessPopup.IsOpen = !BrightnessPopup.IsOpen;

    private void OnBrightnessOpened(object sender, EventArgs e)
        => BrightnessSlider.Focus();

    private void OnBrightnessClosed(object sender, EventArgs e)
        => CommitBrightnessEdit();

    private void CommitBrightnessEdit()
    {
        _viewModel.DisplayBrightness = (int)Math.Round(BrightnessSlider.Value);
        BrightnessSlider.GetBindingExpression(System.Windows.Controls.Primitives.RangeBase.ValueProperty)?.UpdateSource();
    }

    private void OnBrightnessKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CommitBrightnessEdit();
        BrightnessPopup.IsOpen = false;
        BrightnessButton.Focus();
        e.Handled = true;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = this,
            DataContext = _viewModel
        };
        window.ShowDialog();
    }

    private void RestoreWorkspaceLayout(EditorWorkspaceLayout layout)
    {
        _restoringWorkspaceLayout = true;
        try { LeftDock.RestoreLayout(layout.Left); RightDock.RestoreLayout(layout.Right); }
        finally { _restoringWorkspaceLayout = false; }
        ConstrainWorkspacePanels();
    }

    private void OnDockLayoutChanged(object? sender, EventArgs e)
    {
        if (_restoringWorkspaceLayout || LeftDock is null || RightDock is null) return;
        ConstrainWorkspacePanels();
        _viewModel.WorkspaceLayout = new EditorWorkspaceLayout { Left = LeftDock.CaptureLayout(), Right = RightDock.CaptureLayout() };
    }

    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => ConstrainWorkspacePanels();
    private void OnDockOpenPanelsChanged(object? sender, EventArgs e) => ConstrainWorkspacePanels();

    private void ConstrainWorkspacePanels()
    {
        if (EditorWorkspace is null || LeftDock is null || RightDock is null) return;
        var left = LeftDock.Visibility == Visibility.Visible;
        var right = RightDock.Visibility == Visibility.Visible;
        var leftOpen = left && LeftDock.HasOpenPanels;
        var rightOpen = right && RightDock.HasOpenPanels;
        // Adaptive widths preserve the user's preferred widths while reserving canvas space.
        var budget = Math.Max(240, EditorWorkspace.ActualWidth - 320 -
            (left ? Controls.WorkspaceDock.RailWidth : 0) - (right ? Controls.WorkspaceDock.RailWidth : 0) -
            (leftOpen ? Controls.WorkspaceDock.DividerWidth : 0) - (rightOpen ? Controls.WorkspaceDock.DividerWidth : 0));
        var desired = (leftOpen ? LeftDock.PreferredPanelWidth : 0) + (rightOpen ? RightDock.PreferredPanelWidth : 0);
        var ratio = desired > 0 ? Math.Min(1, budget / desired) : 1;
        LeftDock.ConstrainPanelWidth(leftOpen ? LeftDock.PreferredPanelWidth * ratio : 460);
        RightDock.ConstrainPanelWidth(rightOpen ? RightDock.PreferredPanelWidth * ratio : 460);
    }

    private void OnActualSizeClick(object sender, RoutedEventArgs e)
    { _viewModel.CanvasZoom = 1; _viewModel.IsCanvasFit = false; }

    private void OnResetWorkspaceClick(object sender, RoutedEventArgs e)
    {
        RestoreWorkspaceLayout(new EditorWorkspaceLayout
        {
            Left = new DockLayout { Width = 300, Panels = LeftDock.Panels.Select(p => new WorkspacePanelLayout
                { Id = p.PanelId, IsOpen = p.PanelId is "widgets" or "photos" or "layers" }).ToList() },
            Right = new DockLayout { Width = 330, Panels = RightDock.Panels.Select(p => new WorkspacePanelLayout
                { Id = p.PanelId, IsOpen = p.PanelId == "properties" }).ToList() }
        });
        _viewModel.IsCanvasFit = true;
        OnDockLayoutChanged(this, EventArgs.Empty);
    }

    private void OnPhotoPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _photoDragStart = e.GetPosition(null);
        _draggedPhoto = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as PhotoFrameItem;
    }

    private void OnPhotoPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedPhoto is null) return;
        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _photoDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _photoDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop((DependencyObject)sender, _draggedPhoto, DragDropEffects.Move);
    }

    private void OnPhotoPlaylistDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PhotoFrameItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPhotoPlaylistDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PhotoFrameItem)) is not PhotoFrameItem source) return;
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as PhotoFrameItem;
        if (target is not null) _viewModel.MovePhoto(source, target);
        _draggedPhoto = null;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_startupLoaded) return;
        _startupLoaded = true;
        _viewModel.SetEditorActive(false);
        if (StartInTray) Hide();
        try
        {
            var restored = await _viewModel.RestoreLastSessionAsync();
            if (!restored) await _viewModel.RecoverIfAvailableAsync();
        }
        finally
        {
            IsEnabled = true;
            _viewModel.SetEditorActive(!StartInTray);
        }
        _viewModel.StartAutoDisplayIfEnabled();
        await _viewModel.InitializeWindowsStartupAsync();
    }

    public void RestoreFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.SetEditorActive(true);
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.CloseToTray)
        {
            _viewModel.SetEditorActive(false);
            Hide();
        }
    }

    public void ExitFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            _exitRequested = true;
            Close();
        });
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!_viewModel.IsEditorPage || !_viewModel.CanChangeWorkspace) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!_viewModel.IsEditorPage) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        foreach (var path in files)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".t3theme" or ".pcphoto" or ".pchybrid")
            {
                await _viewModel.OpenThemeFileAsync(path);
                continue;
            }

            if (ext is ".ttf" or ".otf")
            {
                await _viewModel.ImportFontFileAsync(path);
                continue;
            }

            if (ext == ".gif")
            {
                await _viewModel.ImportMediaFileAsync(path, ThemeAssetKind.Gif);
                continue;
            }

            if (ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv")
            {
                await _viewModel.ImportMediaFileAsync(path, ThemeAssetKind.Video);
                continue;
            }

            if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp")
            {
                if (_viewModel.Document.Mode == ScreenMode.InfoScreen) await _viewModel.ImportMediaFileAsync(path, ThemeAssetKind.Image);
                else await _viewModel.AddPhotoFilesAsync([path]);
            }
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_sessionSaved) { DisposeRuntime(); return; }
        if (_savingSession) { e.Cancel = true; return; }
        // Closing to the tray keeps the existing session running.
        if (!_exitRequested && _viewModel.CloseToTray)
        {
            e.Cancel = true;
            _viewModel.SetEditorActive(false);
            Hide();
            return;
        }
        e.Cancel = true;
        _savingSession = true;
        _viewModel.CancelFileOperation();
        IsEnabled = false;
        try
        {
            _sessionSaveTask = _viewModel.SaveLastSessionAsync();
            await _sessionSaveTask;
            await _viewModel.ClearDisplayForExitAsync();
            _sessionSaved = true;
            Close();
        }
        catch (Exception ex)
        {
            _exitRequested = false;
            _sessionSaveTask = null;
            IsEnabled = true;
            MessageBox.Show(this, "Could not save the last session. The app will stay open so your work is preserved.\n\n" + ex.Message,
                "Save session", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _savingSession = false; }
    }

    public void MarkSessionSavedForExit() => _sessionSaved = true;

    // Windows logoff does not wait for an async Closing event. Snapshot while the dispatcher is paused.
    public void SaveSessionForWindowsShutdown()
    {
        if (_disposed || _sessionSaved) return;
        _viewModel.CancelFileOperation();
        (_sessionSaveTask ?? _viewModel.SaveLastSessionAsync()).GetAwaiter().GetResult();
        _viewModel.ClearDisplayForExitAsync().GetAwaiter().GetResult();
        _sessionSaved = true;
    }

    private void DisposeRuntime()
    {
        if (_disposed)
            return;

        _disposed = true;
        _deviceChangeTimer?.Stop();
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowMessageHook);
            _windowSource = null;
        }
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _viewModel.Dispose();
    }
}
