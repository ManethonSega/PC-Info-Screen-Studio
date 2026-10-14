// SPDX-License-Identifier: GPL-3.0-or-later
// Protocol based on mathoudebine/turing-smart-screen-python (Matthieu Houdebine)
// and usausa/turing-smart-screen. See THIRD_PARTY_NOTICES.md.
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PCInfoScreenStudio.Services.TuringUsb;

public static class TuringUsbProtocol
{
    public const int NativeWidth = 720;
    public const int NativeHeight = 1280;
    public const int MaxImageBytes = 1024 * 1024;
    public const string HardwareId = "VID_1CBE&PID_0050";
    private static readonly byte[] Key = "slv3tuzx"u8.ToArray();

    public static bool IsDevice(string? address)
        => address?.Contains(HardwareId, StringComparison.OrdinalIgnoreCase) == true;

    public static byte[] Packet(byte command, ReadOnlySpan<byte> arguments = default, int? milliseconds = null)
    {
        if (arguments.Length > 492) throw new ArgumentOutOfRangeException(nameof(arguments));
        var plain = new byte[504]; // 500-byte header, zero padded to an 8-byte DES block.
        plain[0] = command;
        plain[2] = 0x1A;
        plain[3] = 0x6D;
        BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(4, 4), milliseconds ?? (int)DateTime.Now.TimeOfDay.TotalMilliseconds);
        arguments.CopyTo(plain.AsSpan(8));
        var packet = new byte[512];
        using var des = DES.Create(); // Required by the device wire protocol, not used for data security.
        des.Key = Key;
        des.EncryptCbc(plain, Key, packet, PaddingMode.None);
        packet[510] = 0xA1;
        packet[511] = 0x1A;
        return packet;
    }

    public static byte[] ImagePacket(byte command, byte[] image)
    {
        if (command is not (101 or 102)) throw new ArgumentOutOfRangeException(nameof(command));
        if (image.Length == 0 || image.Length > MaxImageBytes) throw new ArgumentOutOfRangeException(nameof(image));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, image.Length);
        var result = new byte[512 + image.Length];
        Packet(command, length).CopyTo(result, 0);
        image.CopyTo(result, 512);
        return result;
    }

    public static bool ResponseSucceeded(ReadOnlySpan<byte> response)
        => response.Length >= 2 && (response[1] == 0xC8 || (response.Length > 8 && response[8] == 0xC8));
}

public interface ITuringUsbTransport : IDisposable
{
    void Open(string devicePath);
    byte[] Exchange(byte[] payload, CancellationToken cancellationToken);
}

public sealed class DisplaySetupException(string message) : IOException(message);
