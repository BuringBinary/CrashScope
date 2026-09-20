using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class ResourceExhaustionEventTracker : IDisposable
{
    private const int QueueCapacity = 32;
    private readonly ConcurrentQueue<ResourceExhaustionEvent> _events = new();
    private EventLogWatcher? _watcher;
    private volatile bool _disposed;

    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Resource-Exhaustion-Detector'] and EventID=2004]]");
            _watcher = new EventLogWatcher(query, bookmark: null, readExistingEvents: false);
            _watcher.EventRecordWritten += OnEventRecordWritten;
            _watcher.Enabled = true;
            IsRunning = true;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}";
            Stop();
        }
    }

    public IReadOnlyList<ResourceExhaustionEvent> Drain()
    {
        var result = new List<ResourceExhaustionEvent>();
        while (_events.TryDequeue(out var item)) result.Add(item);
        return result;
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs args)
    {
        if (_disposed) return;
        if (args.EventException is not null)
        {
            LastError = $"EventLogWatcher: {args.EventException.Message}";
            return;
        }

        using var record = args.EventRecord;
        if (record is null) return;
        try
        {
            string? message = null;
            try { message = record.FormatDescription(); } catch { }
            var timestamp = record.TimeCreated is null ? DateTimeOffset.Now : new DateTimeOffset(record.TimeCreated.Value);
            var item = Parse(record.ToXml(), timestamp, record.RecordId, message);
            while (_events.Count >= QueueCapacity) _events.TryDequeue(out _);
            _events.Enqueue(item);
        }
        catch (Exception ex)
        {
            LastError = $"Event 2004 parse failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    internal static ResourceExhaustionEvent Parse(string xml, DateTimeOffset timestamp, long? recordId, string? message)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var system = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "SystemInfo");
        var processes = document.Descendants()
            .Where(e => e.Name.LocalName == "ProcessInfo")
            .SelectMany(e => e.Elements())
            .Select(ParseProcess)
            .Where(p => p is not null && p.ProcessId > 0)
            .Cast<ResourceExhaustionProcess>()
            .ToArray();

        return new ResourceExhaustionEvent(timestamp, recordId,
            Long(system, "SystemCommitLimit"), Long(system, "SystemCommitCharge"),
            Long(system, "ProcessCommitCharge"), Long(system, "PagedPoolUsage"),
            Long(system, "PhysicalMemorySize"), Long(system, "PhysicalMemoryUsage"),
            Long(system, "NonPagedPoolUsage"), Int(system, "Processes"), processes, message, xml);
    }

    private static ResourceExhaustionProcess? ParseProcess(XElement process)
    {
        var pid = Int(process, "ID") ?? 0;
        if (pid <= 0) return null;
        DateTimeOffset? creation = null;
        var text = Value(process, "CreationTime");
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            creation = parsed;
        return new ResourceExhaustionProcess(Value(process, "Name"), pid, creation,
            Long(process, "CommitCharge") ?? 0, Int(process, "HandleCount") ?? 0,
            Value(process, "Version"), Int(process, "TypeInfo"));
    }

    private static string? Value(XElement? parent, string name)
        => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
    private static long? Long(XElement? parent, string name)
        => long.TryParse(Value(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static int? Int(XElement? parent, string name)
        => int.TryParse(Value(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public void Stop()
    {
        IsRunning = false;
        if (_watcher is null) return;
        try { _watcher.Enabled = false; } catch { }
        _watcher.EventRecordWritten -= OnEventRecordWritten;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
