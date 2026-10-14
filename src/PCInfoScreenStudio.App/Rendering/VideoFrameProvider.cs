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
    public static VideoDecoderDetails[] GetDiagnostics() { lock (Sync) return Active.Values.Select(p => p.GetDiagnostics()).ToArray(); }
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
        var path = asset is not null ? workspace.GetAbsolutePath(asset) : widget.MediaSourcePath;
        if (string.IsNullOrWhiteSpace(path)) { status = "Video file missing"; return null; }
        var request = new Request(path, workspace.Document.CanvasWidth, workspace.Document.CanvasHeight,
            VideoPlaybackProfile.FramesPerSecond(display, widget.TargetFps, widget.AutoOptimize, transferMs), widget.PlaybackSpeed, widget.Loop, widget.MediaPaused, widget.TargetFps, widget.AutoOptimize);
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
    private sealed record Request(string Path, int Width, int Height, int Fps, double Speed, bool Loop, bool Paused, int RequestedFps, bool Automatic);

    private sealed class Playback : IDisposable
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _stop = new();
        private Request _request;
        private SKBitmap? _frame;
        private string _status = "Opening video…";
        private bool _disposed;
        private bool _stopped;
        private long _lastFrameTick;
        private VideoDecoderDetails _details = new("Queued", "None", 0, 0, 0, 0, 0, 0, 0, 0);
        public VideoDecoderDetails GetDiagnostics()
        {
            var request = Volatile.Read(ref _request);
            lock (_sync) return _details with { RequestedFps = request.RequestedFps, EffectiveFps = request.Fps,
                Automatic = request.Automatic, Paused = request.Paused, Loop = request.Loop, Speed = request.Speed,
                Stopped = _stopped, MillisecondsSinceLastFrame = _lastFrameTick == 0 ? null : Math.Round(Stopwatch.GetElapsedTime(_lastFrameTick).TotalMilliseconds) };
        }
        private void SetDetails(VideoDecoderDetails details) { lock (_sync) _details = details; }
        public long FrameBytes { get { lock (_sync) return _frame?.ByteCount ?? 0; } }
        public Playback(Request request)
        {
            _request = request;
            _ = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        public bool NeedsNewDecoder(Request request)
        {
            var current = Volatile.Read(ref _request);
            if (current.Path != request.Path || current.Width != request.Width || current.Height != request.Height) return true;
            // A stopped worker cannot observe later settings. Reopen only on an
            // explicit control edit, never on transfer-time changes or every render.
            lock (_sync) return _stopped && (current.RequestedFps != request.RequestedFps || current.Automatic != request.Automatic
                || current.Speed != request.Speed || current.Loop != request.Loop || current.Paused != request.Paused);
        }
        public void Update(Request request) => Volatile.Write(ref _request, request);
        public SKBitmap? CopyFrame(out string status) { lock (_sync) { status = _status; return _frame?.Copy(); } }
        private void Publish(SKBitmap frame, string status, VideoDecoderDetails details)
        {
            lock (_sync) { if (_disposed) { frame.Dispose(); return; } _frame?.Dispose(); _frame = frame; _status = status; _details = details; _lastFrameTick = Stopwatch.GetTimestamp(); }
        }
        private void Run()
        {
            var slot = false; var com = false; var foundation = false;
            var details = GetDiagnostics();
            InvalidDataException Reject(string code, string message) { details = details with { Code = code }; return new(message); }
            try
            {
                DecoderSlots.Wait(_stop.Token); slot = true; Interlocked.Increment(ref _running);
                details = details with { Stage = "Initialize" }; SetDetails(details);
                Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0)); com = true;
                MFStartup().CheckError(); foundation = true;
                var request = Volatile.Read(ref _request);
                details = details with { Stage = "OpenSource" }; SetDetails(details);
                if (!Path.IsPathFullyQualified(request.Path) || !File.Exists(request.Path)) throw new FileNotFoundException();
                if (new FileInfo(request.Path).Length > AssetImportService.MaximumVideoBytes) throw Reject("FileTooLarge", "Use a video smaller than 512 MiB.");
                using var attributes = MFCreateAttributes(2);
                attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, true).CheckError();
                using var reader = MFCreateSourceReaderFromURL(request.Path, attributes);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true); // Never decode or play audio.
                using var native = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
                MFGetAttributeSize(native, MediaTypeAttributeKeys.FrameSize, out var sourceWidth, out var sourceHeight).CheckError();
                details = details with { Stage = "SourceMetadata", SourceWidth = (int)sourceWidth, SourceHeight = (int)sourceHeight }; SetDetails(details);
                (int Width, int Height) size;
                try { size = VideoPlaybackProfile.DecodeSize((int)sourceWidth, (int)sourceHeight, request.Width, request.Height); }
                catch (InvalidDataException) { details = details with { Code = "UnsupportedSourceDimensions" }; throw; }
                details = details with { Stage = "ConfigureOutput" }; SetDetails(details);
                using var output = MFCreateMediaType();
                output.Set(MediaTypeAttributeKeys.MajorType, new Guid("73646976-0000-0010-8000-00aa00389b71")).CheckError();
                output.Set(MediaTypeAttributeKeys.Subtype, new Guid("00000016-0000-0010-8000-00aa00389b71")).CheckError();
                MFSetAttributeSize(output, MediaTypeAttributeKeys.FrameSize, (uint)size.Width, (uint)size.Height).CheckError();
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);
                var (width, height, stride) = ReadOutputFormat(reader, size, ref details); SetDetails(details);
                var clock = Stopwatch.StartNew();
                double mediaTicks = 0; double lastMs = 0; double nextPublish = 0;
                long? origin = null; var first = true; var emptyReads = 0; var loopHadFrame = false;
                while (!_stop.IsCancellationRequested)
                {
                    details = details with { Stage = "ReadSample" }; SetDetails(details);
                    using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, 0, out _, out var flags, out var timestamp);
                    details = details with { LastFlags = (int)flags };
                    if ((flags & SourceReaderFlag.Error) != 0) throw Reject("StreamError", "Video stream error.");
                    if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0)
                    {
                        // Decoders can refine colour/stride metadata after reading H.264
                        // headers. This notification does not mean dimensions changed.
                        // Refresh the negotiated layout before reading this very sample.
                        details = details with { Stage = "RefreshOutput", FormatChanges = details.FormatChanges + 1 }; SetDetails(details);
                        (width, height, stride) = ReadOutputFormat(reader, size, ref details); SetDetails(details);
                    }
                    if ((flags & SourceReaderFlag.EndOfStream) != 0)
                    {
                        request = Volatile.Read(ref _request);
                        if (!loopHadFrame) throw Reject("NoFrames", "Video contains no frames.");
                        if (!request.Loop) { lock (_sync) { _stopped = true; _status = "Video ended · Restart to play again"; } details = details with { Stage = "Ended" }; SetDetails(details); break; }
                        reader.SetCurrentPosition(0); mediaTicks = 0; lastMs = clock.Elapsed.TotalMilliseconds; origin = null; loopHadFrame = false; continue;
                    }
                    if (sample is null || sample.NativePointer == IntPtr.Zero) { if (++emptyReads > 120) throw Reject("NoReadableFrames", "Video contains no readable frames."); continue; }
                    emptyReads = 0; loopHadFrame = true; origin ??= timestamp;
                    details = details with { Stage = "WaitForTimestamp", ReadSamples = details.ReadSamples + 1, SampleTimeTicks = timestamp - origin.Value }; SetDetails(details);
                    while (true)
                    {
                        request = Volatile.Read(ref _request);
                        var now = clock.Elapsed.TotalMilliseconds;
                        if (!request.Paused) mediaTicks += (now - lastMs) * 10000 * request.Speed;
                        lastMs = now;
                        details = details with { MediaClockTicks = double.IsFinite(mediaTicks) ? (long)Math.Clamp(mediaTicks, 0, long.MaxValue) : 0 }; SetDetails(details);
                        if (first || (!request.Paused && timestamp - origin.Value <= mediaTicks)) break;
                        lock (_sync) if (!_disposed) _status = request.Paused ? "Video paused" : $"Playing · {request.Fps} fps limit";
                        if (_stop.Token.WaitHandle.WaitOne(15)) return;
                    }
                    if (first || clock.Elapsed.TotalMilliseconds >= nextPublish)
                    {
                        details = details with { Stage = "CopyFrame" }; SetDetails(details);
                        using var buffer = sample.ConvertToContiguousBuffer();
                        buffer.Lock(out var pixels, out _, out var length);
                        SKBitmap? frame = null;
                        try
                        {
                            var rowBytes = checked((int)width * 4);
                            if (stride == 0 || Math.Abs((long)stride) < rowBytes || Math.Abs((long)stride) * (height - 1) + rowBytes > length)
                                throw Reject("InvalidBufferLayout", "Invalid video frame stride or buffer length.");
                            frame = new SKBitmap((int)width, (int)height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                            var row = new byte[rowBytes];
                            for (var y = 0; y < height; y++)
                            {
                                var offset = stride < 0 ? ((int)height - 1 - y) * -stride : y * stride;
                                Marshal.Copy(IntPtr.Add(pixels, offset), row, 0, rowBytes);
                                Marshal.Copy(row, 0, IntPtr.Add(frame.GetPixels(), y * frame.RowBytes), rowBytes);
                            }
                            details = details with { PublishedFrames = details.PublishedFrames + 1 };
                            Publish(frame, request.Paused ? "Video paused" : $"Playing · {request.Fps} fps limit", details); frame = null;
                        }
                        finally { frame?.Dispose(); buffer.Unlock(); }
                        first = false; nextPublish = clock.Elapsed.TotalMilliseconds + 1000d / request.Fps;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (details.Code == "None") details = details with { Code = ex is InvalidDataException ? "InvalidMediaData" : "NativeOrFileError" };
                SetDetails(details);
                lock (_sync) _stopped = true;
                DiagnosticsService.RecordError("Video decoder", ex, GetDiagnostics());
                Console.Error.WriteLine($"Video decoder: {ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}");
                lock (_sync) { _stopped = true; if (!_disposed) _status = ex is FileNotFoundException or DirectoryNotFoundException ? "Video file missing · Restore the file and Restart video" : ex is InvalidDataException ? ex.Message : "Cannot play video · Try H.264 MP4 or install Windows Media Feature Pack"; }
            }
            finally
            {
                if (foundation) MFShutdown();
                if (com) CoUninitialize();
                if (slot) { Interlocked.Decrement(ref _running); DecoderSlots.Release(); }
                _stop.Dispose();
            }
        }
        private static (uint Width, uint Height, int Stride) ReadOutputFormat(IMFSourceReader reader, (int Width, int Height) size, ref VideoDecoderDetails details)
        {
            using var actual = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            MFGetAttributeSize(actual, MediaTypeAttributeKeys.FrameSize, out var width, out var height).CheckError();
            // Save numeric metadata even if validation below rejects this layout.
            details = details with { OutputWidth = (int)width, OutputHeight = (int)height };
            if (width != size.Width || height != size.Height)
            {
                details = details with { Code = "OutputSizeMismatch" };
                throw new InvalidDataException("The video decoder did not accept the bounded output size.");
            }
            if (actual.GetGUID(MediaTypeAttributeKeys.Subtype) != new Guid("00000016-0000-0010-8000-00aa00389b71"))
            {
                details = details with { Code = "OutputNotRgb32" };
                throw new InvalidDataException("The video decoder did not provide RGB32 frames.");
            }
            var stride = unchecked((int)MFGetAttributeUInt32(actual, MediaTypeAttributeKeys.DefaultStride, width * 4));
            details = details with { Stride = stride };
            return (width, height, stride);
        }
        public void Dispose()
        {
            lock (_sync) { if (_disposed) return; _disposed = true; try { _stop.Cancel(); } catch (ObjectDisposedException) { } _frame?.Dispose(); _frame = null; }
        }
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint model);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
