using Microsoft.Win32;
using System.IO.Ports;

namespace PCInfoScreenStudio.Services;

public sealed record SerialPortOption(
    string PortName,
    string FriendlyName,
    string HardwareId,
    string Manufacturer,
    int ScreenLikelihood)
{
    public bool IsLikelyScreen => ScreenLikelihood >= 2;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(FriendlyName) || FriendlyName.Equals(PortName, StringComparison.OrdinalIgnoreCase)
            ? PortName
            : $"{PortName}  ·  {FriendlyName}";
}

public sealed class SerialDeviceDiscoveryService
{
    private static readonly string[] EnumRoots =
    [
        @"SYSTEM\CurrentControlSet\Enum\USB",
        @"SYSTEM\CurrentControlSet\Enum\FTDIBUS",
        @"SYSTEM\CurrentControlSet\Enum\SERENUM"
    ];

    public IReadOnlyList<SerialPortOption> Discover()
    {
        var detected = new Dictionary<string, SerialPortOption>(StringComparer.OrdinalIgnoreCase);
        var presentPorts = SerialPort.GetPortNames()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            ReadRegistryView(RegistryView.Registry64, detected);
            ReadRegistryView(RegistryView.Registry32, detected);
        }

        // Enum registry entries can remain after a USB device is unplugged.
        // Only advertise ports that Windows currently reports as present.
        foreach (var stalePort in detected.Keys.Where(port => !presentPorts.Contains(port)).ToArray())
            detected.Remove(stalePort);

        foreach (var port in presentPorts)
        {
            if (!detected.ContainsKey(port))
                detected[port] = new SerialPortOption(port, "Serial port", string.Empty, string.Empty, 0);
        }

        return detected.Values
            .OrderByDescending(p => p.ScreenLikelihood)
            .ThenBy(p => ParsePortNumber(p.PortName))
            .ToArray();
    }

    public SerialPortOption? BestScreenCandidate()
        => Discover().FirstOrDefault(p => p.IsLikelyScreen);

    private static void ReadRegistryView(RegistryView view, IDictionary<string, SerialPortOption> output)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var rootPath in EnumRoots)
            {
                using var root = baseKey.OpenSubKey(rootPath);
                if (root is not null)
                    ScanKey(root, output, depth: 0);
            }
        }
        catch
        {
            // Device discovery is best-effort. SerialPort.GetPortNames is the fallback.
        }
    }

    private static void ScanKey(RegistryKey key, IDictionary<string, SerialPortOption> output, int depth)
    {
        if (depth > 4)
            return;

        try
        {
            using var parameters = key.OpenSubKey("Device Parameters");
            var portName = parameters?.GetValue("PortName") as string;

            if (!string.IsNullOrWhiteSpace(portName) &&
                portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                var friendly = CleanRegistryText(key.GetValue("FriendlyName") as string);
                var description = CleanRegistryText(key.GetValue("DeviceDesc") as string);
                var manufacturer = CleanRegistryText(key.GetValue("Mfg") as string);
                var hardware = ReadStringArray(key.GetValue("HardwareID"));

                if (string.IsNullOrWhiteSpace(friendly))
                    friendly = string.IsNullOrWhiteSpace(description) ? "USB serial device" : description;

                var score = Score(friendly, description, manufacturer, hardware);
                var candidate = new SerialPortOption(portName, friendly, hardware, manufacturer, score);

                if (!output.TryGetValue(portName, out var existing) ||
                    candidate.ScreenLikelihood > existing.ScreenLikelihood)
                {
                    output[portName] = candidate;
                }
            }

            foreach (var subName in key.GetSubKeyNames())
            {
                if (subName.Equals("Device Parameters", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var sub = key.OpenSubKey(subName);
                if (sub is not null)
                    ScanKey(sub, output, depth + 1);
            }
        }
        catch
        {
            // Ignore keys that disappear or are inaccessible during enumeration.
        }
    }

    private static int Score(params string[] values)
    {
        var text = string.Join(" ", values).ToUpperInvariant();
        var score = 0;

        if (text.Contains("USB35INCHIPSV2") ||
            text.Contains("VID_1A86&PID_5722"))
            score += 20;

        if (text.Contains("TURING") || text.Contains("TURZX") || text.Contains("USBMONITOR"))
            score += 8;

        if (text.Contains("USB-SERIAL") || text.Contains("USB SERIAL") || text.Contains("USB2.0-SERIAL"))
            score += 4;

        if (text.Contains("CH340") || text.Contains("CH341") || text.Contains("CH910") ||
            text.Contains("CP210") || text.Contains("FTDI") || text.Contains("USB CDC") ||
            text.Contains("USBSER"))
            score += 3;

        if (text.Contains("USB"))
            score += 1;

        if (text.Contains("SERIAL"))
            score += 1;

        if (text.Contains("BLUETOOTH") || text.Contains("STANDARD SERIAL") || text.Contains("COMMUNICATIONS PORT"))
            score -= 5;

        return score;
    }

    private static string CleanRegistryText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var semicolon = value.LastIndexOf(';');
        return semicolon >= 0 && semicolon + 1 < value.Length
            ? value[(semicolon + 1)..].Trim()
            : value.Trim();
    }

    private static string ReadStringArray(object? value)
        => value switch
        {
            string text => text,
            string[] values => string.Join(";", values),
            _ => string.Empty
        };

    private static int ParsePortNumber(string port)
        => int.TryParse(port.Replace("COM", "", StringComparison.OrdinalIgnoreCase), out var number)
            ? number
            : int.MaxValue;
}
