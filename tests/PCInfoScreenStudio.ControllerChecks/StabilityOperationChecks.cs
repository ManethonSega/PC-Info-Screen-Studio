using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;
using SkiaSharp;

internal static class StabilityOperationChecks
{
    public static void Check(string root, Action<Task> awaitTask, Action<Func<bool>> pump, string? captureDirectory = null)
    {
        var settingsPath = Path.Combine(root, "operation-settings.json");
        var store = new AppSettingsService(settingsPath);
        store.Save(new AppSettings { FirstRunCompleted = true, StartWithWindows = false, AutoStartDisplay = false, CloseToTray = false, WeatherCity = "Recovered" });
        store.Save(new AppSettings { FirstRunCompleted = true, StartWithWindows = false, AutoStartDisplay = false, CloseToTray = false, WeatherCity = "Newer" });
        File.WriteAllText(settingsPath, "broken JSON");
        var settings = new SettingsController(store);
        using var vm = new MainViewModel(settings, new SessionStateService(Path.Combine(root, "operation-session")));
        vm.SetEditorActive(false);
        Require(vm.RecoveryNotice.Contains("backup"), "The recovery notice must reach the actual view model without a modal prompt");
        var window = new PCInfoScreenStudio.MainWindow(vm);
        try
        {
            var startupGate = GetGate(Path.Combine(root, "operation-session", "last-session.t3theme"));
            startupGate.Wait();
            try
            {
                window.Show(); pump(() => vm.IsFileOperationBusy); window.UpdateLayout();
                var startupCancel = All<Button>(window).Single(b => ReferenceEquals(b.Command, vm.CancelFileOperationCommand));
                Require(startupCancel.IsVisible && startupCancel.IsEnabled, "Startup restoration must leave its actual Cancel control responsive");
                vm.CancelFileOperation(); pump(() => !vm.IsFileOperationBusy);
            }
            finally { startupGate.Release(); }
            pump(() => window.IsEnabled && !vm.IsFileOperationBusy); vm.SetEditorActive(false);
            Require(vm.ActivityStatus.Contains("backup"), "Recovery remains visible even when hardware status changes");
            if (captureDirectory is not null) Capture(window, captureDirectory, "Settings-recovery.png");
            vm.DismissRecoveryNoticeCommand.Execute(null);
            Require(string.IsNullOrEmpty(vm.RecoveryNotice), "The recovery notice can be dismissed");

            var path = Path.Combine(root, "guarded-save.t3theme");
            vm.Workspace.FilePath = path;
            vm.Document.Name = "Before Save";
            var original = vm.Workspace;
            var gate = GetGate(path); gate.Wait();
            Task saving;
            try
            {
                saving = vm.SaveCurrentThemeAsync();
                Require(vm.IsFileOperationBusy && vm.CanEditContent && !vm.CanChangeWorkspace && vm.ActivityStatus.Contains("Saving"), "Saving exposes inline progress while ordinary editing remains available");
                Require(!vm.SaveCommand.CanExecute(null) && !vm.NewCommand.CanExecute(null) && !vm.OpenCommand.CanExecute(null) && !vm.ImportImageCommand.CanExecute(null) && !vm.UndoCommand.CanExecute(null), "Conflicting menus and shortcuts are disabled");
                window.UpdateLayout();
                var cancel = All<Button>(window).Single(b => ReferenceEquals(b.Command, vm.CancelFileOperationCommand));
                Require(cancel.IsVisible && cancel.IsEnabled, "The actual footer exposes an enabled Cancel button during long file operations");
                if (captureDirectory is not null) Capture(window, captureDirectory, "File-operation-progress.png");
                vm.NewCommand.Execute(null);
                awaitTask(vm.OpenThemeFileAsync("not-opened.t3theme"));
                awaitTask(vm.ImportMediaFileAsync("not-imported.png", ThemeAssetKind.Image));
                awaitTask(vm.SaveCurrentThemeAsync());
                Require(ReferenceEquals(original, vm.Workspace), "Repeated New/Open/import/Save requests must not replace a workspace or queue another save");
                vm.Document.Mode = ScreenMode.Hybrid;
                Require(vm.Document.Mode == ScreenMode.InfoScreen, "Mode changes must not redirect an in-progress operation");
                vm.Document.Name = "Edited During Save";
            }
            finally { gate.Release(); }
            awaitTask(saving);
            Require(!vm.IsFileOperationBusy && vm.CanChangeWorkspace && vm.IsDirty && vm.HasUnsavedChanges, "Ending a save restores commands and keeps later edits dirty");
            var loaded = new ThemePackageService().LoadAsync(path); awaitTask(loaded);
            using (var saved = loaded.Result) Require(saved.Document.Name == "Before Save", "The saved file remains its frozen snapshot");

            var hash = SHA256.HashData(File.ReadAllBytes(path));
            gate.Wait();
            try
            {
                var cancelled = vm.SaveCurrentThemeAsync();
                vm.CancelFileOperationCommand.Execute(null);
                awaitTask(cancelled);
            }
            finally { gate.Release(); }
            Require(!vm.IsFileOperationBusy && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash) && vm.HasUnsavedChanges,
                "A cancelled save keeps the previous destination and unsaved edits");

            // Mark the test workspace clean before Open, avoiding an unrelated discard dialog.
            var sessions = (ThemeSessionController)typeof(MainViewModel).GetField("_themeSessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            sessions.Loaded(ScreenMode.InfoScreen, path); vm.Workspace.IsDirty = false;
            var invalid = Path.Combine(root, "invalid-open.t3theme"); File.WriteAllText(invalid, "invalid ZIP");
            awaitTask(vm.OpenThemeFileAsync(invalid));
            Require(ReferenceEquals(original, vm.Workspace) && !vm.IsFileOperationBusy && vm.RecoveryNotice.Contains("current workspace was kept"),
                "A failed open must preserve the prior workspace and restore command availability with an inline notice");
            vm.DismissRecoveryNoticeCommand.Execute(null);
            var sessionGate = GetGate(Path.Combine(root, "operation-session", "last-session.t3theme"));
            sessionGate.Wait();
            try
            {
                var restoring = vm.RestoreLastSessionAsync(); vm.CancelFileOperation(); awaitTask(restoring);
                Require(!restoring.Result && !vm.IsFileOperationBusy && ReferenceEquals(original, vm.Workspace),
                    "Cancelling session restoration releases the guard and retains the current workspace");
            }
            finally { sessionGate.Release(); }
            var missingImport = vm.ImportMediaFileAsync(Path.Combine(root, "missing-import.png"), ThemeAssetKind.Image); awaitTask(missingImport);
            Require(!vm.IsFileOperationBusy && ReferenceEquals(original, vm.Workspace), "Failed imports leave the workspace and guard usable");
            var source = Path.Combine(root, "large-import.mp4");
            using (var file = File.Create(source)) file.SetLength(32L * 1024 * 1024);
            var beforeAssets = vm.Document.Assets.Count;
            var importing = vm.ImportMediaFileAsync(source, ThemeAssetKind.Video);
            vm.CancelFileOperation(); awaitTask(importing);
            Require(!vm.IsFileOperationBusy && vm.Document.Assets.Count == beforeAssets && !Directory.EnumerateFiles(vm.Workspace.AssetsDirectory, "*.mp4", SearchOption.AllDirectories).Any(),
                "Cancelling an import removes partial copies without attaching assets");
            var photoDirectory = Path.Combine(root, "operation-photos"); Directory.CreateDirectory(photoDirectory);
            var photoPath = Path.Combine(photoDirectory, "first.jpg");
            using (var image = new SKBitmap(20, 20))
            {
                image.Erase(SKColors.Red); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85); File.WriteAllBytes(photoPath, encoded.ToArray());
                File.WriteAllBytes(Path.Combine(photoDirectory, "second.jpg"), encoded.ToArray());
            }
            vm.Document.Mode = ScreenMode.PhotoFrame;
            awaitTask(vm.AddPhotoFilesAsync([photoPath, photoPath]));
            Require(vm.Document.PhotoFrame.Photos.Count == 1 && vm.Document.PhotoFrame.Photos[0].AssetId is null && File.Exists(photoPath), "Asynchronous photo preparation avoids duplicates and keeps source photos linked");
            vm.Document.PhotoFrame.WatchedFolder = photoDirectory;
            var modePath = new ModeThemeService(root).Save(vm.Document, ScreenMode.PhotoFrame, "asynchronous-folder");
            sessions.Loaded(ScreenMode.PhotoFrame, modePath); vm.Workspace.IsDirty = false;
            awaitTask(vm.LoadModeThemeFileAsync(modePath));
            Require(vm.Document.PhotoFrame.Photos.Count == 2 && !vm.IsFileOperationBusy && vm.Document.PhotoFrame.WatchedFolder == photoDirectory,
                "UI mode-theme loading reads folder metadata asynchronously and applies one complete playlist");
            var malformedMode = Path.Combine(root, "malformed.pcphoto");
            File.WriteAllText(malformedMode, "{\"FormatVersion\":2,\"Mode\":\"PhotoFrame\",\"PhotoFrame\":null}");
            var retainedPhotos = vm.Document.PhotoFrame.Photos;
            awaitTask(vm.LoadModeThemeFileAsync(malformedMode));
            Require(ReferenceEquals(retainedPhotos, vm.Document.PhotoFrame.Photos) && !vm.IsFileOperationBusy,
                "Incomplete mode metadata cannot partially replace a valid playlist");
            using var cancelledPhoto = new CancellationTokenSource(); cancelledPhoto.Cancel();
            try { awaitTask(PhotoImportService.PrepareAsync(() => Directory.EnumerateFiles(photoDirectory), cancelledPhoto.Token)); throw new Exception("Cancelled photo scan succeeded"); }
            catch (OperationCanceledException) { }
        }
        finally { window.MarkSessionSavedForExit(); window.Close(); }
    }

    private static SemaphoreSlim GetGate(string path)
        => (SemaphoreSlim)typeof(AppSettingsService).Assembly.GetType("PCInfoScreenStudio.Services.RecoveryFileStore")!.GetMethod("Gate")!.Invoke(null, [path])!;
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Capture(PCInfoScreenStudio.MainWindow window, string directory, string name)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1560, 1040)); root.Arrange(new Rect(0, 0, 1560, 1040)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1560, 1040, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
