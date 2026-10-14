namespace PCInfoScreenStudio.Rendering;

public sealed class MediaLease<T>(T value, Action release) : IDisposable where T : class
{
    public T Value { get; } = value;
    private Action? _release = release;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

/// <summary>A shared retained-byte budget for images, prepared photos and GIF resources.</summary>
public static class MediaResourceCache
{
    private sealed class Entry(string key, string category, string source, IDisposable value, long bytes)
    {
        public string Key = key; public string Category = category; public string Source = source;
        public IDisposable Value = value; public long Bytes = bytes; public int Pins;
        public bool Removed; public LinkedListNode<Entry>? Node;
    }
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<Entry> Lru = new();
    private static HashSet<string> ActiveResources = new(StringComparer.OrdinalIgnoreCase);
    private static long _budget = 128L * 1024 * 1024;
    private static long _resident, _temporary, _peak, _hits, _loads, _evictions;
    public static long BudgetBytes
    {
        get { lock (Sync) return _budget; }
        set { if (value < 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(value)); lock (Sync) { _budget = value; MakeRoom(0); } }
    }
    public static object Diagnostics { get { lock (Sync) return new { budgetBytes = _budget, retainedBytes = _resident, temporaryBytes = _temporary, peakTrackedBytes = _peak, entries = Entries.Count, hits = _hits, loads = _loads, evictions = _evictions }; } }

    public static MediaLease<T> Acquire<T>(string key, string category, string source, Func<T> create, Func<T, long> size) where T : class, IDisposable
    {
        lock (Sync)
        {
            if (Entries.TryGetValue(key, out var existing))
            {
                _hits++; existing.Pins++; Lru.Remove(existing.Node!); existing.Node = Lru.AddFirst(existing);
                return new((T)existing.Value, () => Release(existing));
            }
            foreach (var stale in Entries.Values.Where(e => e.Category == category && e.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray()) Remove(stale);
            var value = create(); long bytes;
            try { bytes = size(value); if (bytes < 0) throw new InvalidDataException("Invalid media allocation size."); }
            catch { value.Dispose(); throw; }
            _loads++;
            MakeRoom(bytes);
            if (bytes > _budget || _resident + bytes > _budget)
            {
                // A caller still draws this resource, but it is never retained beyond its lease.
                _temporary += bytes; _peak = Math.Max(_peak, _resident + _temporary);
                return new(value, () => { lock (Sync) { _temporary -= bytes; value.Dispose(); } });
            }
            var entry = new Entry(key, category, source, value, bytes) { Pins = 1 };
            entry.Node = Lru.AddFirst(entry); Entries.Add(key, entry); _resident += bytes;
            _peak = Math.Max(_peak, _resident + _temporary);
            return new(value, () => Release(entry));
        }
    }

    private static void MakeRoom(long incoming)
    {
        var node = Lru.Last;
        while (_resident + incoming > _budget && node is not null)
        {
            var previous = node.Previous;
            if (node.Value.Pins == 0 && (_resident > _budget || !ActiveResources.Contains(node.Value.Category + "|" + node.Value.Source))) Remove(node.Value);
            node = previous;
        }
    }
    private static void Remove(Entry entry)
    {
        if (entry.Removed) return;
        entry.Removed = true; Entries.Remove(entry.Key); Lru.Remove(entry.Node!); _evictions++;
        if (entry.Pins == 0) { _resident -= entry.Bytes; entry.Value.Dispose(); }
    }
    private static void Release(Entry entry)
    {
        lock (Sync)
        {
            entry.Pins--;
            if (entry.Removed && entry.Pins == 0) { _resident -= entry.Bytes; entry.Value.Dispose(); }
            MakeRoom(0);
        }
    }
    public static void RetainSources(IEnumerable<string> paths)
    {
        var live = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (Sync) foreach (var entry in Entries.Values.Where(e => !live.Contains(e.Source)).ToArray()) Remove(entry);
    }
    public static void RetainResources(IEnumerable<(string Category, string Source)> resources)
    {
        var live = resources.Select(r => r.Category + "|" + r.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (Sync)
        {
            ActiveResources = live;
            foreach (var entry in Entries.Values.Where(e => !live.Contains(e.Category + "|" + e.Source)).ToArray()) Remove(entry);
        }
    }
    public static void Clear(string? category = null)
    {
        lock (Sync)
        {
            if (category is null) ActiveResources.Clear();
            foreach (var entry in Entries.Values.Where(e => category is null || e.Category == category).ToArray()) Remove(entry);
        }
    }
}
