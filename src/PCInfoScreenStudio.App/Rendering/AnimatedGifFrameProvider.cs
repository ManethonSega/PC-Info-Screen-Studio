using System.Collections.Concurrent;
using SkiaSharp;

namespace PCInfoScreenStudio.Rendering;

/// <summary>
/// GIF playback cache. Each asset keeps one decoder and composited bitmap while
/// its workspace is active, then releases both when the workspace changes.
/// </summary>
public static class AnimatedGifFrameProvider
{
    private static readonly ConcurrentDictionary<string, long> Started = new(StringComparer.OrdinalIgnoreCase);
    public static SKBitmap? GetFrame(
        string path,
        double playbackSpeed,
        int targetFps,
        bool loop)
    {
        try
        {
            using var frame = AcquireFrame(path, playbackSpeed, targetFps, loop);
            return frame?.Value.Copy();
        }
        catch
        {
            return null;
        }
    }

    public static MediaLease<SKBitmap>? AcquireFrame(string path, double playbackSpeed, int targetFps, bool loop)
    {
        MediaLease<GifState>? retained = null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path).Ticks;
            var started = Started.GetOrAdd(path, _ => Environment.TickCount64);
            retained = MediaResourceCache.Acquire($"gif:{path}|{stamp}", "gif", path, () => new GifState(path, started), s => s.MemoryBytes, stamp.ToString());
            var state = retained.Value;
            Monitor.Enter(state.Sync);
            try
            {
                var frame = state.GetFrame(playbackSpeed, targetFps, loop);
                var owned = retained;
                return new(frame, () => { Monitor.Exit(state.Sync); owned.Dispose(); });
            }
            catch { Monitor.Exit(state.Sync); throw; }
        }
        catch { retained?.Dispose(); return null; }
    }

    public static void Clear() { MediaResourceCache.Clear("gif"); Started.Clear(); }
    public static void Synchronize(IEnumerable<string> paths)
    {
        var active = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Started.Keys.Where(p => !active.Contains(p))) Started.TryRemove(path, out _);
    }

    private sealed class GifState : IDisposable
    {
        private readonly object _sync = new();
        public object Sync => _sync;
        public long MemoryBytes => (long)_bitmap.RowBytes * _bitmap.Height + (_encodedData?.Size ?? 0) + (long)_frames.Length * (System.Runtime.CompilerServices.Unsafe.SizeOf<SKCodecFrameInfo>() + sizeof(int));
        private readonly SKCodec _codec;
        private readonly SKData? _encodedData;
        private readonly SKBitmap _bitmap;
        private readonly SKImageInfo _decodeInfo;
        private readonly SKCodecFrameInfo[] _frames;
        private readonly int[] _durations;
        private readonly long _startedAtMs = Environment.TickCount64;
        private readonly int _totalDurationMs;
        private int _decodedFrame = -1;
        private bool _disposed;

        public GifState(string path) : this(path, Environment.TickCount64) { }
        public GifState(string path, long startedAtMs)
        {
            _startedAtMs = startedAtMs;
            // Keep modest compressed GIFs in memory so looping/skip decodes do
            // not seek and read the file repeatedly. Do not cache decoded frames.
            if (new FileInfo(path).Length <= 16 * 1024 * 1024)
                _encodedData = SKData.CreateCopy(File.ReadAllBytes(path));
            _codec = (_encodedData is null ? SKCodec.Create(path) : SKCodec.Create(_encodedData))
                ?? throw new InvalidDataException("SkiaSharp could not decode the GIF.");

            _frames = _codec.FrameInfo;
            if (_frames.Length == 0)
            { _codec.Dispose(); _encodedData?.Dispose(); throw new InvalidDataException("The GIF does not contain animation frames."); }

            var info = _codec.Info;
            if ((long)info.Width * info.Height * 4 > 64L * 1024 * 1024)
            { _codec.Dispose(); _encodedData?.Dispose(); throw new InvalidDataException("GIF decoding exceeds the 64 MiB single-frame limit. Use a smaller GIF."); }
            _decodeInfo = new SKImageInfo(
                info.Width,
                info.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);

            _bitmap = new SKBitmap(_decodeInfo);
            _durations = new int[_frames.Length];

            var total = 0;
            for (var i = 0; i < _frames.Length; i++)
            {
                var duration = _frames[i].Duration;
                if (duration <= 0)
                    duration = 100;

                duration = Math.Max(16, duration);
                _durations[i] = duration;
                total += duration;
            }

            _totalDurationMs = Math.Max(1, total);
        }

        public SKBitmap GetFrame(double playbackSpeed, int targetFps, bool loop)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var speed = Math.Clamp(playbackSpeed, 0.1, 4.0);
                var elapsed = Math.Max(0d, (Environment.TickCount64 - _startedAtMs) * speed);

                if (targetFps > 0)
                {
                    var step = 1000d / Math.Clamp(targetFps, 1, 60);
                    elapsed = Math.Floor(elapsed / step) * step;
                }

                if (loop)
                    elapsed %= _totalDurationMs;
                else
                    elapsed = Math.Min(elapsed, _totalDurationMs - 1);

                var target = FrameAt((int)elapsed);
                DecodeThrough(target);
                return _bitmap;
            }
        }

        private int FrameAt(int elapsedMs)
        {
            var cursor = 0;
            for (var i = 0; i < _durations.Length; i++)
            {
                cursor += _durations[i];
                if (elapsedMs < cursor)
                    return i;
            }

            return _durations.Length - 1;
        }

        private void DecodeThrough(int target)
        {
            if (_decodedFrame == target) return;
            var required = _frames[target].RequiredFrame;
            var prior = _decodedFrame >= required && _decodedFrame < target && _decodedFrame >= 0
                && _frames[_decodedFrame].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious
                ? _decodedFrame : -1;
            if (prior < 0) _bitmap.Erase(SKColors.Transparent);
            var result = _codec.GetPixels(_decodeInfo, _bitmap.GetPixels(),
                new SKCodecOptions(target) { PriorFrame = prior });
            if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            {
                _bitmap.NotifyPixelsChanged();
                _decodedFrame = target;
            }
            else _decodedFrame = -1;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _bitmap.Dispose();
                _codec.Dispose();
                _encodedData?.Dispose();
            }
        }
    }
}
