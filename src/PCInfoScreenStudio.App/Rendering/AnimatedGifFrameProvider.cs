using System.Collections.Concurrent;
using SkiaSharp;

namespace PCInfoScreenStudio.Rendering;

/// <summary>
/// GIF playback cache. Each asset keeps one decoder and composited bitmap while
/// its workspace is active, then releases both when the workspace changes.
/// </summary>
public static class AnimatedGifFrameProvider
{
    private static readonly ConcurrentDictionary<string, GifState> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static SKBitmap? GetFrame(
        string path,
        double playbackSpeed,
        int targetFps,
        bool loop)
    {
        try
        {
            var state = Cache.GetOrAdd(path, static p => new GifState(p));
            return state.GetFrame(playbackSpeed, targetFps, loop);
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        foreach (var state in Cache.Values)
            state.Dispose();
        Cache.Clear();
    }

    private sealed class GifState : IDisposable
    {
        private readonly object _sync = new();
        private readonly SKCodec _codec;
        private readonly SKBitmap _bitmap;
        private readonly SKImageInfo _decodeInfo;
        private readonly SKCodecFrameInfo[] _frames;
        private readonly int[] _durations;
        private readonly long _startedAtMs = Environment.TickCount64;
        private readonly int _totalDurationMs;
        private int _decodedFrame = -1;
        private bool _disposed;

        public GifState(string path)
        {
            _codec = SKCodec.Create(path)
                ?? throw new InvalidDataException("SkiaSharp could not decode the GIF.");

            _frames = _codec.FrameInfo;
            if (_frames.Length == 0)
                throw new InvalidDataException("The GIF does not contain animation frames.");

            var info = _codec.Info;
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
            if (_decodedFrame == target)
                return;

            if (_decodedFrame < 0 || target < _decodedFrame)
            {
                _bitmap.Erase(SKColors.Transparent);
                _decodedFrame = -1;
            }

            for (var frame = _decodedFrame + 1; frame <= target; frame++)
            {
                var result = _codec.GetPixels(
                    _decodeInfo,
                    _bitmap.GetPixels(),
                    new SKCodecOptions(frame));

                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                    break;

                _bitmap.NotifyPixelsChanged();
                _decodedFrame = frame;
            }
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
            }
        }
    }
}
