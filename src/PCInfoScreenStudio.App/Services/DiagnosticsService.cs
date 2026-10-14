using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCInfoScreenStudio.Rendering;

namespace PCInfoScreenStudio.Services;

/// <summary>Explicit local export. No photos, theme names, file paths, account names or raw logs.</summary>
public static class DiagnosticsService
{
    private static readonly object Sync = new();
    private static readonly Queue<DiagnosticError> Errors = new();
    public sealed record DiagnosticError(DateTimeOffset TimeUtc, string Category, string ExceptionType, string HResult, VideoDecoderDetails? Video);
    public static void RecordError(string category, Exception error, VideoDecoderDetails? video = null)
    {
        lock (Sync)
        {
            Errors.Enqueue(new(DateTimeOffset.UtcNow, category, error.GetType().Name, $"0x{error.HResult:X8}", video));
            while (Errors.Count > 30) Errors.Dequeue();
        }
    }
    public static object CreateReport(object display, object runtime, string? startupLogPath = null)
    {
        using var process = Process.GetCurrentProcess();
        DiagnosticError[] errors; lock (Sync) errors = Errors.ToArray();
        return new {
            schemaVersion = 2, exportedUtc = DateTimeOffset.UtcNow,
            appVersion = typeof(DiagnosticsService).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion,
            windows = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            dotnet = RuntimeInformation.FrameworkDescription, display, runtime,
            memory = new { workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64 },
            videoPlayback = VideoFrameProvider.GetDiagnostics(),
            recentErrors = errors, previousStartupErrors = ReadStartupErrorTypes(startupLogPath),
            privacy = "No media, theme content, account names, personal paths or raw log text. Saved locally only; attach this file yourself if needed."
        };
    }
    private static string[] ReadStartupErrorTypes(string? startupLogPath)
    {
        try
        {
            var path = startupLogPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCInfoScreenStudio", "Logs", "startup.log");
            if (!File.Exists(path)) return [];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 32768) stream.Seek(-32768, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            // Export only exception class names, never messages/stack traces containing private paths.
            return Regex.Matches(reader.ReadToEnd(), @"(?m)^(System\.[A-Za-z0-9_.]*Exception)(?=:|$)").Select(m => m.Groups[1].Value).Distinct().Take(20).ToArray();
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

// Only application-controlled stages/codes and numeric decoder metadata, never file names or messages.
public sealed record VideoDecoderDetails(string Stage, string Code, int SourceWidth, int SourceHeight,
    int OutputWidth, int OutputHeight, int Stride, int FormatChanges, long PublishedFrames, int LastFlags);
