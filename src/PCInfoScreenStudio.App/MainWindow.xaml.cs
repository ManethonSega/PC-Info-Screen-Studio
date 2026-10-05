using System.ComponentModel;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    public bool StartInTray { get; set; }
    private Point _photoDragStart;
    private PhotoFrameItem? _draggedPhoto;

    public MainWindow() : this(new MainViewModel()) { }

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;

        var trayMenu = new WinForms.ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            BackColor = System.Drawing.Color.FromArgb(23, 26, 31),
            ForeColor = System.Drawing.Color.FromArgb(240, 242, 245),
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

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = this,
            DataContext = _viewModel
        };
        window.ShowDialog();
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
        IsEnabled = false;
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
        if (!_viewModel.IsEditorPage) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
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
                _viewModel.ImportFontFile(path);
                continue;
            }

            if (ext == ".gif")
            {
                _viewModel.ImportMediaFile(path, ThemeAssetKind.Gif);
                continue;
            }

            if (ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv")
            {
                _viewModel.ImportMediaFile(path, ThemeAssetKind.Video);
                continue;
            }

            if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp")
            {
                if (_viewModel.Document.Mode == ScreenMode.InfoScreen) _viewModel.ImportMediaFile(path, ThemeAssetKind.Image);
                else _viewModel.AddPhotoFiles([path]);
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
        IsEnabled = false;
        try
        {
            _sessionSaveTask = _viewModel.SaveLastSessionAsync();
            await _sessionSaveTask;
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
        (_sessionSaveTask ?? _viewModel.SaveLastSessionAsync()).GetAwaiter().GetResult();
        _sessionSaved = true;
    }

    private void DisposeRuntime()
    {
        if (_disposed)
            return;

        _disposed = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _viewModel.Dispose();
    }
}
