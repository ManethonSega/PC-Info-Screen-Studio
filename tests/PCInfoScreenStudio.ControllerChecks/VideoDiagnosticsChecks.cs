using System.IO;
using System.Text.Json;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;
using SkiaSharp;

internal static class VideoDiagnosticsChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Check(string root, Action<Func<bool>, TimeSpan> pump)
    {
        Require(VideoPlaybackProfile.FramesPerSecond(DisplayCanvasSize.Serial35, 0, true) == 3, "Serial Auto profile");
        Require(VideoPlaybackProfile.FramesPerSecond(DisplayCanvasSize.Serial35, 60, false) == 5, "Serial FPS ceiling");
        Require(VideoPlaybackProfile.FramesPerSecond(DisplayCanvasSize.Turing52, 0, true) == 12, "USB Auto profile");
        Require(VideoPlaybackProfile.FramesPerSecond(DisplayCanvasSize.Turing52, 60, false) == 20, "USB FPS ceiling");
        Require(VideoPlaybackProfile.FramesPerSecond(DisplayCanvasSize.Turing52, 60, false, 500) == 1, "Measured transfer backpressure");
        Require(VideoPlaybackProfile.DecodeSize(1920, 1080, 480, 320) == (480, 270), "Aspect-preserving serial decode size");
        Require(VideoPlaybackProfile.DecodeSize(1080, 1920, 720, 1280) == (720, 1280), "Portrait USB decode size");
        try { VideoPlaybackProfile.DecodeSize(3840, 2160, 1280, 720); throw new Exception("4K must not be admitted"); }
        catch (InvalidDataException) { }
        using var workspace = new ThemePackageService().CreateNewWorkspace();
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "video-colours.mp4");
        var asset = new AssetImportService().Import(workspace, fixture, ThemeAssetKind.Video);
        var widget = new WidgetModel { Type = WidgetType.Video, AssetId = asset.Id, Width = 480, Height = 320, TargetFps = 20, Loop = false, MediaPaused = true };
        workspace.Document.Widgets.Add(widget); workspace.Document.RuntimeMode = RuntimeScreenMode.InfoScreen;
        VideoFrameProvider.Clear(); VideoFrameProvider.Synchronize(workspace, [widget]);
        SKColor top = default, bottom = default; string status = "";
        pump(() => {
            using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 0, out status);
            if (frame is null) return false;
            top = frame.GetPixel(frame.Width / 2, 10); bottom = frame.GetPixel(frame.Width / 2, frame.Height - 10);
            return true;
        }, TimeSpan.FromSeconds(15));
        Require(top.Red > 180 && top.Blue < 70 && bottom.Blue > 180 && bottom.Red < 70, $"Decoded RGB colour/stride/orientation: top={top} bottom={bottom}, {status}");
        Require(VideoFrameProvider.CachedFrameBytes <= 480 * 320 * 4, "Only one bounded cached bitmap");
        Require(status.Contains("paused"), "Initially paused video shows first frame");
        widget.MediaPaused = false;
        pump(() => {
            using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 0, out status);
            return frame is not null && frame.GetPixel(80, 10).Green > 70 && frame.GetPixel(80, 10).Red < 70;
        }, TimeSpan.FromSeconds(10));
        pump(() => { using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 0, out status); if (status.StartsWith("Cannot") || status.StartsWith("Invalid")) throw new InvalidOperationException("End-of-stream failed: " + status); return status.Contains("ended"); }, TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS: video first frame, colour, pause/unpause and non-looping EOF");
        widget.Loop = true; widget.PlaybackSpeed = 2; VideoFrameProvider.Restart(workspace, widget);
        var sawGreen = false;
        pump(() => {
            using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 0, out status);
            if (frame is null) return false;
            var c = frame.GetPixel(80, 10); if (c.Green > 70 && c.Red < 70) sawGreen = true;
            return sawGreen && c.Red > 180 && c.Green < 70;
        }, TimeSpan.FromSeconds(10));
        widget.MediaPaused = true;
        using (var ignored = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 0, out status)) { }
        pump(() => { using var ignored = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 0, out status); return status.Contains("paused"); }, TimeSpan.FromSeconds(3));
        var other = widget.Clone(); other.Id = Guid.NewGuid();
        var third = widget.Clone(); third.Id = Guid.NewGuid();
        VideoFrameProvider.Synchronize(workspace, [widget, other, third]);
        using (var second = VideoFrameProvider.GetFrame(workspace, other, DisplayCanvasSize.Serial35, 0, out _)) { }
        using (var excluded = VideoFrameProvider.GetFrame(workspace, third, DisplayCanvasSize.Serial35, 0, out var limit)) Require(excluded is null && limit.Contains("two"), "Two-video limit must be clear");
        workspace.Document.RuntimeMode = RuntimeScreenMode.PhotoFrame;
        using (var frame = new ThemeRenderer().Render(workspace)) { }
        pump(() => VideoFrameProvider.RunningDecoders == 0, TimeSpan.FromSeconds(5));
        Require(VideoFrameProvider.ActiveCount == 0 && VideoFrameProvider.CachedFrameBytes == 0, "Inactive mode releases decoders and frames");
        var broken = Path.Combine(root, "broken.mp4"); File.WriteAllText(broken, "not a movie");
        var badAsset = new AssetImportService().Import(workspace, broken, ThemeAssetKind.Video);
        widget.AssetId = badAsset.Id;
        VideoFrameProvider.Synchronize(workspace, [widget]);
        pump(() => { using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Serial35, 0, out status); return !status.StartsWith("Opening"); }, TimeSpan.FromSeconds(10));
        Require(status.Contains("Cannot play") || status.Contains("Video"), "Unsupported media has an actionable status");
        VideoFrameProvider.Clear(); pump(() => VideoFrameProvider.RunningDecoders == 0, TimeSpan.FromSeconds(5));
        DiagnosticsService.RecordError("Test decoder", new FileNotFoundException("Secret account Alice at C:\\Users\\Alice\\Private\\holiday.mp4"));
        var report = DiagnosticsService.CreateReport(new { connected = false, family = "Serial35" }, new { activeVideos = 0 });
        var reportPath = Path.Combine(root, "diagnostics.json"); DiagnosticsService.ExportAsync(reportPath, report).GetAwaiter().GetResult();
        var json = File.ReadAllText(reportPath);
        using var parsed = JsonDocument.Parse(json);
        Require(parsed.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "Diagnostics schema");
        Require(parsed.RootElement.GetProperty("memory").GetProperty("workingSetBytes").GetInt64() > 0, "Memory counters");
        Require(json.Contains("FileNotFoundException") && !json.Contains("Alice") && !json.Contains("holiday.mp4") && !json.Contains("C:\\\\Users"), "Diagnostics excludes messages and personal paths");
        Require(!Directory.EnumerateFiles(root, "diagnostics.json.*.tmp").Any(), "Diagnostics export removes temporary files");
        var clone = widget.Clone(); Require(clone.MediaPaused == widget.MediaPaused && clone.PlaybackSpeed == 2, "Playback controls persist in clones");
        Console.WriteLine("PASS: real H.264 decoding, RGB/stride/orientation, bounded frames, pause, playback, EOF, loop, speed, two-video limit, inactive cleanup, bad media and private local diagnostics.");
    }
}
