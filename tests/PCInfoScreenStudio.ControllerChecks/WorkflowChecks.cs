using System.IO;
using System.Windows;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;
using SkiaSharp;

internal static class WorkflowChecks
{
    public static void Check(string root, Action<Task> awaitTask, Action<Func<bool>> pump)
    {
        var album = Path.Combine(root, "everyday-album");
        Directory.CreateDirectory(album);
        using (var bitmap = new SKBitmap(8, 8))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(Path.Combine(album, "photo.png"))) data.SaveTo(stream);
        var infoPath = Path.Combine(root, "everyday.t3theme");
        var photoPath = Path.Combine(root, "everyday.pcphoto");
        var hybridPath = Path.Combine(root, "everyday.pchybrid");
        var packages = new ThemePackageService();
        var modes = new ModeThemeService(root);
        using (var source = packages.CreateNewWorkspace())
        {
            source.Document.Widgets.Add(new WidgetModel { Type = WidgetType.Value, Label = "CPU", DataSource = "CPU.Usage" });
            awaitTask(packages.SaveAsync(source, infoPath));
            source.Document.Mode = ScreenMode.PhotoFrame;
            source.Document.PhotoFrame.WatchedFolder = album;
            modes.SaveToPath(source.Document, ScreenMode.PhotoFrame, photoPath);
            source.Document.Mode = ScreenMode.Hybrid;
            modes.SaveToPath(source.Document, ScreenMode.Hybrid, hybridPath);
        }
        var settings = new SettingsController(new AppSettingsService(Path.Combine(root, "everyday-preferences.json")))
        { FirstRunCompleted = true, StartWithWindows = false, AutoStartDisplay = false, CloseToTray = true };
        var sessions = new SessionStateService(Path.Combine(root, "everyday-session"));
        var vm = new MainViewModel(settings, sessions);
        vm.SetEditorActive(false);
        awaitTask(vm.OpenThemeFileAsync(infoPath));
        vm.AddWidgetCommand.Execute("Text");
        vm.SelectedWidget!.Label = "Added directly";
        vm.SelectedWidget.X = 54;
        awaitTask(vm.SaveCurrentThemeAsync());
        var info = packages.LoadAsync(infoPath); awaitTask(info);
        using (info.Result) Assert(info.Result.Document.Widgets.Count == 2 && info.Result.Document.Widgets[1].X == 54, "Info Screen Add and Save must preserve new widgets.");
        vm.Document.Mode = ScreenMode.PhotoFrame;
        vm.LoadModeThemeFile(photoPath);
        vm.Document.PhotoFrame.CaptionFontSize = 24;
        awaitTask(vm.SaveCurrentThemeAsync());
        Assert(modes.Load(photoPath).PhotoFrame.CaptionFontSize == 24 && vm.Document.PhotoFrame.Photos.Count == 1, "Photo Frame Save must retain the playlist and save global caption settings.");
        vm.Document.Mode = ScreenMode.Hybrid;
        vm.LoadModeThemeFile(hybridPath);
        vm.AddWidgetCommand.Execute("Shape");
        vm.SelectedWidget!.X = 185;
        vm.SelectedWidget.Y = 111;
        awaitTask(vm.SaveCurrentThemeAsync());
        var hybrid = modes.Load(hybridPath);
        Assert(hybrid.HybridWidgets.Count == 1 && hybrid.HybridWidgets[0].X == 185 && hybrid.HybridWidgets[0].Y == 111, "Hybrid Add and Save must retain exact layer positions.");
        vm.Document.Mode = ScreenMode.InfoScreen;
        Assert(vm.Document.Widgets.Count == 2 && !vm.HasUnsavedChanges, "Changing modes after Save must preserve each mode and its clean state.");
        vm.Document.Mode = ScreenMode.Hybrid;
        var document = vm.Document;
        var window = new PCInfoScreenStudio.MainWindow(vm);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show();
            pump(() => window.IsEnabled);
            vm.SetEditorActive(false); // Never sample physical sensors in this workflow check.
            window.WindowState = WindowState.Minimized;
            Assert(!window.IsVisible && !vm.IsEditorActive, "Minimizing must move the runtime to the tray and suspend editor work.");
            window.RestoreFromTray();
            vm.SetEditorActive(false);
            Assert(window.IsVisible && ReferenceEquals(vm.Document, document), "Restoring from the tray must show the same working session.");
            window.Close();
            Assert(!closed && !window.IsVisible && ReferenceEquals(vm.Document, document), "The close button must keep the configured tray runtime alive.");
            window.RestoreFromTray();
            vm.SetEditorActive(false);
            window.ExitFromTray();
            pump(() => closed);
        }
        finally
        {
            if (!closed) { window.MarkSessionSavedForExit(); window.Close(); }
        }
        using var reopened = new MainViewModel(settings, sessions);
        reopened.SetEditorActive(false);
        var restored = reopened.RestoreLastSessionAsync(); awaitTask(restored);
        Assert(restored.Result && reopened.Document.Mode == ScreenMode.Hybrid && !reopened.HasUnsavedChanges &&
            reopened.Document.Widgets.Count == 2 && reopened.Document.HybridWidgets[0].X == 185,
            "Tray Exit and reopen must restore the saved mode, both layouts and the clean theme state.");
        Console.WriteLine("PASS: everyday Add/Save in all three modes, actual WPF minimize/close/restore/Exit, and session reopening.");
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
