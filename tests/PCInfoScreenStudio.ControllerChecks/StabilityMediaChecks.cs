using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;
using SkiaSharp;

internal static class StabilityMediaChecks
{
    public static void Check(string root)
    {
        var budget = MediaResourceCache.BudgetBytes;
        try
        {
            ThemeRenderer.ClearCaches(); MediaResourceCache.BudgetBytes = 1024 * 1024;
            var a = new Tracked(); var b = new Tracked(); var c = new Tracked(); var loads = 0;
            using (var retained = MediaResourceCache.Acquire("a", "image", "a", () => { loads++; return a; }, _ => 512 * 1024)) { }
            using (var retained = MediaResourceCache.Acquire("b", "gif", "b", () => b, _ => 512 * 1024)) { }
            using (var retained = MediaResourceCache.Acquire("a", "image", "a", () => { loads++; return new Tracked(); }, _ => 512 * 1024)) { }
            using (var retained = MediaResourceCache.Acquire("c", "photo", "c", () => c, _ => 512 * 1024)) { }
            Require(loads == 1 && b.Disposed && !a.Disposed && Bytes("retainedBytes") <= MediaResourceCache.BudgetBytes,
                "One shared budget must reuse active entries and evict least-recently-used resources across categories");
            using (var pinned = MediaResourceCache.Acquire("a", "image", "a", () => new Tracked(), _ => 512 * 1024))
            {
                MediaResourceCache.Clear();
                Require(!a.Disposed && Bytes("retainedBytes") == 512 * 1024, "Clear must defer disposing an actively leased resource");
            }
            Require(a.Disposed && Bytes("retainedBytes") == 0, "The last lease releases deferred bytes and disposes the resource");
            var oversized = new Tracked();
            using (var temporary = MediaResourceCache.Acquire("large", "image", "large", () => oversized, _ => 2 * 1024 * 1024))
                Require(Bytes("retainedBytes") == 0 && Bytes("temporaryBytes") == 2 * 1024 * 1024, "Over-budget resources are tracked only for the current draw, not retained");
            Require(oversized.Disposed && Bytes("temporaryBytes") == 0, "Temporary resources are released after drawing");
            MediaResourceCache.RetainResources([("gif", "active")]);
            var active = new Tracked();
            using (var retained = MediaResourceCache.Acquire("active", "gif", "active", () => active, _ => 1024 * 1024)) { }
            using (var retained = MediaResourceCache.Acquire("extra", "image", "extra", () => new Tracked(), _ => 512 * 1024)) { }
            Require(!active.Disposed && Bytes("retainedBytes") == 1024 * 1024, "A larger working set cannot repeatedly evict and restart its already-active GIF");
            MediaResourceCache.Clear();
            CheckGifLeases();
            CheckRenderingWorkloads(root);
        }
        finally { ThemeRenderer.ClearCaches(); MediaResourceCache.BudgetBytes = budget; }
    }

