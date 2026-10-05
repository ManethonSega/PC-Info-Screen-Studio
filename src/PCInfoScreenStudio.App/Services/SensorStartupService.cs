using System.ComponentModel;
using System.Diagnostics;

namespace PCInfoScreenStudio.Services;

public static class SensorStartupService
{
    public const string ElevationAttemptArgument = "--sensor-elevation-attempted";

    public static bool ShouldRequestElevation(bool enabled, bool elevated, bool driverInstalled, IEnumerable<string> arguments)
        => enabled && !elevated && driverInstalled && !arguments.Any(argument =>
            argument.Equals(ElevationAttemptArgument, StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));

    public static ProcessStartInfo CreateStartInfo(string executable, string entryPoint, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
        // Preserve dotnet-hosted development launches as well as the portable executable.
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(entryPoint);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(ElevationAttemptArgument);
        return startInfo;
    }

    public static bool RequestIfNeeded(AppSettings settings, string[] arguments, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows()) return false;
        using var hardware = new HardwareMetricsService();
        if (!ShouldRequestElevation(settings.RequestAdministratorAtStartup, hardware.IsElevated,
            hardware.IsLowLevelDriverInstalled, arguments)) return false;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "Could not find the application executable. Available sensors will still run.";
            return false;
        }
        return TryLaunch(CreateStartInfo(executable, Environment.GetCommandLineArgs()[0], arguments), startInfo =>
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }, out error);
    }

    public static bool TryLaunch(ProcessStartInfo startInfo, Func<ProcessStartInfo, bool> launch, out string? error)
    {
        error = null;
        try
        {
            if (launch(startInfo)) return true;
            error = "The administrator launch did not start. Available sensors will still run.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Declining UAC keeps the current process usable. Do not prompt again this launch.
        }
        catch (Exception ex)
        {
            error = "Could not request administrator access: " + ex.Message +
                "\n\nThe app will continue with the sensors available to your account.";
        }
        return false;
    }
}
