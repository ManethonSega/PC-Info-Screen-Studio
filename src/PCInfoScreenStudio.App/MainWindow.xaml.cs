using System.ComponentModel;
using System.Windows;
using PCInfoScreenStudio.ViewModels;
using PCInfoScreenStudio.Models;

namespace PCInfoScreenStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

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
        if (_viewModel.IsDirty)
        {
            var result = MessageBox.Show(
                "This theme has unsaved changes. Close and discard them?",
                "Unsaved changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _viewModel.Dispose();
    }
}
