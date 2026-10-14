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
        Require(VideoPlaybackProfile.DecodeSize(3840, 2160, 1280, 720) == (1280, 720), "4K source bounded to USB canvas");
        Require(VideoPlaybackProfile.DecodeSize(3840, 2160, 480, 320) == (480, 270), "4K source bounded to serial canvas");
        try { VideoPlaybackProfile.DecodeSize(7680, 4320, 1280, 720); throw new Exception("8K must not be admitted"); }
        catch (InvalidDataException) { }
        CheckFragmentedVideo(pump, "video-fragmented-720p.mp4", 1280, 720);
        CheckFragmentedVideo(pump, "video-fragmented-4k.mp4", 3840, 2160);
        CheckLiveFpsChanges(pump);
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
        using (var unrelated = new ThemePackageService().CreateNewWorkspace())
        {
            VideoFrameProvider.Synchronize(unrelated, []);
            VideoFrameProvider.Suspend(unrelated);
            ThemeRenderer.ClearCaches();
            Require(VideoFrameProvider.ActiveCount == 1 && VideoFrameProvider.CachedFrameBytes > 0, "Other workspaces and image/font cleanup must not restart a video");
        }
        workspace.Document.CanvasWidth = 80; workspace.Document.CanvasHeight = 48;
        pump(() => {
            using var resized = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Serial35, 0, out _);
            return resized is not null && resized.Width == 80 && resized.Height == 48 && resized.GetPixel(40, 5).Red > 180;
        }, TimeSpan.FromSeconds(10));
        workspace.Document.CanvasWidth = 480; workspace.Document.CanvasHeight = 320;
        pump(() => { using var restored = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 0, out _); return restored is not null && restored.Width == 160; }, TimeSpan.FromSeconds(10));
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
        DiagnosticsService.RecordError("Video decoder", new InvalidDataException("Private media at C:\\Users\\Alice\\holiday.mp4"),
            new VideoDecoderDetails("RefreshOutput", "OutputSizeMismatch", 1280, 720, 1920, 1080, 7680, 1, 0, 32));
        var oldLog = Path.Combine(root, "private-startup.log");
        File.WriteAllText(oldLog, "System.IO.FileNotFoundException: Secret account Alice at C:\\Users\\Alice\\Private\\holiday.mp4\n   at C:\\private\\System.CustomerNameException.cs\n");
        var report = DiagnosticsService.CreateReport(new { connected = false, family = "Serial35" }, new { activeVideos = 0 }, oldLog);
        var reportPath = Path.Combine(root, "diagnostics.json"); DiagnosticsService.ExportAsync(reportPath, report).GetAwaiter().GetResult();
        var json = File.ReadAllText(reportPath);
        using var parsed = JsonDocument.Parse(json);
        Require(parsed.RootElement.GetProperty("schemaVersion").GetInt32() == 3, "Diagnostics schema");
        Require(parsed.RootElement.GetProperty("videoPlayback").GetArrayLength() == 0, "Inactive decoder diagnostics cleared");
        var videoError = parsed.RootElement.GetProperty("recentErrors").EnumerateArray().Last().GetProperty("Video");
        Require(videoError.GetProperty("Stage").GetString() == "RefreshOutput" && videoError.GetProperty("Code").GetString() == "OutputSizeMismatch" && videoError.GetProperty("FormatChanges").GetInt32() == 1, "Decoder diagnostic stage, stable code and numeric metadata");
        Require(parsed.RootElement.GetProperty("memory").GetProperty("workingSetBytes").GetInt64() > 0, "Memory counters");
        Require(json.Contains("FileNotFoundException") && !json.Contains("Alice") && !json.Contains("holiday.mp4") && !json.Contains("C:\\\\Users") && !json.Contains("CustomerName"), "Diagnostics excludes messages and personal paths");
        Require(!Directory.EnumerateFiles(root, "diagnostics.json.*.tmp").Any(), "Diagnostics export removes temporary files");
        var importer = new AssetImportService();
        var assetCount = workspace.Document.Assets.Count;
        var prepared = importer.PrepareVideoAsync(workspace, fixture).GetAwaiter().GetResult();
        Require(File.Exists(prepared.LocalPath) && workspace.Document.Assets.Count == assetCount, "Preparing video must not mutate UI collections");
        var before = Directory.EnumerateFiles(workspace.AssetsDirectory, "*", SearchOption.AllDirectories).Count();
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { importer.PrepareVideoAsync(workspace, fixture, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Cancelled import must not succeed"); }
            catch (OperationCanceledException) { }
        }
        Require(Directory.EnumerateFiles(workspace.AssetsDirectory, "*", SearchOption.AllDirectories).Count() == before, "Cancelled import removes staged files");
        widget.RuntimeMediaStatus = "Transient status";
        var serialized = JsonSerializer.Serialize(widget);
        var reopened = JsonSerializer.Deserialize<WidgetModel>(serialized)!;
        Require(reopened.MediaPaused == widget.MediaPaused && reopened.PlaybackSpeed == widget.PlaybackSpeed && !serialized.Contains("Transient status"), "Playback settings serialize; runtime status does not");
        using (var info = new ThemePackageService().CreateNewWorkspace())
        {
            var embedded = importer.Import(info, fixture, ThemeAssetKind.Video);
            info.Document.Widgets.Add(new WidgetModel { Type = WidgetType.Video, AssetId = embedded.Id, MediaPaused = true });
            info.Document.MinimumAppVersion = "0.10.0-alpha.14";
            var package = new ThemePackageService(); var themePath = Path.Combine(root, "video-package.t3theme");
            var saving = package.SaveAsync(info, themePath);
            pump(() => saving.IsCompleted, TimeSpan.FromSeconds(10)); saving.GetAwaiter().GetResult();
            var loading = package.LoadAsync(themePath);
            pump(() => loading.IsCompleted, TimeSpan.FromSeconds(10));
            using var reopenedInfo = loading.GetAwaiter().GetResult();
            reopenedInfo.Document.RuntimeMode = RuntimeScreenMode.InfoScreen;
            Require(reopenedInfo.Document.Widgets.Single().MediaPaused && File.Exists(reopenedInfo.GetAbsolutePath(reopenedInfo.Document.Assets.Single())), "Info Screen package preserves video bytes and pause settings");
            pump(() => { using var frame = new ThemeRenderer().Render(reopenedInfo); return reopenedInfo.Document.Widgets.Single().RuntimeMediaStatus.Contains("paused"); }, TimeSpan.FromSeconds(10));
            VideoFrameProvider.Suspend(reopenedInfo);
        }
        pump(() => VideoFrameProvider.RunningDecoders == 0, TimeSpan.FromSeconds(5));
        using (var hybrid = new ThemePackageService().CreateNewWorkspace())
        {
            hybrid.Document.Mode = ScreenMode.Hybrid;
            var linked = importer.DescribeLinkedVideo(fixture);
            var overlay = new WidgetModel { Type = WidgetType.Video, MediaSourcePath = linked.LocalPath!, MediaPaused = true };
            hybrid.Document.HybridWidgets.Add(overlay);
            var library = new ModeThemeService(Path.Combine(root, "video-linked-themes"));
            var saved = library.Save(hybrid.Document, ScreenMode.Hybrid, "Linked video");
            using var reopenedHybrid = new ThemePackageService().CreateNewWorkspace();
            reopenedHybrid.Document.Mode = ScreenMode.Hybrid; reopenedHybrid.Document.RuntimeMode = RuntimeScreenMode.Hybrid;
            ModeThemeService.Apply(reopenedHybrid.Document, library.Load(saved));
            Require(reopenedHybrid.Document.HybridWidgets.Single().MediaSourcePath == fixture && reopenedHybrid.Document.Assets.Count == 0, "Hybrid settings theme preserves video link without embedding media");
            pump(() => { using var frame = new ThemeRenderer().Render(reopenedHybrid); return reopenedHybrid.Document.HybridWidgets.Single().RuntimeMediaStatus.Contains("paused"); }, TimeSpan.FromSeconds(10));
            Require(VideoFrameProvider.CachedFrameBytes > 0, "Reopened Hybrid video link decodes");
            VideoFrameProvider.Suspend(reopenedHybrid);
        }
        Require(File.Exists(fixture), "Linked-video cleanup must never delete the source");
        pump(() => VideoFrameProvider.RunningDecoders == 0, TimeSpan.FromSeconds(5));
        var clone = widget.Clone(); Require(clone.MediaPaused == widget.MediaPaused && clone.PlaybackSpeed == 2, "Playback controls persist in clones");
        Console.WriteLine("PASS: real H.264 decoding, RGB/stride/orientation, bounded frames, pause, playback, EOF, loop, speed, two-video limit, inactive cleanup, bad media and private local diagnostics.");
    }

    private static void CheckFragmentedVideo(Action<Func<bool>, TimeSpan> pump, string fileName, int sourceWidth, int sourceHeight)
    {
        // Matches the reported clip's fragmented MP4, High-profile H.264, B-frames,
        // BT.709 and 1280x720 at 30000/1001 fps without using customer media.
        foreach (var display in new[] { DisplayCanvasSize.Turing52, DisplayCanvasSize.Serial35 })
        {
            using var workspace = new ThemePackageService().CreateNewWorkspace();
            workspace.Document.CanvasWidth = display == DisplayCanvasSize.Turing52 ? 1280 : 480;
            workspace.Document.CanvasHeight = display == DisplayCanvasSize.Turing52 ? 720 : 320;
            var asset = new AssetImportService().Import(workspace, Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName), ThemeAssetKind.Video);
            var widget = new WidgetModel { Type = WidgetType.Video, AssetId = asset.Id, MediaPaused = true, Loop = false, PlaybackSpeed = 2 };
            VideoFrameProvider.Clear(); VideoFrameProvider.Synchronize(workspace, [widget]);
            string status = "";
            pump(() => {
                using var frame = VideoFrameProvider.GetFrame(workspace, widget, display, 0, out status);
                if (frame is null)
                {
                    if (!status.StartsWith("Opening")) throw new InvalidOperationException($"Fragmented 720p {display}: {status}");
                    return false;
                }
                Require(frame.Width == workspace.Document.CanvasWidth && frame.Height == (display == DisplayCanvasSize.Turing52 ? 720 : 270), "Fragmented video output respects canvas bounds");
                var top = frame.GetPixel(frame.Width / 2, 10); var bottom = frame.GetPixel(frame.Width / 2, frame.Height - 10);
                Require(top.Red > 180 && top.Blue < 70 && bottom.Blue > 180 && bottom.Red < 70, "Fragmented 720p RGB orientation");
                return true;
            }, TimeSpan.FromSeconds(15));
            Require(status.Contains("paused"), "Fragmented video paused first frame");
            var details = VideoFrameProvider.GetDiagnostics().Single();
            Require(details.SourceWidth == sourceWidth && details.SourceHeight == sourceHeight && details.OutputWidth == workspace.Document.CanvasWidth && details.PublishedFrames == 1, "Active decoder diagnostics describe bounded first frame");
            Require(VideoFrameProvider.CachedFrameBytes <= (long)workspace.Document.CanvasWidth * workspace.Document.CanvasHeight * 4, "4K never becomes a cached 4K bitmap");
            if (display == DisplayCanvasSize.Turing52) Require(details.FormatChanges > 0, "Regression fixture must exercise the formerly rejected output-format notification");
            Console.WriteLine($"Fragmented {display}: output {details.OutputWidth}x{details.OutputHeight}, stride {details.Stride}, format notifications {details.FormatChanges}");
            var activeJson = JsonSerializer.Serialize(DiagnosticsService.CreateReport(new { connected = false }, new { activeVideos = 1 }));
            Require(activeJson.Contains("videoPlayback") && activeJson.Contains("PublishedFrames") && !activeJson.Contains(workspace.RootDirectory) && !activeJson.Contains("video-fragmented"), "Active diagnostics exclude source and workspace paths");
            widget.MediaPaused = false;
            pump(() => {
                using var frame = VideoFrameProvider.GetFrame(workspace, widget, display, 0, out status);
                if (frame is null) throw new InvalidOperationException("Fragmented playback failed: " + status);
                var colour = frame.GetPixel(frame.Width / 2, 10);
                return colour.Green > 70 && colour.Red < 70;
            }, TimeSpan.FromSeconds(10));
            pump(() => { using var frame = VideoFrameProvider.GetFrame(workspace, widget, display, 0, out status); return status.Contains("ended"); }, TimeSpan.FromSeconds(5));
            widget.Loop = true; // A stopped decoder must reopen on a playback control edit.
            var sawGreen = false;
            pump(() => {
                using var frame = VideoFrameProvider.GetFrame(workspace, widget, display, 0, out status);
                if (frame is null) return false;
                var colour = frame.GetPixel(frame.Width / 2, 10);
                if (colour.Green > 70 && colour.Red < 70) sawGreen = true;
                return sawGreen && colour.Red > 180 && colour.Green < 70;
            }, TimeSpan.FromSeconds(10));
            VideoFrameProvider.Suspend(workspace);
            pump(() => VideoFrameProvider.RunningDecoders == 0, TimeSpan.FromSeconds(5));
        }
        Console.WriteLine($"PASS: fragmented High-profile {sourceWidth}x{sourceHeight} H.264, USB/serial bounded scaling, pause, movement, EOF, recovery through Loop edit and cleanup.");
    }

    private static void CheckLiveFpsChanges(Action<Func<bool>, TimeSpan> pump)
    {
        using var workspace = new ThemePackageService().CreateNewWorkspace();
        workspace.Document.CanvasWidth = 1280; workspace.Document.CanvasHeight = 720;
        var asset = new AssetImportService().Import(workspace, Path.Combine(AppContext.BaseDirectory, "Fixtures", "video-fragmented-720p.mp4"), ThemeAssetKind.Video);
        var widget = new WidgetModel { Type = WidgetType.Video, AssetId = asset.Id, Loop = true, TargetFps = 20 };
        VideoFrameProvider.Clear(); VideoFrameProvider.Synchronize(workspace, [widget]);
        long priorFrames = 0;
        foreach (var fps in new[] { 20, 21, 22, 20, 0, 5, 20 })
        {
            widget.TargetFps = fps; widget.AutoOptimize = true;
            pump(() => {
                using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 31, out var status);
                if (frame is null && !status.StartsWith("Opening")) throw new InvalidOperationException("FPS edit stopped playback: " + status);
                return VideoFrameProvider.GetDiagnostics().Single().PublishedFrames >= priorFrames + 4;
            }, TimeSpan.FromSeconds(5));
            var details = VideoFrameProvider.GetDiagnostics().Single();
            Require(details.RequestedFps == fps && details.EffectiveFps == (fps == 0 ? 12 : Math.Min(fps, 20)) && details.Code == "None" && !details.Stopped, "Manual/Auto rate diagnostics and continuous playback");
            Require(details.ReadSamples > 0 && details.MillisecondsSinceLastFrame < 1000, "Diagnostics identify ongoing frame progress");
            priorFrames = details.PublishedFrames;
        }
        widget.Loop = false;
        pump(() => { using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 31, out var status); return status.Contains("ended"); }, TimeSpan.FromSeconds(5));
        widget.TargetFps = 22;
        pump(() => { using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 31, out _); var details = VideoFrameProvider.GetDiagnostics().Single(); return !details.Stopped && details.PublishedFrames > 1; }, TimeSpan.FromSeconds(5));
        VideoFrameProvider.Restart(workspace, widget);
        widget.MediaPaused = true;
        pump(() => { using var frame = VideoFrameProvider.GetFrame(workspace, widget, DisplayCanvasSize.Turing52, 31, out var status); return frame is not null && status.Contains("paused"); }, TimeSpan.FromSeconds(5));
        Require(VideoFrameProvider.GetDiagnostics().Single().PublishedFrames == 1, "Restart recovers without replacing the widget");
        VideoFrameProvider.Suspend(workspace); pump(() => VideoFrameProvider.RunningDecoders == 0, TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS: live FPS edits 20/21/22/20/Auto/5/20, continued looped playback, stopped-worker FPS recovery and explicit Restart without reimport.");
    }
}