    private static void CheckGifLeases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "disposal-1.gif");
        using (var retained = AnimatedGifFrameProvider.AcquireFrame(path, 1, 60, true))
        {
            Require(retained is not null, "GIF fixture loads");
            var pixels = retained!.Value.Pixels;
            AnimatedGifFrameProvider.Clear();
            Require(retained.Value.Pixels.SequenceEqual(pixels), "Clearing a cache cannot dispose or change a GIF frame being drawn");
        }
        using (var snapshot = AnimatedGifFrameProvider.GetFrame(path, 1, 60, true))
        {
            Require(snapshot is not null, "An owned GIF snapshot can be acquired");
            var pixels = snapshot!.Pixels;
            AnimatedGifFrameProvider.Clear();
            Require(snapshot.Pixels.SequenceEqual(pixels), "Public GIF snapshots outlive the decoder/cache that produced them");
        }
        var starts = (ConcurrentDictionary<string, long>)typeof(AnimatedGifFrameProvider).GetField("Started", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        starts[path] = Environment.TickCount64 - 1250;
        MediaResourceCache.Clear("gif");
        using var recovered = AnimatedGifFrameProvider.GetFrame(path, 1, 1, true);
        using var reference = SKCodec.Create(path)!;
        using var expected = new SKBitmap(160, 100, SKColorType.Bgra8888, SKAlphaType.Premul); expected.Erase(SKColors.Transparent);
        var decoded = reference.GetPixels(expected.Info, expected.GetPixels(), new SKCodecOptions(10));
        Require(decoded == SKCodecResult.Success && recovered is not null && recovered.Pixels.SequenceEqual(expected.Pixels), "Recreating an evicted GIF decoder preserves its media timeline and exact composition");
        ThemeRenderer.ClearCaches();
    }

    private static void CheckRenderingWorkloads(string root)
    {
        MediaResourceCache.BudgetBytes = 8L * 1024 * 1024;
        var package = new ThemePackageService(); using var workspace = package.CreateNewWorkspace();
        var importer = new AssetImportService(); var images = new List<ThemeAsset>();
        for (var i = 0; i < 30; i++)
        {
            var path = Path.Combine(root, "cache-photo-" + i + ".jpg");
            using var bitmap = new SKBitmap(512, 320); bitmap.Erase(new SKColor((byte)(30 + i * 5), (byte)(80 + i), 120));
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85); File.WriteAllBytes(path, encoded.ToArray());
            images.Add(importer.Import(workspace, path, ThemeAssetKind.Image));
            workspace.Document.PhotoFrame.Photos.Add(new PhotoFrameItem { SourcePath = path });
        }
        var gif = importer.Import(workspace, Path.Combine(AppContext.BaseDirectory, "Fixtures", "disposal-2.gif"), ThemeAssetKind.Gif);
        var gifTwo = importer.Import(workspace, Path.Combine(AppContext.BaseDirectory, "Fixtures", "disposal-3.gif"), ThemeAssetKind.Gif);
        var imageWidget = new WidgetModel { Type = WidgetType.Image, AssetId = images[0].Id, Width = 480, Height = 320 };
        workspace.Document.Widgets.Add(imageWidget);
        workspace.Document.HybridWidgets.Add(new WidgetModel { Type = WidgetType.AnimatedImage, AssetId = gif.Id, Width = 160, Height = 100 });
        workspace.Document.HybridWidgets.Add(new WidgetModel { Type = WidgetType.AnimatedImage, AssetId = gifTwo.Id, X = 180, Width = 160, Height = 100 });
        var renderer = new ThemeRenderer();
        GC.Collect(); GC.WaitForPendingFinalizers();
        var before = Process.GetCurrentProcess().PrivateMemorySize64;
        var timer = Stopwatch.StartNew(); long maximum = 0;
        for (var i = 0; i < 500; i++)
        {
            workspace.Document.RuntimeMode = (RuntimeScreenMode)(i % 3);
            imageWidget.AssetId = images[i % images.Count].Id;
            workspace.Document.PhotoFrame.RuntimeCurrentIndex = i % images.Count;
            workspace.Document.PhotoFrame.RuntimePreviousIndex = (i + images.Count - 1) % images.Count;
            workspace.Document.PhotoFrame.RuntimeTransition = PhotoTransition.Crossfade;
            workspace.Document.PhotoFrame.RuntimeTransitionProgress = .5;
            using var frame = renderer.Render(workspace);
            Require(frame.Width == 480 && frame.Height == 320 && Bytes("retainedBytes") <= MediaResourceCache.BudgetBytes,
                "Repeated album/image/GIF/mode changes retain valid output within one total cache budget");
            maximum = Math.Max(maximum, Bytes("retainedBytes"));
        }
        workspace.Document.RuntimeMode = RuntimeScreenMode.InfoScreen;
        imageWidget.AssetId = images[0].Id;
        using (var frame = renderer.Render(workspace)) { }
        var loads = Bytes("loads");
        for (var i = 0; i < 30; i++) using (var frame = renderer.Render(workspace)) { }
        Require(Bytes("loads") == loads, "The same active image cannot be decoded again every preview frame");
        workspace.Document.Widgets.Add(new WidgetModel { Type = WidgetType.Image, AssetId = images[0].Id, Width = 120, Height = 80 });
        using (var frame = renderer.Render(workspace)) { }
        loads = Bytes("loads");
        for (var i = 0; i < 30; i++) using (var frame = renderer.Render(workspace)) { }
        Require(Bytes("loads") == loads, "Two widget sizes using the same image cannot evict and decode each other's variant every frame");
        workspace.Document.Widgets.Clear();
        ThemeRenderer.SynchronizeMedia(workspace);
        Require(Bytes("retainedBytes") == 0 && File.Exists(workspace.GetAbsolutePath(images[0])), "Removing widgets evicts decoded media but preserves its source for Undo");
        workspace.Document.Widgets.Add(imageWidget);
        using (var frame = renderer.Render(workspace)) Require(frame.GetPixel(20, 20).Red > 20, "Undo can reuse retained source files after cache eviction");
        ThemeRenderer.ClearCaches(); GC.Collect(); GC.WaitForPendingFinalizers();
        Require(Bytes("retainedBytes") == 0 && Bytes("temporaryBytes") == 0, "The stress workload leaves no live media allocations after cleanup");
        Console.WriteLine($"MEDIA STRESS: 30 photos, two GIFs, 500 mode frames; {timer.ElapsedMilliseconds} ms; peak retained {maximum} bytes under 8 MiB test budget; private bytes before {before}, after {Process.GetCurrentProcess().PrivateMemorySize64}. Process bytes include runtime/native allocator overhead.");
    }

    private static long Bytes(string property) => JsonSerializer.SerializeToElement(MediaResourceCache.Diagnostics).GetProperty(property).GetInt64();
    private sealed class Tracked : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
