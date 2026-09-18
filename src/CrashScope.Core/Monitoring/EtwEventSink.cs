namespace CrashScope.Core.Monitoring;

/// <summary>One durable write per matched event; the bounded queue is only a handoff, never a retry log.</summary>
internal sealed class EtwEventSink<T>(EtwCoverageTracker coverage, Action<T> write, IDisposable? writer = null, int capacity = 5000) : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<T> _queue = new();
    private bool _stopped;
    public int Count { get { lock (_gate) return _queue.Count; } }

    public void Record(T item, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_stopped) return;
            coverage.Matched();
            try { write(item); coverage.Written(at); }
            catch (Exception ex) { coverage.Error("Event write failed", ex); }
            _queue.Enqueue(item);
            while (_queue.Count > capacity) { _queue.Dequeue(); coverage.Dropped(); }
        }
    }

    public IReadOnlyList<T> Drain()
    {
        lock (_gate) { var events = _queue.ToArray(); _queue.Clear(); return events; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            _queue.Clear(); // Already persisted (or explicitly failed). Never duplicate records on stop.
            writer?.Dispose();
        }
    }
}
