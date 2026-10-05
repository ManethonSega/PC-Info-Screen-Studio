using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PCInfoScreenStudio.Services;

/// <summary>A per-user, interactive logon task. Windows owns approval and the access token; no passwords are saved.</summary>
public sealed class WindowsStartupService
{
    public const string StartupArgument = "--windows-startup";
    public const string ScheduledArgument = "--scheduled-startup";
    public const string ConfigureArgument = "--configure-windows-startup";
    private static readonly XNamespace Schema = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    public static string CurrentUserSid => WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("Could not identify the signed-in Windows account.");
    public static string TaskName(string sid) => "PC Info Screen Studio - " + new SecurityIdentifier(sid).Value;

    public static string BuildTaskXml(string sid, string executable, string entryPoint)
    {
        _ = new SecurityIdentifier(sid);
        var arguments = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? QuoteArgument(entryPoint) + " " + StartupArgument + " " + ScheduledArgument : StartupArgument + " " + ScheduledArgument;
        XElement E(string name, object value) => new(Schema + name, value);
        return new XDocument(new XElement(Schema + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", "Start PC Info Screen Studio in the tray and resume the display for this account.")),
            E("Principals", new XElement(Schema + "Principal", new XAttribute("id", "User"),
                E("UserId", sid), E("LogonType", "InteractiveToken"), E("RunLevel", "HighestAvailable"))),
            E("Settings", new object[] {
                E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", "false"),
                E("StopIfGoingOnBatteries", "false"), E("AllowHardTerminate", "false"), E("StartWhenAvailable", "true"),
                E("Enabled", "true"), E("Hidden", "false"), E("ExecutionTimeLimit", "PT0S"), E("Priority", "7") }),
            new XElement(Schema + "Actions", new XAttribute("Context", "User"),
                E("Exec", new object[] { E("Command", executable), E("Arguments", arguments),
                    E("WorkingDirectory", Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory) })))).ToString();
    }

    // EntryPoint is an absolute file path; Windows file names cannot contain a quote.
    private static string QuoteArgument(string value) => "\"" + value + "\"";

    public bool IsConfiguredForCurrentLocation(bool includeStartupEntry = true)
    {
        try
        {
            if (includeStartupEntry && !HasStartupEntry()) return false;
            return WithFolder<bool>(folder => {
                dynamic task = folder.GetTask(TaskName(CurrentUserSid));
                try
                {
                    return (bool)task.Enabled && MatchesTaskXml((string)task.Xml, CurrentUserSid, Executable(), EntryPoint());
                }
                finally { Marshal.FinalReleaseComObject(task); }
            });
        }
        catch { return false; }
    }

    public static bool MatchesTaskXml(string xml, string sid, string executable, string entryPoint)
    {
        var actual = XDocument.Parse(xml);
        var expected = XDocument.Parse(BuildTaskXml(sid, executable, entryPoint));
        var actions = actual.Root?.Element(Schema + "Actions");
        var exec = actions?.Element(Schema + "Exec");
        var target = expected.Descendants(Schema + "Exec").Single();
        return actions?.Elements().Count() == 1 && exec is not null &&
            string.Equals(exec.Element(Schema + "Command")?.Value, target.Element(Schema + "Command")?.Value, StringComparison.OrdinalIgnoreCase) &&
            exec.Element(Schema + "Arguments")?.Value == target.Element(Schema + "Arguments")?.Value &&
            string.Equals(exec.Element(Schema + "WorkingDirectory")?.Value, target.Element(Schema + "WorkingDirectory")?.Value, StringComparison.OrdinalIgnoreCase) &&
            actual.Descendants(Schema + "RunLevel").SingleOrDefault()?.Value == "HighestAvailable" &&
            actual.Descendants(Schema + "LogonType").SingleOrDefault()?.Value == "InteractiveToken" &&
            actual.Descendants(Schema + "Principal").SingleOrDefault()?.Element(Schema + "UserId")?.Value == sid &&
            !actual.Descendants(Schema + "Triggers").Any(t => t.HasElements);
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        using var hardware = new HardwareMetricsService();
        if (hardware.IsElevated)
        {
            await Task.Run(() => Configure(enabled, CurrentUserSid));
            SetStartupEntry(enabled);
            return true;
        }
        var start = new ProcessStartInfo(Executable()) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(EntryPoint());
        start.ArgumentList.Add(ConfigureArgument);
        start.ArgumentList.Add(enabled ? "enable" : "disable");
        start.ArgumentList.Add(CurrentUserSid);
        try
        {
            using var process = Process.Start(start);
            if (process is null) throw new InvalidOperationException("Windows did not start startup configuration.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException("Windows could not configure startup. Try running the app as administrator, then enable Start with Windows again.");
            SetStartupEntry(enabled);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; }
    }

    public void Configure(bool enabled, string sid)
    {
        WithFolder<bool>(folder => {
            var name = TaskName(sid);
            if (!enabled)
            {
                try { folder.DeleteTask(name, 0); }
                catch (COMException ex) when (ex.HResult is unchecked((int)0x80070002) or unchecked((int)0x8004130F)) { }
                return true;
            }
            // HighestAvailable is the user's own token, not SYSTEM or a stored administrator password.
            dynamic registered = folder.RegisterTask(name, BuildTaskXml(sid, Executable(), EntryPoint()), 6, sid, null, 3, null);
            Marshal.FinalReleaseComObject(registered);
            return true;
        });
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string StartupCommand() => QuoteArgument(Executable()) + " " +
        (Path.GetFileNameWithoutExtension(Executable()).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? QuoteArgument(EntryPoint()) + " " : "") + StartupArgument;
    private static bool HasStartupEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue("PC Info Screen Studio") as string, StartupCommand(), StringComparison.OrdinalIgnoreCase);
    }
    private static void SetStartupEntry(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("PC Info Screen Studio", StartupCommand(), RegistryValueKind.String);
        else key.DeleteValue("PC Info Screen Studio", throwOnMissingValue: false);
    }

    public bool LaunchApprovedStartupTask()
    {
        if (!IsConfiguredForCurrentLocation(includeStartupEntry: false)) return false;
        try
        {
            return WithFolder<bool>(folder => {
                dynamic task = folder.GetTask(TaskName(CurrentUserSid));
                try
                {
                    dynamic running = task.Run(null);
                    if (running is not null) Marshal.FinalReleaseComObject(running);
                    return true;
                }
                finally { Marshal.FinalReleaseComObject(task); }
            });
        }
        catch { return false; }
    }

    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
        dynamic service = Activator.CreateInstance(type)!;
        dynamic? folder = null;
        try { service.Connect(); folder = service.GetFolder("\\"); return action(folder); }
        finally
        {
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            Marshal.FinalReleaseComObject(service);
        }
    }

    private static string Executable() => Environment.ProcessPath ?? throw new InvalidOperationException("Could not locate the app executable.");
    private static string EntryPoint() => Environment.GetCommandLineArgs()[0];
}
