using System.ComponentModel;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.ViewModels;
using WinForms = System.Windows.Forms;

namespace PCInfoScreenStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly WinForms.NotifyIcon _trayIcon;
    private bool _exitRequested;
    private bool _disposed;
    private Point _photoDragStart;
    private PhotoFrameItem? _draggedPhoto;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        var trayMenu = new WinForms.ContextMenuStrip();
        trayMenu.Items.Add("Show PC Info Screen Studio", null, (_, _) => RestoreFromTray());
        var modeMenu = new WinForms.ToolStripMenuItem("Mode");
        modeMenu.DropDownItems.Add("Info Screen", null, (_, _) => SetModeFromTray(ScreenMode.InfoScreen));
        modeMenu.DropDownItems.Add("Photo Frame", null, (_, _) => SetModeFromTray(ScreenMode.PhotoFrame));
        modeMenu.DropDownItems.Add("Hybrid", null, (_, _) => SetModeFromTray(ScreenMode.Hybrid));
        trayMenu.Items.Add(modeMenu);
        var photoMenu = new WinForms.ToolStripMenuItem("Photo frame");
        photoMenu.DropDownItems.Add("Previous photo", null, (_, _) => Dispatcher.Invoke(_viewModel.PreviousPhoto));
        photoMenu.DropDownItems.Add("Play / Pause", null, (_, _) => Dispatcher.Invoke(_viewModel.TogglePhotoPlayback));
        photoMenu.DropDownItems.Add("Next photo", null, (_, _) => Dispatcher.Invoke(_viewModel.NextPhoto));
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

    private void OnFileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
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
        await _viewModel.RecoverIfAvailableAsync();
        _viewModel.StartAutoDisplayIfEnabled();
    }

    private void RestoreFromTray()
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

    private void ExitFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            _exitRequested = true;
            Close();
        });
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        foreach (var path in files)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".t3theme")
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
                _viewModel.ImportMediaFile(path, ThemeAssetKind.Image);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // The normal window close button keeps the display/runtime alive and
        // moves the editor to the notification area. Use the tray menu's Exit
        // command for an actual application shutdown.
        if (!_exitRequested && _viewModel.CloseToTray)
        {
            e.Cancel = true;
            _viewModel.SetEditorActive(false);
            Hide();
            return;
        }

        if (_viewModel.IsDirty)
        {
            var result = MessageBox.Show(
                "This theme has unsaved changes. Exit and discard them?",
                "Unsaved changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                _exitRequested = false;
                e.Cancel = true;
                return;
            }
        }

        DisposeRuntime();
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
