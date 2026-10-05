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
        return names;
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
