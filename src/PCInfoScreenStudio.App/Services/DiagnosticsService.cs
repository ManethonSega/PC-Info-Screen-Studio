using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCInfoScreenStudio.Services;

/// <summary>Explicit local export. No photos, theme names, file paths, account names or raw logs.</summary>
public static class DiagnosticsService
{
    private static readonly object Sync = new();
    private static readonly Queue<DiagnosticError> Errors = new();
    public sealed record DiagnosticError(DateTimeOffset TimeUtc, string Category, string ExceptionType, string HResult);
    public static void RecordError(string category, Exception error)
    {
        lock (Sync)
        {
            Errors.Enqueue(new(DateTimeOffset.UtcNow, category, error.GetType().Name, $"0x{error.HResult:X8}"));
            while (Errors.Count > 30) Errors.Dequeue();
        }
    }
    public static object CreateReport(object display, object runtime)
    {
        using var process = Process.GetCurrentProcess();
        DiagnosticError[] errors; lock (Sync) errors = Errors.ToArray();
        return new {
            schemaVersion = 1, exportedUtc = DateTimeOffset.UtcNow,
            appVersion = typeof(DiagnosticsService).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
            windows = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            dotnet = RuntimeInformation.FrameworkDescription, display, runtime,
            memory = new { workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64 },
            recentErrors = errors, previousStartupErrors = ReadStartupErrorTypes(),
            privacy = "No media, theme content, account names, personal paths or raw log text. Saved locally only; attach this file yourself if needed."
        };
    }
    private static string[] ReadStartupErrorTypes()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCInfoScreenStudio", "Logs", "startup.log");
            if (!File.Exists(path)) return [];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 32768) stream.Seek(-32768, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            // Export only exception class names, never messages/stack traces containing private paths.
            return Regex.Matches(reader.ReadToEnd(), @"\b(?:System\.)[A-Za-z0-9_.]*Exception\b").Select(m => m.Value).Distinct().Take(20).ToArray();
        }
        catch { return ["Startup log unavailable"]; }
    }
    public static async Task ExportAsync(string destination, object report)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
