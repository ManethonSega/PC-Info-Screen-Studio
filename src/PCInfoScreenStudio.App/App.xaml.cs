using System.Windows;
using System.Windows.Threading;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;
using SkiaSharp;

namespace PCInfoScreenStudio;

public partial class App : Application
{
    private bool _shuttingDown;
    private SingleInstanceService? _instance;
    private DispatcherTimer? _activationTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (e.Args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            RunSmokeTest();
            return;
        }

        try
        {
            if (e.Args.Length == 3 && e.Args[0] == WindowsStartupService.ConfigureArgument &&
                e.Args[1] is "enable" or "disable")
            {
                new WindowsStartupService().Configure(e.Args[1] == "enable", e.Args[2]);
                ShutdownSafely(0);
                return;
            }
            if (e.Args.Contains(WindowsStartupService.StartupArgument) && !e.Args.Contains(WindowsStartupService.ScheduledArgument) &&
                new AppSettingsService().Load().StartWithWindows && new WindowsStartupService().LaunchApprovedStartupTask())
            {
                ShutdownSafely(0);
                return;
            }
            if (SensorStartupService.RequestIfNeeded(new AppSettingsService().Load(), e.Args, out var sensorStartupError))
            {
                ShutdownSafely(0);
                return;
            }
            _instance = new SingleInstanceService(e.Args.Contains("--wait-for-existing-instance"), !e.Args.Contains(WindowsStartupService.StartupArgument));
            if (!_instance.IsPrimary) { ShutdownSafely(0); return; }
            MainWindow = new MainWindow { StartInTray = e.Args.Contains(WindowsStartupService.StartupArgument) };
            _activationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _activationTimer.Tick += (_, _) => { if (_instance?.ConsumeActivation() == true && MainWindow is MainWindow window) window.RestoreFromTray(); };
            _activationTimer.Start();
            MainWindow.Show();
            if (sensorStartupError is not null)
                MessageBox.Show(MainWindow, sensorStartupError, "Sensor access", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowFatalStartupError(ex);
            ShutdownSafely(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        try { if (MainWindow is MainWindow window) window.SaveSessionForWindowsShutdown(); }
        catch (Exception ex) { WriteCrashLog("Could not save session at Windows shutdown", ex); }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationTimer?.Stop();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void RunSmokeTest()
    {
        try
        {
            using (var workspace = new ThemePackageService().CreateNewWorkspace())
            {
                var renderer = new ThemeRenderer();
                using var bitmap = renderer.Render(workspace);
                if (bitmap.Width <= 0 || bitmap.Height <= 0)
                    throw new InvalidOperationException("Renderer returned an invalid bitmap.");

                var photoPath = Path.Combine(workspace.RootDirectory, "smoke-photo.png");
                using (var photo = new SKBitmap(64, 48))
                {
                    photo.Erase(SKColors.CornflowerBlue);
                    using var image = SKImage.FromBitmap(photo);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var stream = File.Create(photoPath);
                    data.SaveTo(stream);
                }

                workspace.Document.PhotoFrame.CaptionMode = PhotoCaptionMode.Custom;
                workspace.Document.PhotoFrame.CustomCaption = "Photo frame smoke test";
                workspace.Document.PhotoFrame.Photos.Add(new PhotoFrameItem
                {
                    DisplayName = "Smoke photo",
                    SourcePath = photoPath
                });
                workspace.Document.RuntimeMode = RuntimeScreenMode.PhotoFrame;
                using var photoFrame = renderer.Render(workspace);
                workspace.Document.HybridWidgets.Add(new WidgetModel
                {
                    Type = WidgetType.Text,
                    Label = "Hybrid overlay",
                    Width = 150,
                    Height = 40,
                    X = 10,
                    Y = 10
                });
                workspace.Document.RuntimeMode = RuntimeScreenMode.Hybrid;
                using var hybridFrame = renderer.Render(workspace);
                if (photoFrame.Width != workspace.Document.CanvasWidth || hybridFrame.Height != workspace.Document.CanvasHeight)
                    throw new InvalidOperationException("Photo frame renderer returned an invalid bitmap.");
            }

            var window = new MainWindow();
            window.MarkSessionSavedForExit();
            window.Measure(new Size(1480, 880));
            window.Arrange(new Rect(0, 0, 1480, 880));
            window.UpdateLayout();

            ShutdownSafely(0);
        }
        catch (Exception ex)
        {
            WriteCrashLog("Smoke test failed", ex);
            ShutdownSafely(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatalStartupError(e.Exception);
        ShutdownSafely(1);
    }

    private static void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog("Unhandled application exception", ex);
        else
            WriteCrashLog("Unhandled application exception", new Exception(e.ExceptionObject?.ToString() ?? "Unknown fatal error."));
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void ShowFatalStartupError(Exception ex)
    {
        var logPath = WriteCrashLog("Application startup/runtime failure", ex);
        try
        {
            MessageBox.Show(
                "PC Info Screen Studio could not start correctly.\n\n" +
                ex.GetBaseException().Message +
                "\n\nA diagnostic log was written to:\n" + logPath,
                "PC Info Screen Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private void ShutdownSafely(int exitCode)
    {
        if (_shuttingDown)
            return;

        _shuttingDown = true;
        Shutdown(exitCode);
    }

    private static string WriteCrashLog(string context, Exception ex)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PCInfoScreenStudio",
                "Logs");

            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup.log");
            var entry =
                $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}" +
                new string('-', 80) +
                Environment.NewLine;

            File.AppendAllText(path, entry);
            return path;
        }
        catch
        {
            return "Unable to write diagnostic log.";
        }
    }
}
