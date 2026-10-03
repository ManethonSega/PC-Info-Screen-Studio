using System.Buffers;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Tedd.TuringScreen;

public sealed class TuringScreen : IDisposable
{
    // ########################################################################
    // 1. CONFIGURATION
    // ########################################################################

    private const int HwWidth = 320;
    private const int HwHeight = 480;
    private const int HeuristicCostPerPixel = 12;

    // Revision-A reference implementations send image payloads in four-row
    // chunks (display width * 8 bytes). Favor reliability first; this can be
    // made adaptive after the physical display benchmark is stable.
    private const int MaxBlockHeight = 4;
    // BENCHMARK CONFIGURATION:
    private static readonly int[] BenchmarkSteps =
        { 1000, 1200, 1400, 1500, 1600, 1700, 1800, 2000, 2500 };

    // PROTOCOL COMMANDS
    private const byte CmdHello = 69;
    private const byte CmdReset = 101;
    private const byte CmdClear = 102;
    private const byte CmdScreenOff = 108;
    private const byte CmdScreenOn = 109;
    private const byte CmdBrightness = 110;
    private const byte CmdOrientation = 121;
    private const byte CmdDraw = 197;

    // ########################################################################
    // 2. STATE
    // ########################################################################

    private readonly int _comPortName;
    private readonly int _baudRate;
    private SerialPort? _port;
    private Stream? _baseStream; // Optimization: Cache the base stream

    private readonly byte[] _commandBuffer = new byte[16];
    private ScreenBuffer _screenBuffer;

    private int _cachedWidth;
    private int _cachedHeight;
    private bool _useSoftwareRotation;

    private int _lastBrightness = 100;
    private byte _lastOrientationIndex = 0;
    private RevACompatibilityMode _compatibilityMode;
    private Rgb565Encoding _pixelEncoding;

    // ########################################################################
    // 3. PUBLIC API
    // ########################################################################

    public ScreenOrientation Orientation { get; private set; } = ScreenOrientation.Portrait;
    public int Width => _cachedWidth;
    public int Height => _cachedHeight;
    public string DetectedModel { get; private set; } = "Turing 3.5-inch";
    public RevACompatibilityMode CompatibilityMode => _compatibilityMode;
    public Rgb565Encoding PixelEncoding => _pixelEncoding;

    public TuringScreen(
        int comPort,
        int baudRate = 921600,
        RevACompatibilityMode compatibilityMode = RevACompatibilityMode.HardwareLogicalDimensions,
        Rgb565Encoding pixelEncoding = Rgb565Encoding.RgbLittleEndian)
    {
        _comPortName = comPort;
        _baudRate = baudRate;
        _compatibilityMode = compatibilityMode;
        _pixelEncoding = pixelEncoding;
        _screenBuffer = new ScreenBuffer(HwWidth, HwHeight);
        _cachedWidth = HwWidth;
        _cachedHeight = HwHeight;
        Connect();
    }

    public void Dispose() => Close();

