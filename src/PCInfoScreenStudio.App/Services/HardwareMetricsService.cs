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
                Status = result.Count > 0
                    ? $"Hardware sensors active ({sensors.Count} sensors)"
                    : "Hardware monitor opened, but no supported sensors were returned.";

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
            // One inaccessible device should not hide all other hardware sensors.
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
            Pick(sensors, IsCpu, "Temperature", ["package", "tctl", "tdie", "cpu", "core max"]),
            "°C");

        Add(output, "CPU.Power",
            Pick(sensors, IsCpu, "Power", ["package", "cpu package", "cores", "cpu"]),
            " W");

        Add(output, "CPU.Clock",
            Average(sensors, IsCpu, "Clock", s =>
                ContainsAny(s.SensorName, "core", "cpu") &&
                !ContainsAny(s.SensorName, "bus", "bclk", "effective")),
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

        Add(output, "GPU.VRAM",
            Pick(sensors, IsGpu, "Load", ["memory", "vram", "dedicated"]),
            "%");

        Add(output, "GPU.Power",
            Pick(sensors, IsGpu, "Power", ["gpu package", "total board", "board", "asic", "gpu"]),
            " W");

        Add(output, "GPU.FanRPM",
            Pick(sensors, IsGpu, "Fan", ["gpu", "fan"]),
            " RPM");

        var storageTemps = sensors
            .Where(s => IsStorage(s) &&
                        s.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            .Select(s => (double)s.Value)
            .Where(v => v > -30 && v < 150)
            .ToArray();

        if (storageTemps.Length > 0)
            output["Disk.Temperature"] = new MetricValue(storageTemps.Max(), Unit: "°C");

        Add(output, "Cooling.PumpRPM",
            Pick(sensors, IsCoolingHardware, "Fan", ["pump", "aio", "water"]),
            " RPM");

        var fan = sensors
            .Where(s => IsCoolingHardware(s) &&
                        s.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) &&
                        !ContainsAny(s.SensorName, "pump", "aio", "water"))
            .OrderByDescending(s => ScoreName(s.SensorName, ["cpu", "chassis", "system", "fan"]))
            .ThenByDescending(s => s.Value)
            .FirstOrDefault();

        if (fan is not null)
            output["Cooling.FanRPM"] = new MetricValue(fan.Value, Unit: " RPM");

        return output;
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

        var best = matches
            .OrderByDescending(s => ScoreName(s.SensorName, preferredNames))
            .ThenByDescending(s => s.Value)
            .First();

        return best.Value;
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
           sensor.RootType.Equals("Cooler", StringComparison.OrdinalIgnoreCase);

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
