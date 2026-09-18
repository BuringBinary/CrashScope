using CrashScope.Core.Infrastructure;

namespace CrashScope.Core.Monitoring;

public sealed record CollectorHealth(string Collector, string Status, string Detail, DateTimeOffset ChangedAt, EtwCoverageSnapshot? Etw = null);

/// <summary>Last observed collection state. Started does not imply that any matching event was seen.</summary>
internal sealed class CollectorHealthTracker(string path)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CollectorHealth> _items = new(StringComparer.Ordinal);

    public IReadOnlyList<CollectorHealth> Snapshot()
    {
        lock (_gate) return _items.Values.OrderBy(x => x.Collector).ToArray();
    }

    public void Set(string collector, string status, string detail, EtwCoverageSnapshot? etw = null)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(collector, out var old) && old.Status == status && old.Detail == detail && old.Etw == etw) return;
            var next = new CollectorHealth(collector, status, detail, DateTimeOffset.UtcNow, etw);
            var snapshot = _items.Values.Where(x => x.Collector != collector).Append(next).OrderBy(x => x.Collector).ToArray();
            AtomicJsonFile.Write(path, snapshot);
            _items[collector] = next; // Publish in memory only after durable persistence succeeds.
        }
    }
}
