using System.Collections.Concurrent;

namespace PCInfoScreenStudio.Services;

internal static class RecoveryFileStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static SemaphoreSlim Gate(string path) => Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));
    public static string Pending(string path) => path + "." + Guid.NewGuid().ToString("N") + ".tmp";

    public static void PreserveUnreadable(string path)
    {
        if (File.Exists(path)) File.Move(path, path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
    }

    // The caller validates the complete pending file and existing candidates while holding Gate.
    public static void Commit(string pending, string path, bool primaryValid, bool backupValid)
    {
        var backup = path + ".bak";
        if (File.Exists(path) && primaryValid)
        {
            if (!backupValid) PreserveUnreadable(backup);
            File.Replace(pending, path, backup, ignoreMetadataErrors: true);
            return;
        }
        PreserveUnreadable(path);
        File.Move(pending, path);
        if (backupValid) return;
        PreserveUnreadable(backup);
        var backupPending = Pending(backup);
        try { File.Copy(path, backupPending); File.Move(backupPending, backup); }
        finally { if (File.Exists(backupPending)) File.Delete(backupPending); }
    }

    public static void Restore(string path)
    {
        var pending = Pending(path);
        try
        {
            File.Copy(path + ".bak", pending);
            PreserveUnreadable(path);
            File.Move(pending, path);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
