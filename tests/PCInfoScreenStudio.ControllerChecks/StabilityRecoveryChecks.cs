using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;

internal static class StabilityRecoveryChecks
{
    public static void Check(string root, Action<Task> awaitTask)
    {
        CheckSettings(root, awaitTask);
        CheckSessions(root, awaitTask);
        CheckFileLifetime(root, awaitTask);
    }

    private static void CheckSettings(string root, Action<Task> awaitTask)
    {
        var path = Path.Combine(root, "recovery-settings.json");
        var service = new AppSettingsService(path);
        var first = new AppSettings { WeatherCity = "First", CloseToTray = false, AutoStartDisplay = false, SerialBrightness = 31, UsbBrightness = 72 };
        service.Save(first);
        Require(File.Exists(path + ".bak"), "First validated settings save establishes a backup");
        var second = new AppSettings { WeatherCity = "Second", SerialBrightness = 42 };
        service.Save(second);
        var backup = Hash(path + ".bak");
        File.WriteAllText(path, "{broken");
        var recovered = service.Load();
        Require(recovered.WeatherCity == "First" && recovered.SerialBrightness == 31 && recovered.UsbBrightness == 72 && !recovered.CloseToTray && !recovered.AutoStartDisplay,
            "Corrupt settings must recover the previous validated values, including user opt-outs");
        Require(!string.IsNullOrWhiteSpace(service.RecoveryNotice) && Directory.EnumerateFiles(root, "recovery-settings.json.unreadable-*").Any(), "Recovery explains itself and retains the bad file");
        Require(Hash(path + ".bak").SequenceEqual(backup), "Recovery cannot overwrite its good backup with corrupt data");
        File.WriteAllText(path, "[]");
        service.Save(second);
        Require(Hash(path + ".bak").SequenceEqual(backup), "Saving after a corrupt primary must preserve the good backup");
        var primary = Hash(path);
        try { service.Save(new AppSettings { SettingsVersion = 99 }); throw new Exception("Invalid settings accepted"); } catch (InvalidDataException) { }
        Require(Hash(path).SequenceEqual(primary) && Hash(path + ".bak").SequenceEqual(backup), "Rejected writes preserve both valid copies");
        File.WriteAllText(path + ".interrupted.tmp", "partial");
        Require(service.Load().WeatherCity == "Second", "Interrupted pending files are never promoted on startup");
        File.Delete(path);
        Require(service.Load().WeatherCity == "First", "A missing primary must also recover from backup");
        File.WriteAllText(path, "null"); File.WriteAllText(path + ".bak", "not JSON");
        Require(service.Load().SerialBrightness == 50 && !string.IsNullOrEmpty(service.RecoveryNotice) && File.ReadAllText(path) == "null",
            "Two unreadable copies must remain available for diagnosis while defaults are explicit");
        var legacy = Path.Combine(root, "legacy-recovery-settings.json");
        File.WriteAllText(legacy, "{\"SettingsVersion\":0,\"WeatherCity\":\"Legacy\"}");
        var migrated = new AppSettingsService(legacy).Load();
        Require(migrated.SettingsVersion == 5 && migrated.WeatherCity == "Legacy" && File.Exists(legacy + ".bak"), "Older settings still migrate and gain a valid backup");
        var concurrentPath = Path.Combine(root, "concurrent-settings.json");
        awaitTask(Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => new AppSettingsService(concurrentPath).Save(new AppSettings { WeatherCity = "Writer " + i })))));
        Require(new AppSettingsService(concurrentPath).Load().WeatherCity.StartsWith("Writer ") && JsonDocument.Parse(File.ReadAllText(concurrentPath + ".bak")).RootElement.ValueKind == JsonValueKind.Object,
            "Different service instances serialize writes to the same settings file");
    }

    private static void CheckSessions(string root, Action<Task> awaitTask)
    {
        var directory = Path.Combine(root, "recovery-session"); var path = Path.Combine(directory, "last-session.t3theme");
        var package = new ThemePackageService(); using var workspace = package.CreateNewWorkspace();
        var source = Path.Combine(root, "retained-session-image.png"); File.WriteAllText(source, "passive asset bytes");
        var image = new AssetImportService().Import(workspace, source, ThemeAssetKind.Image);
        workspace.Document.Widgets.Add(new WidgetModel { Type = WidgetType.Image, AssetId = image.Id });
        workspace.Document.Name = "First unsaved";
        var state = new SessionState { InfoThemePath = "original-theme.t3theme", IsHardwarePage = true,
            Themes = new ThemeSessionState { DirtyModes = [ScreenMode.InfoScreen], PhotoSettings = new() { [ScreenMode.PhotoFrame] = new PhotoFrameSettings() } } };
        state.Themes.PhotoSettings[ScreenMode.PhotoFrame].Photos.Add(new PhotoFrameItem { AssetId = image.Id });
        var sessions = new SessionStateService(directory);
        awaitTask(sessions.SaveAsync(workspace, state));
        workspace.Document.Name = "Second unsaved";
        awaitTask(sessions.SaveAsync(workspace, state));
        var backup = Hash(path + ".bak");
        using (var file = File.Create(path)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create)) { }
        var loading = sessions.LoadAsync(); awaitTask(loading);
        using (var restored = loading.Result!.Workspace)
        {
            Require(restored.Document.Name == "First unsaved" && restored.IsDirty && restored.FilePath == state.InfoThemePath && loading.Result.State.IsHardwarePage,
                "Corrupt private resume storage recovers the previous document, unsaved state and page");
            Require(restored.Document.Assets.Count == 1 && File.ReadAllText(restored.GetAbsolutePath(restored.Document.Assets[0])) == "passive asset bytes" &&
                loading.Result.State.Themes.PhotoSettings[ScreenMode.PhotoFrame].Photos.Single().AssetId == image.Id, "Recovery retains embedded media and inactive mode photo references");
        }
        Require(sessions.RecoveryNotice.Contains("backup") && Hash(path + ".bak").SequenceEqual(backup) && Directory.EnumerateFiles(directory, "last-session.t3theme.unreadable-*").Any(),
            "Session recovery preserves the unreadable archive and its good backup");
        var primary = Hash(path);
        try { awaitTask(sessions.SaveAsync(workspace, new SessionState { Version = 99 })); throw new Exception("Invalid state accepted"); } catch (InvalidDataException) { }
        Require(Hash(path).SequenceEqual(primary) && Hash(path + ".bak").SequenceEqual(backup), "Invalid session metadata cannot replace a valid primary or backup");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { awaitTask(sessions.SaveAsync(workspace, state, cancellation.Token)); throw new Exception("Cancelled state accepted"); } catch (OperationCanceledException) { }
        }
        Require(Hash(path).SequenceEqual(primary) && !Directory.EnumerateFiles(directory, "last-session.t3theme.*.tmp").Any(), "Cancelled session writes preserve existing copies and clean temporary files");
        File.WriteAllText(path, "broken primary");
        workspace.Document.Name = "Third unsaved";
        awaitTask(sessions.SaveAsync(workspace, state));
        Require(Hash(path + ".bak").SequenceEqual(backup), "A corrupt primary cannot poison a good session backup during save");
        var queuedDirectory = Path.Combine(root, "queued-sessions");
        workspace.Document.Name = "Queued first";
        var one = new SessionStateService(queuedDirectory).SaveAsync(workspace, state);
        workspace.Document.Name = "Queued second";
        var two = new SessionStateService(queuedDirectory).SaveAsync(workspace, state);
        awaitTask(Task.WhenAll(one, two));
        var queued = new SessionStateService(queuedDirectory).LoadAsync(); awaitTask(queued);
        using (var restored = queued.Result!.Workspace) Require(restored.Document.Name == "Queued second", "Queued sessions serialize complete frozen snapshots in request order");
        File.Delete(path);
        var missing = sessions.LoadAsync(); awaitTask(missing); missing.Result!.Workspace.Dispose();
        Require(File.Exists(path), "Missing session primary must be restored from the good backup");
        File.WriteAllText(path, "broken"); File.WriteAllText(path + ".bak", "broken backup");
        var failed = sessions.LoadAsync(); awaitTask(failed);
        Require(failed.Result is null && sessions.RecoveryNotice.Contains("could not be read") && File.ReadAllText(path) == "broken", "Two invalid session copies are retained rather than silently replaced");
    }

    private static void CheckFileLifetime(string root, Action<Task> awaitTask)
    {
        var package = new ThemePackageService(); var workspace = package.CreateNewWorkspace();
        var source = Path.Combine(root, "leased-source.png"); File.WriteAllText(source, "leased asset bytes");
        var asset = new AssetImportService().Import(workspace, source, ThemeAssetKind.Image);
        workspace.Document.Widgets.Add(new WidgetModel { Type = WidgetType.Image, AssetId = asset.Id });
        var directory = workspace.RootDirectory; var saved = Path.Combine(root, "leased-save.t3theme");
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var progress = new InlineProgress(_ => { started.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); });
        var saving = package.SaveAsync(workspace, saved, progress: progress);
        Require(started.Wait(TimeSpan.FromSeconds(5)), "Save reached asset streaming");
        workspace.Dispose();
        Require(Directory.Exists(directory) && File.Exists(workspace.GetAbsolutePath(asset)), "Disposing a workspace must defer deletion while a snapshot reads its files");
        release.Set(); awaitTask(saving);
        Require(!Directory.Exists(directory), "The retained workspace is cleaned after its final reader finishes");
        var loaded = package.LoadAsync(saved); awaitTask(loaded);
        using (var restored = loaded.Result) Require(File.ReadAllText(restored.GetAbsolutePath(restored.Document.Assets.Single())) == "leased asset bytes", "A save survives workspace disposal with its source media intact");
    }

    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    private static byte[] Hash(string path) => SHA256.HashData(File.ReadAllBytes(path));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