    public void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        var color = ScreenBuffer.FullRgbToColor565(r, g, b);
        _screenBuffer[x, y] = color;
        WritePixelImmediate(CmdDraw, x, y, color);
    }

    public void DisplayBuffer(int x, int y, ScreenBuffer buffer)
    {
        if (_compatibilityMode == RevACompatibilityMode.NativePortraitSoftwareRotation)
        {
            SendFullEncodedFrame(buffer, rotateLandscapeToNativePortrait: true);
            return;
        }

        if (_pixelEncoding != Rgb565Encoding.RgbLittleEndian)
        {
            SendFullEncodedFrame(buffer, rotateLandscapeToNativePortrait: false);
            return;
        }

        WriteSmartCommand(CmdDraw, x, y, buffer.Width, buffer.Height, buffer.Buffer);
    }

    public void ConfigureCompatibility(RevACompatibilityMode compatibilityMode, Rgb565Encoding pixelEncoding)
    {
        _compatibilityMode = compatibilityMode;
        _pixelEncoding = pixelEncoding;
        SetOrientation(Orientation);
    }

    public void Clear()
    {
        if (_compatibilityMode == RevACompatibilityMode.NativePortraitSoftwareRotation)
        {
            WriteCommand(CmdClear);
            _screenBuffer.Clear(Color656.White);
            return;
        }

        // Some Revision-A firmware only clears correctly in portrait mode.
        var restore = Orientation;
        if (restore != ScreenOrientation.Portrait)
            WriteOrientationCommand(CmdOrientation, (byte)ScreenOrientation.Portrait);

        WriteCommand(CmdClear);

        if (restore != ScreenOrientation.Portrait)
            WriteOrientationCommand(CmdOrientation, (byte)restore);

        _screenBuffer.Clear(Color656.White);
    }

    public void Reset()
    {
        WriteCommand(CmdReset);
        Close();

        // Revision-A 3.5" UsbMonitor/Turing devices re-enumerate after reset.
        // Give the controller time to restart before reopening the COM port.
        Thread.Sleep(5000);
        Connect(waitForConnect: 5000);
    }

    public void InitializeComm()
    {
        if (_port is null || !_port.IsOpen)
            throw new IOException("Display serial port is not open.");

        var hello = new byte[] { CmdHello, CmdHello, CmdHello, CmdHello, CmdHello, CmdHello };
        _port.DiscardInBuffer();
        _port.Write(hello, 0, hello.Length);

        try
        {
            var response = new byte[6];
            var read = 0;
            var deadline = Stopwatch.StartNew();
            while (read < response.Length && deadline.ElapsedMilliseconds < 450)
            {
                try
                {
                    var count = _port.Read(response, read, response.Length - read);
                    if (count <= 0) break;
                    read += count;
                }
                catch (TimeoutException)
                {
                    break;
                }
            }

            if (read == 6 && response.All(b => b == 1))
                DetectedModel = "UsbMonitor 3.5-inch";
            else if (read == 6 && response.All(b => b == 2))
                DetectedModel = "UsbMonitor 5-inch";
            else if (read == 6 && response.All(b => b == 3))
                DetectedModel = "UsbMonitor 7-inch";
            else
                DetectedModel = "Turing 3.5-inch";
        }
        finally
        {
            try { _port.DiscardInBuffer(); } catch { }
        }
    }

    public void ScreenOff() => WriteCommand(CmdScreenOff);
    public void ScreenOn() => WriteCommand(CmdScreenOn);

    public void SetBrightness(int level)
    {
        level = Math.Clamp(level, 0, 100);
        _lastBrightness = level;

        // The device protocol uses 0 as brightest and 255 as darkest.
        var protocolLevel = 255 - (int)Math.Round(level / 100d * 255d);
        WriteCommand(CmdBrightness, protocolLevel);
    }

    public void SetOrientation(ScreenOrientation orientation)
    {
        Orientation = orientation;

        if (orientation is ScreenOrientation.Landscape or ScreenOrientation.ReverseLandscape)
        {
            _cachedWidth = HwHeight;
            _cachedHeight = HwWidth;
        }
        else
        {
            _cachedWidth = HwWidth;
            _cachedHeight = HwHeight;
        }

        _useSoftwareRotation = false;
        _lastOrientationIndex = (byte)orientation;

        // The native-portrait compatibility mode deliberately avoids command
        // 121. The logical 480x320 frame is rotated to the controller's native
        // 320x480 portrait framebuffer immediately before transmission.
        if (_compatibilityMode != RevACompatibilityMode.NativePortraitSoftwareRotation)
            WriteOrientationCommand(CmdOrientation, _lastOrientationIndex);

        _screenBuffer = new ScreenBuffer(_cachedWidth, _cachedHeight);
        Clear();
    }

    // ########################################################################
    // 4. RENDERING LOGIC (AVX2)
    // ########################################################################

    private void WriteSmartCommand(byte command, int left, int top, int width, int height, byte[] data)
    {
        var sourceSpan = MemoryMarshal.Cast<byte, ushort>(data.AsSpan());
        var bufferSpan = MemoryMarshal.Cast<byte, ushort>(_screenBuffer.Buffer.AsSpan());

        int screenWidth = _cachedWidth;
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        int changeCount = 0;

        bool useAvx2 = Avx2.IsSupported;

        ref ushort sourceHead = ref MemoryMarshal.GetReference(sourceSpan);
        ref ushort bufferHead = ref MemoryMarshal.GetReference(bufferSpan);

        // --- 1. DIFF STEP ---
        for (int y = 0; y < height; y++)
        {
            int globalY = top + y;
            int rowOffsetSrc = y * width;
            int rowOffsetDst = globalY * screenWidth + left;

            ref ushort rowSrc = ref Unsafe.Add(ref sourceHead, rowOffsetSrc);
            ref ushort rowDst = ref Unsafe.Add(ref bufferHead, rowOffsetDst);

            int x = 0;

            // AVX2 Vectorized Comparison
            if (useAvx2 && width >= 16)
            {
                int vecLimit = width - 16;
                for (; x <= vecLimit; x += 16)
                {
                    Vector256<short> vSrc = Vector256.LoadUnsafe(ref Unsafe.As<ushort, short>(ref Unsafe.Add(ref rowSrc, x)));
                    Vector256<short> vDst = Vector256.LoadUnsafe(ref Unsafe.As<ushort, short>(ref Unsafe.Add(ref rowDst, x)));

                    // CompareEqual returns 0xFFFF for equal, 0x0000 for not equal
                    Vector256<short> vEq = Avx2.CompareEqual(vSrc, vDst);
                    int mask = Avx2.MoveMask(vEq.AsByte());

                    if (mask == -1) continue; // All bytes identical

                    // Identify differing pixels
                    int diffMask = ~mask;
                    while (diffMask != 0)
                    {
                        int bitIndex = BitOperations.TrailingZeroCount(diffMask);
                        int pixelIdx = bitIndex / 2;
                        int px = x + pixelIdx;

                        changeCount++;
                        if (px < minX) minX = px;
                        if (px > maxX) maxX = px;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;

                        // Clear the 2 bits for this pixel so we find the next one
                        diffMask &= ~(3 << (pixelIdx * 2));
                    }
                }
            }

            // Scalar Cleanup
            for (; x < width; x++)
            {
                if (Unsafe.Add(ref rowDst, x) != Unsafe.Add(ref rowSrc, x))
                {
                    changeCount++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (changeCount == 0) return;

        // --- 2. DECISION STEP ---
        int diffW = maxX - minX + 1;
        int diffH = maxY - minY + 1;
        long boxCost = 6 + (diffW * diffH * 2);
        long pointCost = changeCount * HeuristicCostPerPixel;

        var sourcePixels = MemoryMarshal.Cast<ushort, Color656>(sourceSpan);
        var bufferPixels = MemoryMarshal.Cast<ushort, Color656>(bufferSpan);

        if (pointCost < boxCost)
        {
            // STRATEGY A: Pixel-by-Pixel (Sparse)
            for (int y = 0; y < height; y++)
            {
                int globalY = top + y;
                int rowOffsetSrc = y * width;
                int rowOffsetDst = globalY * screenWidth + left;

                for (int x = 0; x < width; x++)
                {
                    var newVal = sourcePixels[rowOffsetSrc + x];
                    if (bufferPixels[rowOffsetDst + x] != newVal)
                    {
                        bufferPixels[rowOffsetDst + x] = newVal;
                        WritePixelImmediate(command, left + x, globalY, newVal);
                    }
                }
            }
        }
        else
        {
            // STRATEGY B: Dirty Rectangle (Bulk)

            // Sync Backbuffer
            for (int y = minY; y <= maxY; y++)
            {
                int globalY = top + y;
                int rowOffsetSrc = y * width;
                int rowOffsetDst = globalY * screenWidth + left;

                var srcSlice = sourcePixels.Slice(rowOffsetSrc + minX, diffW);
                var dstSlice = bufferPixels.Slice(rowOffsetDst + minX, diffW);
                srcSlice.CopyTo(dstSlice);
            }

            // Transmit with Tiling
            int currentY = minY;
            int remainingH = diffH;

            while (remainingH > 0)
            {
                int tileH = Math.Min(remainingH, MaxBlockHeight);

                if (_useSoftwareRotation)
                {
                    SendRotatedPayload(command, left + minX, top + currentY, diffW, tileH, sourcePixels, width);
                }
                else
                {
                    SendRectangularUpdate(command, left + minX, top + currentY, diffW, tileH, sourcePixels, width, minX, currentY);
                }

                currentY += tileH;
                remainingH -= tileH;
            }
        }
    }

    private void SendRotatedPayload(byte command, int logX, int logY, int logW, int logH, ReadOnlySpan<Color656> sourceData, int sourceStride)
    {
        int physX = logY; int physY = logX;
        int physW = logH; int physH = logW;
        int payloadSize = physW * physH * 2;

        byte[] rent = ArrayPool<byte>.Shared.Rent(payloadSize);

        try
        {
            var packed = MemoryMarshal.Cast<byte, Color656>(rent.AsSpan(0, payloadSize));
            ref Color656 packedHead = ref MemoryMarshal.GetReference(packed);
            ref Color656 sourceHead = ref MemoryMarshal.GetReference(sourceData);
            int pIndex = 0;

            // Software Rotation: Transpose logical X/Y to physical Y/X
            for (int row = 0; row < physH; row++)
            {
                int lx = logX + row;
                for (int col = 0; col < physW; col++)
                {
                    int srcIdx = (logY + col) * sourceStride + lx;
                    Unsafe.Add(ref packedHead, pIndex++) = Unsafe.Add(ref sourceHead, srcIdx);
                }
            }

            PrepareHeader(command, physX, physY, physW, physH);
            // Optimization: Write header + payload in one call? 
            // Better to just ensure Stream handles it.
            SafeWrite(_commandBuffer, 6, rent, payloadSize);
        }
        finally { ArrayPool<byte>.Shared.Return(rent); }
    }

    private void SendRectangularUpdate(byte command, int x, int y, int w, int h, ReadOnlySpan<Color656> fullSource, int fullWidth, int offX, int offY)
    {
        int payloadSize = w * h * 2;
        byte[] rent = ArrayPool<byte>.Shared.Rent(payloadSize);
        try
        {
            // Block Copy row by row
            var packed = MemoryMarshal.Cast<byte, Color656>(rent.AsSpan(0, payloadSize));
            for (int row = 0; row < h; row++)
            {
                var src = fullSource.Slice((offY + row) * fullWidth + offX, w);
                var dst = packed.Slice(row * w, w);
                src.CopyTo(dst);
            }
            PrepareHeader(command, x, y, w, h);
            SafeWrite(_commandBuffer, 6, rent, payloadSize);
        }
        finally { ArrayPool<byte>.Shared.Return(rent); }
    }

    private void WritePixelImmediate(byte command, int x, int y, Color656 color)
    {
        int hwX = x; int hwY = y;
        if (_useSoftwareRotation) { hwX = y; hwY = x; }

        int ex = hwX; int ey = hwY;
        _commandBuffer[0] = (byte)(hwX >> 2);
        _commandBuffer[1] = (byte)(((hwX & 3) << 6) + (hwY >> 4));
        _commandBuffer[2] = (byte)(((hwY & 15) << 4) + (ex >> 6));
        _commandBuffer[3] = (byte)(((ex & 63) << 2) + (ey >> 8));
        _commandBuffer[4] = (byte)(ey & 255);
        _commandBuffer[5] = command;
        MemoryMarshal.Write(_commandBuffer.AsSpan(6, 2), in color);
        SafeWrite(_commandBuffer, 8);
    }

    private void SendFullEncodedFrame(ScreenBuffer buffer, bool rotateLandscapeToNativePortrait)
    {
        var source = MemoryMarshal.Cast<byte, ushort>(buffer.Buffer.AsSpan());

        int physicalWidth;
        int physicalHeight;
        byte[] payload;

        if (rotateLandscapeToNativePortrait && buffer.Width == HwHeight && buffer.Height == HwWidth)
        {
            // Same transform used by proven USB35INCHIPSV2 implementations:
            // 480x320 logical landscape -> clockwise rotation -> 320x480 native.
            physicalWidth = HwWidth;
            physicalHeight = HwHeight;
            payload = new byte[physicalWidth * physicalHeight * 2];

            for (var dy = 0; dy < physicalHeight; dy++)
            {
                for (var dx = 0; dx < physicalWidth; dx++)
                {
                    var sx = dy;
                    var sy = buffer.Height - 1 - dx;
                    var rgb565 = source[sy * buffer.Width + sx];
                    WriteEncodedPixel(payload, (dy * physicalWidth + dx) * 2, rgb565);
                }
            }
        }
        else
        {
            physicalWidth = buffer.Width;
            physicalHeight = buffer.Height;
            payload = new byte[physicalWidth * physicalHeight * 2];

            for (var i = 0; i < source.Length; i++)
                WriteEncodedPixel(payload, i * 2, source[i]);
        }

        PrepareHeader(CmdDraw, 0, 0, physicalWidth, physicalHeight);
        WriteFrameWithChunks(_commandBuffer, 6, payload, physicalWidth * MaxBlockHeight * 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteEncodedPixel(byte[] destination, int offset, ushort rgb565)
    {
        var value = rgb565;

        if (_pixelEncoding is Rgb565Encoding.BgrLittleEndian or Rgb565Encoding.BgrBigEndian)
        {
            value = (ushort)(
                ((rgb565 & 0x001F) << 11) |
                (rgb565 & 0x07E0) |
                ((rgb565 & 0xF800) >> 11));
        }

        if (_pixelEncoding is Rgb565Encoding.RgbBigEndian or Rgb565Encoding.BgrBigEndian)
        {
            destination[offset] = (byte)(value >> 8);
            destination[offset + 1] = (byte)value;
        }
        else
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
        }
    }

    private void WriteFrameWithChunks(byte[] header, int headerLength, byte[] payload, int chunkSize)
    {
        try
        {
            WriteFrameWithChunksRaw(header, headerLength, payload, chunkSize);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (!RecoverConnection())
                throw new IOException("The display disconnected while sending a frame.", ex);

            WriteFrameWithChunksRaw(header, headerLength, payload, chunkSize);
        }
    }

    private void WriteFrameWithChunksRaw(byte[] header, int headerLength, byte[] payload, int chunkSize)
    {
        if (_port is null || !_port.IsOpen)
            throw new IOException("Disconnected");

        _port.Write(header, 0, headerLength);

        chunkSize = Math.Max(2, chunkSize);
        for (var offset = 0; offset < payload.Length; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, payload.Length - offset);
            _port.Write(payload, offset, count);
        }
    }

    // ########################################################################
    // 5. I/O OPTIMIZATION (THE FIX)
    // ########################################################################

    private void PrepareHeader(byte command, int x, int y, int w, int h)
    {
        var ex = x + w - 1;
        var ey = y + h - 1;
        _commandBuffer[0] = (byte)(x >> 2);
        _commandBuffer[1] = (byte)(((x & 3) << 6) + (y >> 4));
        _commandBuffer[2] = (byte)(((y & 15) << 4) + (ex >> 6));
        _commandBuffer[3] = (byte)(((ex & 63) << 2) + (ey >> 8));
        _commandBuffer[4] = (byte)(ey & 255);
        _commandBuffer[5] = command;
    }

    private void WriteCommand(byte command)
    {
        _commandBuffer[5] = command;
        SafeWrite(_commandBuffer, 6);
    }

    private void WriteCommand(byte command, int level)
    {
        _commandBuffer[0] = (byte)(level >> 2);
        _commandBuffer[1] = (byte)((level & 3) << 6);
        _commandBuffer[5] = command;
        SafeWrite(_commandBuffer, 6);
    }

    private void WriteOrientationCommand(byte command, byte orientation)
    {
        if (_compatibilityMode == RevACompatibilityMode.NativePortraitSoftwareRotation)
            return;

        var target = (ScreenOrientation)orientation;
        var landscape = target is ScreenOrientation.Landscape or ScreenOrientation.ReverseLandscape;

        int w;
        int h;
        int commandLength;

        if (_compatibilityMode == RevACompatibilityMode.HardwareNativeDimensions)
        {
            // TuringSmartScreenLib Revision-A behavior.
            w = HwWidth;
            h = HwHeight;
            commandLength = 11;
        }
        else
        {
            // turing-smart-screen-python / TuringMonitor behavior.
            w = landscape ? HwHeight : HwWidth;
            h = landscape ? HwWidth : HwHeight;
            commandLength = 16;
        }

        Array.Clear(_commandBuffer, 0, _commandBuffer.Length);
        _commandBuffer[5] = command;
        _commandBuffer[6] = (byte)(orientation + 100);
        _commandBuffer[7] = (byte)(w >> 8);
        _commandBuffer[8] = (byte)(w & 255);
        _commandBuffer[9] = (byte)(h >> 8);
        _commandBuffer[10] = (byte)(h & 255);
        SafeWrite(_commandBuffer, commandLength);
    }

    private void Connect(int waitForConnect = 0)
    {
        Close();
        var sw = Stopwatch.StartNew();
        while (waitForConnect < 1 || sw.ElapsedMilliseconds < waitForConnect)
        {
            try
            {
                _port = new SerialPort("COM" + _comPortName)
                {
                    DtrEnable = true,
                    Handshake = Handshake.RequestToSend,
                    ReadTimeout = 350,
                    BaudRate = _baudRate,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                    // CRITICAL: Set the OS buffer huge. 
                    // This allows us to write an entire Frame (300KB) without blocking in user code.
                    WriteBufferSize = 524288, // 512 KB
                    WriteTimeout = 2000
                };
                _port.Open();

                // CRITICAL: Bypass the SerialPort wrapper for bulk writes to avoid overhead
                _baseStream = _port.BaseStream;

                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();

                if (Orientation != ScreenOrientation.Portrait &&
                    _compatibilityMode != RevACompatibilityMode.NativePortraitSoftwareRotation)
                {
                    WriteOrientationCommand(CmdOrientation, _lastOrientationIndex);
                }
                break;
            }
            catch (IOException)
            {
                if (sw.ElapsedMilliseconds >= waitForConnect) throw;
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                if (waitForConnect < 1 || sw.ElapsedMilliseconds >= waitForConnect) throw;
                Thread.Sleep(100);
            }
        }
    }

    private void Close()
    {
        _baseStream = null;
        if (_port is { IsOpen: true }) try { _port.Close(); } catch { }
        _port = null;
    }

    private void SafeWrite(byte[] header, int headerLen, byte[]? payload = null, int payloadLen = 0)
    {
        try
        {
            WriteRaw(header, headerLen, payload, payloadLen);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (!RecoverConnection())
                throw new IOException("The display disconnected and automatic reconnection failed.", ex);

            // Retry the command once after state restoration. This is important
            // for partial framebuffer updates: reconnecting without resending
            // the failed payload leaves the LCD visually corrupted.
            WriteRaw(header, headerLen, payload, payloadLen);
        }
    }

    private void WriteRaw(byte[] header, int headerLen, byte[]? payload = null, int payloadLen = 0)
    {
        if (_baseStream == null)
            throw new IOException("Disconnected");

        _baseStream.Write(header, 0, headerLen);

        if (payload is not null && payloadLen > 0)
            _baseStream.Write(payload, 0, payloadLen);
    }

    private bool RecoverConnection()
    {
        try
        {
            Debug.WriteLine("--- RECOVERING CONNECTION ---");
            Close();
            Connect(waitForConnect: 2000);

            if (_port is null || !_port.IsOpen)
                return false;

            // HELLO is safe to repeat and helps restore communication on
            // UsbMonitor sub-revisions after USB/serial reconnection.
            try { InitializeComm(); } catch { }

            Array.Clear(_commandBuffer, 0, _commandBuffer.Length);
            _commandBuffer[5] = CmdScreenOn;
            _port.Write(_commandBuffer, 0, 6);

            if (_compatibilityMode != RevACompatibilityMode.NativePortraitSoftwareRotation &&
                Orientation != ScreenOrientation.Portrait)
            {
                var target = Orientation;
                var landscape = target is ScreenOrientation.Landscape or ScreenOrientation.ReverseLandscape;
                var nativeDimensions = _compatibilityMode == RevACompatibilityMode.HardwareNativeDimensions;
                var w = nativeDimensions ? HwWidth : landscape ? HwHeight : HwWidth;
                var h = nativeDimensions ? HwHeight : landscape ? HwWidth : HwHeight;
                var length = nativeDimensions ? 11 : 16;

                Array.Clear(_commandBuffer, 0, _commandBuffer.Length);
                _commandBuffer[5] = CmdOrientation;
                _commandBuffer[6] = (byte)(_lastOrientationIndex + 100);
                _commandBuffer[7] = (byte)(w >> 8);
                _commandBuffer[8] = (byte)(w & 255);
                _commandBuffer[9] = (byte)(h >> 8);
                _commandBuffer[10] = (byte)(h & 255);
                _port.Write(_commandBuffer, 0, length);
            }

            var protocolBrightness = 255 - (int)Math.Round(Math.Clamp(_lastBrightness, 0, 100) / 100d * 255d);
            Array.Clear(_commandBuffer, 0, _commandBuffer.Length);
            _commandBuffer[0] = (byte)(protocolBrightness >> 2);
            _commandBuffer[1] = (byte)((protocolBrightness & 3) << 6);
            _commandBuffer[5] = CmdBrightness;
            _port.Write(_commandBuffer, 0, 6);

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ########################################################################
    // 9. BENCHMARKING
    // ########################################################################

    public void RunBenchmark()
    {
        Console.WriteLine("--- PRECISION STRATEGY BENCHMARK ---");
        Reset();
        Clear();

        const int w = 100;
        const int h = 100;
        const int boxPayloadBytes = w * h * 2;

        // Dummy payload
        byte[] boxPayload = new byte[boxPayloadBytes];

        Console.WriteLine($"{"Count",-8} | {"Point(ms)",-10} | {"Box(ms)",-10} | {"Winner",-8} | {"Calc Mult",-10}");
        Console.WriteLine(new string('-', 60));

        // Warmup
        WritePixelImmediate(CmdDraw, 0, 0, Color656.Red);

        foreach (var count in BenchmarkSteps)
        {
            GC.Collect();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
                WritePixelImmediate(CmdDraw, i % w, i / w, Color656.Red);
            sw.Stop();
            double timePoints = sw.Elapsed.TotalMilliseconds;

            Thread.Sleep(50); // Drain

            GC.Collect();
            sw.Restart();
            SendRectangularUpdate(CmdDraw, 0, 0, w, h, MemoryMarshal.Cast<byte, Color656>(boxPayload), w, 0, 0);
            sw.Stop();
            double timeBox = sw.Elapsed.TotalMilliseconds;

            string winner = timePoints < timeBox ? "POINTS" : "BOX";
            int suggestedMult = (int)(boxPayloadBytes / (double)count);

            Console.WriteLine($"{count,-8} | {timePoints,-10:F2} | {timeBox,-10:F2} | {winner,-8} | {suggestedMult,-10}");
        }

        Console.WriteLine("--- DONE ---");
        Console.WriteLine("Update 'HeuristicCostPerPixel' with the 'Calc Mult' value where Point ~= Box.");
    }
}