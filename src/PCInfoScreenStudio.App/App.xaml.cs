using System.Windows;
using System.Windows.Threading;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;

namespace PCInfoScreenStudio;

public partial class App : Application
{
    private bool _shuttingDown;

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
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowFatalStartupError(ex);
            ShutdownSafely(1);
        }
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
            }

            var window = new MainWindow();
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
