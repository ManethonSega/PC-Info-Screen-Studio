using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class MatrixWorkspaceChecks
{
    public static void Check(string root, Action<Func<bool>> pump, string? captures)
    {
        MatrixTypographyChecks.Check(pump);
        var path = Path.Combine(root, "matrix-workspace.json");
        var settings = new SettingsController(new AppSettingsService(path))
            { FirstRunCompleted = true, AutoStartDisplay = false, CloseToTray = false, StartWithWindows = false };
        var vm = new MainViewModel(settings, new SessionStateService(Path.Combine(root, "matrix-session")));
        var window = new PCInfoScreenStudio.MainWindow(vm);
        try
        {
            window.Show(); pump(() => window.IsEnabled); vm.SetEditorActive(false);
            var selector = (ComboBox)window.FindName("ScreenSelector");
            var document = vm.Document;
            var original = (document.CanvasWidth, document.CanvasHeight, document.Mode, vm.IsDirty, vm.SelectedWidget);
            var layout = ((WorkspaceDock)window.FindName("LeftDock")).CaptureLayout();
            selector.SetCurrentValue(Selector.SelectedValueProperty, DisplayTarget.Matrix900);
            selector.GetBindingExpression(Selector.SelectedValueProperty)!.UpdateSource(); Flush(window);
            pump(() => vm.CanChooseScreen);
            Require(vm.IsMatrixWorkspace && !vm.IsEditorPage && !vm.IsCanvasActive && !vm.LivePreview, "Choosing Matrix suspends the LCD editor/runtime rendering path.");
            var matrix = (MatrixWorkspaceView)window.FindName("MatrixWorkspace");
            Require(matrix.IsVisible && !((Grid)window.FindName("EditorWorkspace")).IsVisible,
                "Matrix opens its dedicated controls instead of changing the LCD canvas into an LED canvas.");
            Require(!vm.NewCommand.CanExecute(null) && !vm.UndoCommand.CanExecute(null), "LCD document shortcuts do not edit a hidden theme from Matrix mode.");
            Require(!vm.Matrix.ApplyCommand.CanExecute(null), "A disconnected preview cannot claim to apply to hardware.");
            Require((document.CanvasWidth, document.CanvasHeight, document.Mode, vm.IsDirty, vm.SelectedWidget) == original,
                "Selecting Matrix preserves all LCD theme geometry, mode, selection and dirty state.");
            var surface = Find<MatrixSurface>(matrix)!;
            Require(surface.SelectTileAt(PixelPoint(surface, 40, 2)) && vm.Matrix.SelectedTile == 1, "Clicking a column selects it and its controls.");
            vm.Matrix.Layout = 1; vm.Matrix.Text = "SEGA MATRIX"; vm.Matrix.Colour = "#FF21E6E8"; Flush(window);
            Require(matrix.FindName("TileSelector") is null && ((TextBlock)matrix.FindName("SelectedTileCaption")).Text == "Tile 1 selected", "The preview selects tiles directly, with an explicit selection caption and no tile dropdown.");
            Require(((TextBox)matrix.FindName("MessageBox")).Text == "SEGA MATRIX", "The message field and preview stay synchronized after restoring draft values.");
            Require(vm.Matrix.Colour == "#21E6E8" && vm.Matrix.PreviewPixels().Any(b => b != 0), "Colour picker ARGB values normalize to physical RGB, and the preview renders pixels.");
            Capture(window, captures, "Matrix900-text.png");
            vm.Matrix.Text = "Hello 👌"; Flush(window);
            Require(vm.Matrix.Validation.Length > 0 && !vm.Matrix.ApplyCommand.CanExecute(null), "Unsupported firmware text is explained and never silently corrupted.");
            vm.Matrix.Text = "SEGA MATRIX";
            vm.Matrix.ContentKind = MatrixContent.PixelArt;
            surface.Focus();
            surface.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            Require(vm.Matrix.Snapshot.Tiles[0].Pixels[1] == 230, "Keyboard Space paints the selected pixel using the chosen brush.");
            surface.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Delete) { RoutedEvent = Keyboard.KeyDownEvent });
            Require(vm.Matrix.Snapshot.Tiles[0].Pixels.Take(3).All(b => b == 0), "Keyboard Delete erases the pixel.");
            for (int x = 12; x < 78; x++) { vm.Matrix.PaintPixel(x, 2, false); vm.Matrix.PaintPixel(x, 7, false); }
            Capture(window, captures, "Matrix900-pixel-art.png");
            vm.Matrix.Layout = 9; vm.Matrix.SelectedTile = 4; vm.Matrix.ContentKind = MatrixContent.PixelArt;
            vm.Matrix.PaintPixel(61, 1, false); vm.Matrix.PaintPixel(2, 9, false);
            Require(vm.Matrix.Snapshot.Tiles[4].Pixels[93] == 33, "Six-tile painting maps into its local pixel buffer and ignores other regions.");
            window.Width = 1080; window.Height = 680; Flush(window);
            var bounds = surface.TransformToAncestor(window).TransformBounds(new Rect(surface.RenderSize));
            Require(bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, "Compact Matrix preview fits the window.");
            Capture(window, captures, "Matrix900-compact.png");
            var saved = vm.Matrix.Snapshot.Tiles[4].Pixels;
            Require(surface.SelectTileAt(PixelPoint(surface, 35, 8)) && vm.Matrix.SelectedTile == 3, "Clicking a bottom-row tile selects its native slot.");
            Require(vm.Matrix.Snapshot.Tiles[4].Pixels.SequenceEqual(saved), "Selecting another tile does not paint into saved art.");
            Require(!surface.SelectTileAt(new Point(-5, -5)) && vm.Matrix.SelectedTile == 3, "Clicking outside the LED canvas leaves selection unchanged.");
            surface.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.End) { RoutedEvent = Keyboard.KeyDownEvent });
            Require(vm.Matrix.SelectedTile == 5, "Keyboard End selects the final tile without a dropdown.");
            vm.Matrix.SelectedTile = 4;
            var heightSelector = (ComboBox)matrix.FindName("RowHeightSelector");
            heightSelector.SetCurrentValue(Selector.SelectedValueProperty, 5); heightSelector.GetBindingExpression(Selector.SelectedValueProperty)!.UpdateSource(); Flush(window);
            Require(vm.Matrix.RowHeight == 5, "The actual WPF row-height control enables five dots with no gap.");
            var fontSelector = (ComboBox)matrix.FindName("FiveDotFontSelector");
            fontSelector.SetCurrentValue(Selector.SelectedValueProperty, MatrixFiveDotFont.Pixel5x5Thin); fontSelector.GetBindingExpression(Selector.SelectedValueProperty)!.UpdateSource(); Flush(window);
            Require(vm.Matrix.FiveDotFont == MatrixFiveDotFont.Pixel5x5Thin && fontSelector.IsVisible, "Five-dot font selector updates actual settings.");
            Capture(window, captures, "Matrix900-five-row.png");
            vm.ShowPage(true); Flush(window); Require(!matrix.IsVisible && vm.IsHardwarePage, "Hardware navigation hides the Matrix workspace.");
            vm.ShowPage(false); Flush(window); Require(matrix.IsVisible, "Editor navigation returns to the selected Matrix workspace.");
            vm.SelectedDisplayTarget = DisplayTarget.Turing52; Flush(window);
            pump(() => vm.CanChooseScreen);
            Require(!matrix.IsVisible && vm.IsEditorPage && vm.Document.CanvasWidth == 1280, "Choosing the 5.2-inch target restores its LCD workspace and sizing.");
            Require(((WorkspaceDock)window.FindName("LeftDock")).CaptureLayout().Panels.Select(p => (p.Id, p.IsOpen)).SequenceEqual(layout.Panels.Select(p => (p.Id, p.IsOpen))), "Matrix controls do not replace dock preferences.");
            vm.SelectedDisplayTarget = DisplayTarget.Matrix900; pump(() => vm.CanChooseScreen); vm.Matrix.Text = "PERSIST ME";
        }
        finally { window.MarkSessionSavedForExit(); window.Close(); }
        var loaded = new SettingsController(new AppSettingsService(path));
        Require(loaded.Matrix.Selected && loaded.Matrix.Layout == 9 && loaded.Matrix.RowHeight == 5 && loaded.Matrix.FiveDotFont == MatrixFiveDotFont.Pixel5x5Thin && loaded.Matrix.Tiles[4].Text == "PERSIST ME" && loaded.Matrix.Tiles[4].Pixels[93] == 33,
            "Exit flushes the latest Matrix layout, selection target, text and pixels to validated settings.");
        using var secondVm = new MainViewModel(loaded, new SessionStateService(Path.Combine(root, "matrix-second")));
        Require(secondVm.IsMatrixWorkspace && secondVm.Matrix.Layout == 9 && secondVm.Matrix.SelectedTile == 0, "Restart restores Matrix without changing the LCD theme format.");
        Console.WriteLine("PASS: real WPF Matrix target selection, dedicated controls, LCD preservation, unsupported text validation, keyboard painting, tile mapping, compact sizing, navigation and settings restart.");
    }
    private static void Flush(Window window) { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle); window.UpdateLayout(); }
    private static Point PixelPoint(MatrixSurface surface, int x, int y)
    {
        double scale = Math.Min(surface.ActualWidth / 90, surface.ActualHeight / 10);
        return new((surface.ActualWidth - scale * 90) / 2 + (x + .5) * scale, (surface.ActualHeight - scale * 10) / 2 + (y + .5) * scale);
    }
    private static T? Find<T>(DependencyObject p) where T : DependencyObject
    { if (p is T found) return found; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++) if (Find<T>(VisualTreeHelper.GetChild(p, i)) is T child) return child; return null; }
    private static void Capture(Window window, string? directory, string name)
    {
        if (directory is null) return; Directory.CreateDirectory(directory); Flush(window); var element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); using var file = File.Create(Path.Combine(directory, name)); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
