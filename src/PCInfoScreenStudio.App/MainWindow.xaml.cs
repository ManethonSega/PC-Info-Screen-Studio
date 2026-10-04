using System.ComponentModel;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using System.Windows;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        var trayMenu = new WinForms.ContextMenuStrip();
        trayMenu.Items.Add("Show PC Info Screen Studio", null, (_, _) => RestoreFromTray());
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.RecoverIfAvailableAsync();
        _viewModel.StartAutoDisplayIfEnabled();
    }

    private void RestoreFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
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
