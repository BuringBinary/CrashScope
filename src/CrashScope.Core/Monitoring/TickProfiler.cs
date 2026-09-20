using System.Diagnostics;
using CrashScope.Core.Infrastructure;

namespace CrashScope.Core.Monitoring;

/// <summary>One tick that exceeded a budget, with per-stage wall time so the blocking collector is named.</summary>
public sealed record TickTimingRecord(
    DateTimeOffset Timestamp,
    string Reason,
    double TotalMs,
    double? GapSinceLastTickMs,
    IReadOnlyList<TickStageTiming> Stages);

public sealed record TickStageTiming(string Stage, double Ms);

/// <summary>Periodic roll-up so a healthy run still leaves a baseline to compare against.</summary>
public sealed record TickTimingSummary(
    DateTimeOffset Timestamp,
    string Reason,
    int Ticks,
    double TotalP50Ms,
    double TotalMaxMs,
    double GapMaxMs,
    IReadOnlyList<TickStageSummary> Stages);

public sealed record TickStageSummary(string Stage, double MaxMs, double MeanMs, string MaxAt);

/// <summary>
/// Watchdog for the 1 Hz recorder loop. Every stage of <c>Tick</c> is timed; a tick that runs longer
/// than <see cref="SlowTickMs"/>, a stage longer than <see cref="SlowStageMs"/>, or a scheduling gap
/// longer than <see cref="GapMs"/> since the previous tick is written to <c>tick-timing.jsonl</c>
/// with the full stage breakdown. A summary line is written every <see cref="SummaryInterval"/>.
/// </summary>
/// <remarks>
/// Motivation: the 2026-09-20 long run showed two ~6 s holes in every 1 Hz stream, two hours apart,
/// with system CPU idle - a blocked synchronous call, not load. Without stage timing the hole is
/// visible but anonymous. Cost is one Stopwatch read per stage; the file is only touched on slow
/// ticks and once per summary interval.
/// </remarks>
internal sealed class TickProfiler : IDisposable
{
    public const double SlowTickMs = 1000;
    public const double SlowStageMs = 500;
    public const double GapMs = 2500;
    public static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(5);

    private readonly CrashSafeJsonlWriter _writer;
    private readonly Action<string, string, string>? _health;
    private readonly List<TickStageTiming> _current = new(24);
    private readonly Dictionary<string, (double Max, double Sum, int N, DateTimeOffset MaxAt)> _agg = new(StringComparer.Ordinal);
    private readonly List<double> _totals = new(400);
    private readonly Stopwatch _tick = new();
    private long _lastTickStartTicks;
    private DateTimeOffset _lastSummary;
    private double _gapMax;
    private int _slowTicks;

    public TickProfiler(string path, Action<string, string, string>? health)
    {
        _writer = new CrashSafeJsonlWriter(path);
        _health = health;
        _lastSummary = DateTimeOffset.UtcNow;
        _health?.Invoke("Tick Watchdog", "Watching",
            $"Logging ticks >{SlowTickMs:F0} ms, stages >{SlowStageMs:F0} ms, gaps >{GapMs:F0} ms to tick-timing.jsonl.");
    }

    /// <summary>Call at the top of Tick. Returns the gap since the previous tick start, if any.</summary>
    public double? BeginTick()
    {
        var nowTicks = Stopwatch.GetTimestamp();
        double? gap = null;
        if (_lastTickStartTicks != 0)
            gap = Stopwatch.GetElapsedTime(_lastTickStartTicks, nowTicks).TotalMilliseconds;
        _lastTickStartTicks = nowTicks;
        _current.Clear();
        _tick.Restart();
        return gap;
    }

    public StageScope Stage(string name) => new(this, name);

    private void Record(string name, double ms)
    {
        _current.Add(new TickStageTiming(name, Math.Round(ms, 2)));
        var now = DateTimeOffset.Now;
        if (_agg.TryGetValue(name, out var a))
            _agg[name] = (ms > a.Max ? ms : a.Max, a.Sum + ms, a.N + 1, ms > a.Max ? now : a.MaxAt);
        else
            _agg[name] = (ms, ms, 1, now);
    }

    /// <summary>Call at the end of Tick (in a finally). Writes a record if any budget was exceeded.</summary>
    public void EndTick(DateTimeOffset now, double? gapMs)
    {
        _tick.Stop();
        var total = _tick.Elapsed.TotalMilliseconds;
        _totals.Add(total);
        if (gapMs is double g && g > _gapMax) _gapMax = g;

        var slowStage = _current.FirstOrDefault(s => s.Ms > SlowStageMs);
        string? reason = null;
        if (gapMs is double gap && gap > GapMs) reason = $"gap {gap:F0} ms since previous tick";
        else if (total > SlowTickMs) reason = $"tick {total:F0} ms";
        else if (slowStage is not null) reason = $"stage {slowStage.Stage} {slowStage.Ms:F0} ms";

        if (reason is not null)
        {
            _slowTicks++;
            var stages = _current.OrderByDescending(s => s.Ms).ToArray();
            try { _writer.Write(new TickTimingRecord(now, reason, Math.Round(total, 2), gapMs is null ? null : Math.Round(gapMs.Value, 2), stages)); }
            catch { }
            var top = stages.Length > 0 ? $"{stages[0].Stage}={stages[0].Ms:F0} ms" : "no stages";
            _health?.Invoke("Tick Watchdog", "SlowTickObserved",
                $"{now:HH:mm:ss} {reason}; slowest stage {top}; {_slowTicks} slow tick(s) this session.");
        }

        if (now.ToUniversalTime() - _lastSummary >= SummaryInterval)
            WriteSummary(now, "Periodic");
    }

    private void WriteSummary(DateTimeOffset now, string reason)
    {
        _lastSummary = now.ToUniversalTime();
        if (_totals.Count == 0) return;
        var sorted = _totals.OrderBy(x => x).ToArray();
        var summary = new TickTimingSummary(
            now, reason, sorted.Length,
            Math.Round(sorted[sorted.Length / 2], 2),
            Math.Round(sorted[^1], 2),
            Math.Round(_gapMax, 2),
            _agg.Select(kv => new TickStageSummary(kv.Key, Math.Round(kv.Value.Max, 2), Math.Round(kv.Value.Sum / kv.Value.N, 3), kv.Value.MaxAt.ToString("HH:mm:ss.fff")))
                .OrderByDescending(s => s.MaxMs).ToArray());
        try { _writer.Write(summary); } catch { }
        _totals.Clear();
        _agg.Clear();
        _gapMax = 0;
    }

    public void Dispose()
    {
        try { WriteSummary(DateTimeOffset.Now, "Stop"); } catch { }
        _writer.Dispose();
    }

    internal readonly struct StageScope : IDisposable
    {
        private readonly TickProfiler _owner;
        private readonly string _name;
        private readonly long _start;

        public StageScope(TickProfiler owner, string name)
        {
            _owner = owner;
            _name = name;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose() => _owner.Record(_name, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
}
