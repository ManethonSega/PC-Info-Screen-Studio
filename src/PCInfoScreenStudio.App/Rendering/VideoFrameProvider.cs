using System.Diagnostics;
using System.Runtime.InteropServices;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using SkiaSharp;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace PCInfoScreenStudio.Rendering;

/// <summary>Two bounded decoders, one latest bitmap each. COM ownership stays on dedicated MTA workers.</summary>
public static class VideoFrameProvider
{
    public const int DecoderLimit = 2;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Playback> Active = new();
    private static readonly SemaphoreSlim DecoderSlots = new(DecoderLimit);
    private static int _running;
    public static int RunningDecoders => Volatile.Read(ref _running);
    public static int ActiveCount { get { lock (Sync) return Active.Count; } }
    public static long CachedFrameBytes { get { lock (Sync) return Active.Values.Sum(p => p.FrameBytes); } }
    private static string Key(ThemeWorkspace workspace, WidgetModel widget) => workspace.RootDirectory + "|" + widget.Id;

    public static void Synchronize(ThemeWorkspace workspace, IEnumerable<WidgetModel> widgets)
    {
        var wanted = widgets.Where(w => w.Type == WidgetType.Video && w.IsVisible).OrderBy(w => w.ZIndex).Take(DecoderLimit).Select(w => Key(workspace, w)).ToHashSet();
        lock (Sync)
        {
            foreach (var key in Active.Keys.Where(k => k.StartsWith(workspace.RootDirectory + "|", StringComparison.Ordinal) && !wanted.Contains(k)).ToArray()) { Active[key].Dispose(); Active.Remove(key); }
        }
    }

    public static SKBitmap? GetFrame(ThemeWorkspace workspace, WidgetModel widget, DisplayCanvasSize display, double transferMs, out string status)
    {
        var asset = workspace.Document.Assets.FirstOrDefault(a => a.Id == widget.AssetId);
        if (asset is null) { status = "Video file missing"; return null; }
        var path = workspace.GetAbsolutePath(asset);
        var request = new Request(path, workspace.Document.CanvasWidth, workspace.Document.CanvasHeight,
            VideoPlaybackProfile.FramesPerSecond(display, widget.TargetFps, widget.AutoOptimize, transferMs), widget.PlaybackSpeed, widget.Loop, widget.MediaPaused);
        lock (Sync)
        {
            var key = Key(workspace, widget);
            if (!Active.TryGetValue(key, out var playback))
            {
                if (Active.Count >= DecoderLimit) { status = "Limit: two active videos"; return null; }
                playback = new Playback(request); Active[key] = playback;
            }
            if (playback.NeedsNewDecoder(request)) { playback.Dispose(); playback = new Playback(request); Active[key] = playback; }
            playback.Update(request);
            return playback.CopyFrame(out status);
        }
    }

    public static void Restart(ThemeWorkspace workspace, WidgetModel widget)
    {
        lock (Sync) if (Active.Remove(Key(workspace, widget), out var playback)) playback.Dispose();
    }
    public static void Suspend(ThemeWorkspace workspace) => Synchronize(workspace, []);
    public static void Clear() { lock (Sync) { foreach (var p in Active.Values) p.Dispose(); Active.Clear(); } }
    private sealed record Request(string Path, int Width, int Height, int Fps, double Speed, bool Loop, bool Paused);

