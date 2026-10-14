using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PCInfoScreenStudio.Services.TuringUsb;

/// <summary>Windows' built-in WinUSB API. No bundled native libusb DLL or driver replacement.</summary>
public sealed class WinUsbTransport : ITuringUsbTransport
{
    private SafeFileHandle? _file;
    private IntPtr _usb;
    private byte _input;
    private byte _output;
    private bool _drainInput;
    private static readonly Guid UsbDeviceGuid = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    public static IReadOnlyList<SerialPortOption> Discover()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var guids = new HashSet<Guid>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (root is not null)
            foreach (var deviceName in root.GetSubKeyNames().Where(TuringUsbProtocol.IsDevice))
            {
                using var device = root.OpenSubKey(deviceName);
                if (device is null) continue;
                foreach (var instance in device.GetSubKeyNames())
                {
                    using var parameters = device.OpenSubKey(instance + @"\Device Parameters");
                    foreach (var name in new[] { "DeviceInterfaceGUIDs", "DeviceInterfaceGUID" })
                    {
                        var values = parameters?.GetValue(name) switch
                        {
                            string[] multiple => multiple,
                            string single => new[] { single },
                            _ => Array.Empty<string>()
                        };
                        foreach (var value in values)
                            if (Guid.TryParse(value, out var guid)) guids.Add(guid);
                    }
                }
            }
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        // Prefer the driver's application interface over USB's generic interface.
        var paths = guids.Where(g => g != UsbDeviceGuid).SelectMany(EnumeratePaths)
            .Concat(EnumeratePaths(UsbDeviceGuid)).Where(TuringUsbProtocol.IsDevice);
        var devices = new Dictionary<string, SerialPortOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var parts = path.Split('#');
            var identity = parts.Length >= 3 ? parts[1] + "\\" + parts[2] : path;
            devices.TryAdd(identity, new SerialPortOption(path, "TURZX 5.2-inch USB (experimental)", identity, "TURZX", 100));
        }
        return devices.Values.ToArray();
    }

    private static IEnumerable<string> EnumeratePaths(Guid guid)
    {
        var info = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        if (info == new IntPtr(-1)) yield break;
        try
        {
            for (uint index = 0; ; index++)
            {
                var entry = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref guid, index, ref entry)) break;
                SetupDiGetDeviceInterfaceDetail(info, ref entry, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                if (required < 8) continue;
                var buffer = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(info, ref entry, buffer, required, out _, IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                        if (path is not null) yield return path;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(info); }
    }

    public void Open(string devicePath)
    {
        if (!TuringUsbProtocol.IsDevice(devicePath)) throw new ArgumentException("Not a TURZX 5.2-inch USB device.");
        Dispose();
        _file = CreateFile(devicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000080, IntPtr.Zero);
        if (_file.IsInvalid || !WinUsb_Initialize(_file, out _usb))
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            if (error is 5 or 32) throw new UnauthorizedAccessException("The TURZX display is busy.");
            throw new DisplaySetupException("The TURZX screen was found, but Windows did not provide WinUSB access. Close TURZX software and switch it back from Desktop Mode. Check its USB driver in Device Manager. The app does not replace drivers automatically.");
        }
        try
        {
            if (!WinUsb_QueryInterfaceSettings(_usb, 0, out var descriptor)) ThrowIo("inspect USB interface");
            for (byte index = 0; index < descriptor.EndpointCount; index++)
            {
                if (!WinUsb_QueryPipe(_usb, 0, index, out var pipe)) ThrowIo("inspect USB endpoints");
                if (pipe.Type != 2) continue; // bulk endpoints only
                if ((pipe.Id & 0x80) != 0) _input = pipe.Id;
                else _output = pipe.Id;
            }
            if (_input == 0 || _output == 0) throw new DisplaySetupException("The TURZX USB interface has no image-transfer endpoints. Switch the screen to smart-screen mode first.");
            SetTimeout(_input, 2000);
            SetTimeout(_output, 2000);
            _drainInput = true;
        }
        catch { Dispose(); throw; }
    }

    public byte[] Exchange(byte[] payload, CancellationToken cancellationToken)
    {
        if (_usb == IntPtr.Zero) throw new IOException("The USB display is disconnected.");
        cancellationToken.ThrowIfCancellationRequested();
        var response = new byte[512];
        if (_drainInput)
        {
            SetTimeout(_input, 20);
            try
            {
                for (var i = 0; i < 4; i++)
                    if (!WinUsb_ReadPipe(_usb, _input, response, 512, out _, IntPtr.Zero)) break;
            }
            finally { SetTimeout(_input, 2000); }
            _drainInput = false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!WinUsb_WritePipe(_usb, _output, payload, (uint)payload.Length, out var written, IntPtr.Zero)) ThrowIo("send USB frame");
        if (written != payload.Length) throw new IOException("Incomplete USB frame transfer.");
        // A bulk transfer can end with an empty packet. It is not a failed ACK.
        var started = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = 2000 - started.ElapsedMilliseconds;
            if (remaining <= 0) break;
            SetTimeout(_input, (uint)remaining);
            if (!WinUsb_ReadPipe(_usb, _input, response, 512, out var read, IntPtr.Zero)) ThrowIo("read USB response");
            if (read > 0) return response.AsSpan(0, (int)read).ToArray();
        }
        throw new IOException("The TURZX display returned no USB acknowledgement.");
    }

    private void SetTimeout(byte endpoint, uint timeout)
    {
        if (!WinUsb_SetPipePolicy(_usb, endpoint, 3, 4, ref timeout)) ThrowIo("set USB timeout");
    }

    private static void ThrowIo(string action)
        => throw new IOException("Could not " + action + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);

    public void Dispose()
    {
        if (_usb != IntPtr.Zero) { WinUsb_Free(_usb); _usb = IntPtr.Zero; }
        _file?.Dispose(); _file = null; _input = _output = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public int Size; public Guid Guid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct InterfaceDescriptor
    {
        public byte Length, DescriptorType, Number, Alternate, EndpointCount, Class, SubClass, Protocol, Interface;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PipeInfo { public int Type; public byte Id; public ushort MaxPacketSize; public byte Interval; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr info, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Initialize(SafeFileHandle file, out IntPtr usb);
    [DllImport("winusb.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Free(IntPtr usb);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryInterfaceSettings(IntPtr usb, byte alternate, out InterfaceDescriptor descriptor);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryPipe(IntPtr usb, byte alternate, byte index, out PipeInfo pipe);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_SetPipePolicy(IntPtr usb, byte pipe, uint policy, uint length, ref uint value);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_WritePipe(IntPtr usb, byte pipe, byte[] data, uint length, out uint written, IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_ReadPipe(IntPtr usb, byte pipe, byte[] data, uint length, out uint read, IntPtr overlapped);
}
