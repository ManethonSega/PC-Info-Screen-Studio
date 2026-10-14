using System.Management;

namespace PCInfoScreenStudio.Services;

/// <summary>Reads hardware names once. Live values continue to use the existing samplers.</summary>
public static class HardwareInventoryService
{
    public static IReadOnlyDictionary<string, MetricValue> Read()
    {
        var names = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);
        ReadNames("Win32_Processor", "Name", "Hardware.CPUName", names);
        ReadNames("Win32_VideoController", "Name", "Hardware.GPUName", names);
        ReadNames("Win32_DiskDrive", "Model", "Hardware.StorageNames", names);
        ReadMemoryNames(names);
        return names;
    }

    private static void ReadMemoryNames(IDictionary<string, MetricValue> output)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var query = new ManagementObjectSearcher("SELECT Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            query.Options.Timeout = TimeSpan.FromSeconds(3);
            using var objects = query.Get();
            var modules = new List<(string? Manufacturer, string? PartNumber)>();
            foreach (ManagementObject item in objects)
                using (item) modules.Add((item["Manufacturer"]?.ToString(), item["PartNumber"]?.ToString()));
            var name = FormatMemoryNames(modules);
            if (!string.IsNullOrWhiteSpace(name)) output["Hardware.RAMName"] = new MetricValue(Text: name);
        }
        catch { /* Missing firmware information is not a reason to invent a brand. */ }
    }

    public static string FormatMemoryNames(IEnumerable<(string? Manufacturer, string? PartNumber)> modules)
        => string.Join(" · ", modules.Select(module =>
        {
            var maker = CleanIdentity(module.Manufacturer);
            var part = CleanIdentity(module.PartNumber);
            return string.IsNullOrEmpty(maker) || part.StartsWith(maker, StringComparison.OrdinalIgnoreCase)
                ? part : string.IsNullOrEmpty(part) ? maker : maker + " " + part;
        }).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string CleanIdentity(string? value)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length == 0 || clean.All(c => c == '0' || char.IsWhiteSpace(c)) ||
            new[] { "Unknown", "Not Specified", "Undefined", "Default string", "N/A", "To Be Filled By O.E.M." }
                .Contains(clean, StringComparer.OrdinalIgnoreCase) ? string.Empty : clean;
    }

    private static void ReadNames(string className, string property, string key, IDictionary<string, MetricValue> output)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var query = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            query.Options.Timeout = TimeSpan.FromSeconds(3);
            using var objects = query.Get();
            var names = new List<string>();
            foreach (ManagementObject item in objects)
            {
                using (item)
                {
                    var name = item[property]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }
            if (names.Count > 0)
                output[key] = new MetricValue(Text: string.Join(" · ", names.Distinct(StringComparer.OrdinalIgnoreCase)));
        }
        catch
        {
            // Names are optional. Missing WMI access must not block the dashboard.
        }
    }
}