    private sealed class Playback : IDisposable
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _stop = new();
        private Request _request;
        private SKBitmap? _frame;
        private string _status = "Opening video…";
        private bool _disposed;
        public long FrameBytes { get { lock (_sync) return _frame?.ByteCount ?? 0; } }
        public Playback(Request request)
        {
            _request = request;
            _ = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        public bool NeedsNewDecoder(Request request) { var current = Volatile.Read(ref _request); return current.Path != request.Path || current.Width != request.Width || current.Height != request.Height; }
        public void Update(Request request) => Volatile.Write(ref _request, request);
        public SKBitmap? CopyFrame(out string status) { lock (_sync) { status = _status; return _frame?.Copy(); } }
        private void Publish(SKBitmap frame, string status)
        {
            lock (_sync) { if (_disposed) { frame.Dispose(); return; } _frame?.Dispose(); _frame = frame; _status = status; }
        }
        private void Run()
        {
            var slot = false; var com = false; var foundation = false;
            try
            {
                DecoderSlots.Wait(_stop.Token); slot = true; Interlocked.Increment(ref _running);
                Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0)); com = true;
                MFStartup().CheckError(); foundation = true;
                var request = Volatile.Read(ref _request);
                if (!Path.IsPathFullyQualified(request.Path) || !File.Exists(request.Path)) throw new FileNotFoundException();
                if (new FileInfo(request.Path).Length > AssetImportService.MaximumVideoBytes) throw new InvalidDataException("Use a video smaller than 512 MiB.");
                using var attributes = MFCreateAttributes(2);
                attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true).CheckError();
                using var reader = MFCreateSourceReaderFromURL(request.Path, attributes);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true); // Never decode or play audio.
                using var native = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
                MFGetAttributeSize(native, MediaTypeAttributeKeys.FrameSize, out var sourceWidth, out var sourceHeight).CheckError();
                var size = VideoPlaybackProfile.DecodeSize((int)sourceWidth, (int)sourceHeight, request.Width, request.Height);
                using var output = MFCreateMediaType();
                output.Set(MediaTypeAttributeKeys.MajorType, new Guid("73646976-0000-0010-8000-00aa00389b71")).CheckError();
                output.Set(MediaTypeAttributeKeys.Subtype, new Guid("00000016-0000-0010-8000-00aa00389b71")).CheckError();
                MFSetAttributeSize(output, MediaTypeAttributeKeys.FrameSize, (uint)size.Width, (uint)size.Height).CheckError();
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);
                using var actual = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                MFGetAttributeSize(actual, MediaTypeAttributeKeys.FrameSize, out var width, out var height).CheckError();
                if (width != size.Width || height != size.Height) throw new InvalidDataException("The video decoder did not accept the bounded output size.");
                var stride = unchecked((int)MFGetAttributeUInt32(actual, MediaTypeAttributeKeys.DefaultStride, width * 4));
                var clock = Stopwatch.StartNew();
                double mediaTicks = 0; double lastMs = 0; double nextPublish = 0;
                long? origin = null; var first = true; var emptyReads = 0; var loopHadFrame = false;
                while (!_stop.IsCancellationRequested)
                {
                    using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, 0, out _, out var flags, out var timestamp);
                    if ((flags & SourceReaderFlag.Error) != 0) throw new InvalidDataException("Video stream error.");
                    if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0) throw new InvalidDataException("Changing video dimensions are not supported.");
                    if ((flags & SourceReaderFlag.EndOfStream) != 0)
                    {
                        request = Volatile.Read(ref _request);
                        if (!loopHadFrame) throw new InvalidDataException("Video contains no frames.");
                        if (!request.Loop) { lock (_sync) _status = "Video ended · Restart to play again"; break; }
                        reader.SetCurrentPosition(0); mediaTicks = 0; lastMs = clock.Elapsed.TotalMilliseconds; origin = null; loopHadFrame = false; continue;
                    }
                    if (sample is null || sample.NativePointer == IntPtr.Zero) { if (++emptyReads > 120) throw new InvalidDataException("Video contains no readable frames."); continue; }
                    emptyReads = 0; loopHadFrame = true; origin ??= timestamp;
                    while (true)
                    {
                        request = Volatile.Read(ref _request);
                        var now = clock.Elapsed.TotalMilliseconds;
                        if (!request.Paused) mediaTicks += (now - lastMs) * 10000 * request.Speed;
                        lastMs = now;
                        if (first || (!request.Paused && timestamp - origin.Value <= mediaTicks)) break;
                        lock (_sync) if (!_disposed) _status = request.Paused ? "Video paused" : $"Playing · {request.Fps} fps limit";
                        if (_stop.Token.WaitHandle.WaitOne(15)) return;
                    }
                    if (first || clock.Elapsed.TotalMilliseconds >= nextPublish)
                    {
                        using var buffer = sample.ConvertToContiguousBuffer();
                        buffer.Lock(out var pixels, out _, out var length);
                        SKBitmap? frame = null;
                        try
                        {
                            var rowBytes = checked((int)width * 4);
                            if (stride == 0 || Math.Abs((long)stride) < rowBytes || (long)Math.Abs(stride) * height > length)
                                throw new InvalidDataException("Invalid video frame stride or buffer length.");
                            frame = new SKBitmap((int)width, (int)height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                            var row = new byte[rowBytes];
                            for (var y = 0; y < height; y++)
                            {
                                var offset = stride < 0 ? ((int)height - 1 - y) * -stride : y * stride;
                                Marshal.Copy(IntPtr.Add(pixels, offset), row, 0, rowBytes);
                                Marshal.Copy(row, 0, IntPtr.Add(frame.GetPixels(), y * frame.RowBytes), rowBytes);
                            }
                            Publish(frame, request.Paused ? "Video paused" : $"Playing · {request.Fps} fps limit"); frame = null;
                        }
                        finally { frame?.Dispose(); buffer.Unlock(); }
                        first = false; nextPublish = clock.Elapsed.TotalMilliseconds + 1000d / request.Fps;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DiagnosticsService.RecordError("Video decoder", ex);
                Console.Error.WriteLine($"Video decoder: {ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}");
                lock (_sync) if (!_disposed) _status = ex is InvalidDataException ? ex.Message : "Cannot play video · Try H.264 MP4 or install Windows Media Feature Pack";
            }
            finally
            {
                if (foundation) MFShutdown();
                if (com) CoUninitialize();
                if (slot) { Interlocked.Decrement(ref _running); DecoderSlots.Release(); }
                _stop.Dispose();
            }
        }
        public void Dispose()
        {
            lock (_sync) { if (_disposed) return; _disposed = true; try { _stop.Cancel(); } catch (ObjectDisposedException) { } _frame?.Dispose(); _frame = null; }
        }
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint model);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
