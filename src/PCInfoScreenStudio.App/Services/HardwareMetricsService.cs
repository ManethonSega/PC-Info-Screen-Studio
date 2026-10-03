using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace PCInfoScreenStudio.Services;

public sealed class HardwareMetricsService : IDisposable
{
    private readonly object _sync = new();
    private Computer? _computer;
    private bool _disposed;

    public string Status { get; private set; } = "Not initialized";

    public IReadOnlyDictionary<string, MetricValue> Sample()
    {
        lock (_sync)
        {
            if (_disposed)
                return new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

            try
            {
                EnsureComputer();
                var sensors = new List<SensorSnapshot>();

                foreach (var hardware in _computer!.Hardware)
                    CollectHardware(hardware, hardware.HardwareType.ToString(), sensors);

                var result = BuildMetrics(sensors);
                var elevated = IsAdministrator();

                Status = sensors.Count > 0
                    ? $"Hardware sensors active ({sensors.Count} sensors{(elevated ? ", elevated" : ", standard access")})"
                    : elevated
                        ? "Hardware monitor opened, but no supported sensors were returned."
                        : "No low-level sensors available. Run as administrator for CPU/motherboard sensors.";

                return result;
            }
            catch (Exception ex)
            {
                Status = "Hardware sensors unavailable: " + ex.GetBaseException().Message;
                return new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void EnsureComputer()
    {
        if (_computer is not null)
            return;

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false,
            IsPowerMonitorEnabled = true
        };

        _computer.Open();
    }

    private static void CollectHardware(IHardware hardware, string rootType, ICollection<SensorSnapshot> output)
    {
        try
        {
            hardware.Update();
        }
        catch
        {
            // Keep the remaining hardware available even if one controller fails.
        }

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is float value && float.IsFinite(value))
            {
                output.Add(new SensorSnapshot(
                    RootType: rootType,
                    HardwareName: hardware.Name ?? string.Empty,
                    SensorName: sensor.Name ?? string.Empty,
                    SensorType: sensor.SensorType.ToString(),
                    Value: value));
            }
        }

        foreach (var subHardware in hardware.SubHardware)
            CollectHardware(subHardware, rootType, output);
    }

    private static IReadOnlyDictionary<string, MetricValue> BuildMetrics(IReadOnlyList<SensorSnapshot> sensors)
    {
        var output = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

        Add(output, "CPU.Temperature",
            Pick(sensors, IsCpu, "Temperature", ["package", "cpu package", "core max", "tctl", "tdie", "cpu"]),
            "°C");

        Add(output, "CPU.Power",
            Pick(sensors, IsCpu, "Power", ["package", "cpu package", "package power", "cores", "cpu"]),
            " W");

        Add(output, "CPU.Clock",
            Average(sensors, IsCpu, "Clock", s =>
                !ContainsAny(s.SensorName, "bus", "bclk") &&
                ContainsAny(s.SensorName, "core", "cpu", "effective")),
            " MHz");

        Add(output, "GPU.Usage",
            Pick(sensors, IsGpu, "Load", ["gpu core", "core", "d3d 3d", "3d", "gpu"]),
            "%");

        Add(output, "GPU.Temperature",
            Pick(sensors, IsGpu, "Temperature", ["gpu core", "core", "edge", "temperature"]),
            "°C");

        Add(output, "GPU.Hotspot",
            Pick(sensors, IsGpu, "Temperature", ["hot spot", "hotspot", "junction"]),
            "°C");

        var gpuMemoryPercent =
            Pick(sensors, IsGpu, "Load", ["gpu memory", "memory", "vram", "dedicated"]);

        if (gpuMemoryPercent is null)
        {
            var used = PickAnyType(sensors, IsGpu, ["SmallData", "Data"], ["gpu memory used", "dedicated memory used", "memory used"]);
            var total = PickAnyType(sensors, IsGpu, ["SmallData", "Data"], ["gpu memory total", "dedicated memory total", "memory total"]);

            if (used is double usedValue && total is double totalValue && totalValue > 0)
                gpuMemoryPercent = Math.Clamp(usedValue / totalValue * 100d, 0d, 100d);
        }

        Add(output, "GPU.VRAM", gpuMemoryPercent, "%");

        Add(output, "GPU.Power",
            Pick(sensors, IsGpu, "Power", ["gpu package", "total board", "board", "asic", "gpu power", "gpu"]),
            " W");

        Add(output, "GPU.FanRPM",
            Pick(sensors, IsGpu, "Fan", ["gpu fan", "fan"]),
            " RPM");

        var storageTemps = sensors
            .Where(s => IsStorage(s) &&
                        s.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            .Select(s => (double)s.Value)
            .Where(v => v > -30 && v < 150)
            .ToArray();

        if (storageTemps.Length > 0)
            output["Disk.Temperature"] = new MetricValue(storageTemps.Max(), Unit: "°C");

        var pump = sensors
            .Where(s => IsCoolingHardware(s) &&
                        s.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) &&
                        ContainsAny(s.SensorName, "pump", "aio", "water", "liquid"))
            .OrderByDescending(s => ScoreName(s.SensorName, ["aio pump", "pump", "water pump", "water", "liquid"]))
            .FirstOrDefault();

        if (pump is not null)
            output["Cooling.PumpRPM"] = new MetricValue(pump.Value, Unit: " RPM");

        var fans = sensors
            .Where(s => IsCoolingHardware(s) &&
                        s.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) &&
                        !ContainsAny(s.SensorName, "pump", "aio", "water", "liquid"))
            .OrderByDescending(s => ScoreName(s.SensorName, ["cpu fan", "cpu", "chassis", "system", "fan"]))
            .ThenBy(s => s.SensorName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (fans.Length > 0)
        {
            output["Cooling.FanRPM"] = new MetricValue(fans[0].Value, Unit: " RPM");

            for (var i = 0; i < Math.Min(6, fans.Length); i++)
                output[$"Cooling.Fan{i + 1}RPM"] = new MetricValue(fans[i].Value, Unit: " RPM");
        }

        // Expose every detected hardware sensor as an exact selectable source.
        // This avoids losing vendor-specific names such as ASUS AIO_PUMP, CHA_FAN3,
        // GPU Memory Used, CPU Package, etc.
        foreach (var sensor in sensors)
        {
            var key = BuildRawSourceKey(sensor);
            if (!output.ContainsKey(key))
                output[key] = new MetricValue(sensor.Value, Unit: UnitFor(sensor.SensorType, sensor.SensorName));
        }

        return output;
    }

    private static string BuildRawSourceKey(SensorSnapshot sensor)
        => $"Sensor: {sensor.RootType} / {Clean(sensor.HardwareName)} / {Clean(sensor.SensorName)} [{sensor.SensorType}]";

    private static string Clean(string value)
        => string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string UnitFor(string sensorType, string sensorName)
    {
        if (sensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase)) return "°C";
        if (sensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase)) return " RPM";
        if (sensorType.Equals("Clock", StringComparison.OrdinalIgnoreCase)) return " MHz";
        if (sensorType.Equals("Power", StringComparison.OrdinalIgnoreCase)) return " W";
        if (sensorType.Equals("Load", StringComparison.OrdinalIgnoreCase)) return "%";
        if (sensorType.Equals("Voltage", StringComparison.OrdinalIgnoreCase)) return " V";
        if (sensorType.Equals("Current", StringComparison.OrdinalIgnoreCase)) return " A";
        if (sensorType.Equals("Control", StringComparison.OrdinalIgnoreCase)) return "%";
        if (sensorType.Equals("Throughput", StringComparison.OrdinalIgnoreCase)) return " MB/s";
        if ((sensorType.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
             sensorType.Equals("SmallData", StringComparison.OrdinalIgnoreCase)) &&
            sensorName.Contains("memory", StringComparison.OrdinalIgnoreCase))
            return " MB";
        return string.Empty;
    }

    private static void Add(
        IDictionary<string, MetricValue> output,
        string key,
        double? value,
        string unit)
    {
        if (value is double number && double.IsFinite(number))
            output[key] = new MetricValue(number, Unit: unit);
    }

    private static double? Pick(
        IEnumerable<SensorSnapshot> sensors,
        Func<SensorSnapshot, bool> hardwareFilter,
        string sensorType,
        IReadOnlyList<string> preferredNames)
    {
        var matches = sensors
            .Where(s => hardwareFilter(s) &&
                        s.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return null;

        return matches
            .OrderByDescending(s => ScoreName(s.SensorName, preferredNames))
            .ThenByDescending(s => s.Value)
            .First()
            .Value;
    }

    private static double? PickAnyType(
        IEnumerable<SensorSnapshot> sensors,
        Func<SensorSnapshot, bool> hardwareFilter,
        IReadOnlyCollection<string> sensorTypes,
        IReadOnlyList<string> preferredNames)
    {
        var matches = sensors
            .Where(s => hardwareFilter(s) &&
                        sensorTypes.Any(type => s.SensorType.Equals(type, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matches.Length == 0)
            return null;

        return matches
            .OrderByDescending(s => ScoreName(s.SensorName, preferredNames))
            .ThenByDescending(s => s.Value)
            .First()
            .Value;
    }

    private static double? Average(
        IEnumerable<SensorSnapshot> sensors,
        Func<SensorSnapshot, bool> hardwareFilter,
        string sensorType,
        Func<SensorSnapshot, bool> sensorFilter)
    {
        var values = sensors
            .Where(s => hardwareFilter(s) &&
                        s.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase) &&
                        sensorFilter(s))
            .Select(s => (double)s.Value)
            .Where(double.IsFinite)
            .ToArray();

        return values.Length == 0 ? null : values.Average();
    }

    private static int ScoreName(string name, IReadOnlyList<string> preferences)
    {
        for (var i = 0; i < preferences.Count; i++)
        {
            if (name.Contains(preferences[i], StringComparison.OrdinalIgnoreCase))
                return preferences.Count - i;
        }

        return 0;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsCpu(SensorSnapshot sensor)
        => sensor.RootType.Equals("Cpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpu(SensorSnapshot sensor)
        => sensor.RootType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsStorage(SensorSnapshot sensor)
        => sensor.RootType.Equals("Storage", StringComparison.OrdinalIgnoreCase);

    private static bool IsCoolingHardware(SensorSnapshot sensor)
        => sensor.RootType.Equals("Motherboard", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("Controller", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("Cooler", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("SuperIO", StringComparison.OrdinalIgnoreCase) ||
           sensor.RootType.Equals("EmbeddedController", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;

            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }

    private sealed record SensorSnapshot(
        string RootType,
        string HardwareName,
        string SensorName,
        string SensorType,
        float Value);
}
