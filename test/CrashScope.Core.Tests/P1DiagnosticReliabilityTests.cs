using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class P1DiagnosticReliabilityTests
{
    [Fact]
    public void SelfOverheadTracker_ComputesNormalizedCpuAndEtwRates()
    {
        var now = DateTimeOffset.Parse("2026-09-19T12:00:00+08:00");
        var pass = 0;
        var tracker = new SelfOverheadTracker(4, () =>
        {
            var cpu = pass++ == 0 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(11);
            return [new SelfOverheadTracker.SelfProcessReading(42, "CrashScope.Desktop", now.AddMinutes(-1),
                cpu, 1000, 2000, 10, 3)];
        });
        var provider = Guid.NewGuid();

        var first = tracker.Capture(now,
            [Coverage(provider, received: 100, matched: 25, written: 20, dropped: 2, lost: 3)]);
        var second = tracker.Capture(now.AddSeconds(5),
            [Coverage(provider, received: 150, matched: 35, written: 30, dropped: 4, lost: 7)]);

        Assert.Null(first.TotalCpuPercent);
        Assert.Equal(5f, second.TotalCpuPercent);
        Assert.Equal(1000, second.TotalPrivateBytes);
        var etw = Assert.Single(second.Etw);
        Assert.Equal(10d, etw.ReceivedPerSecond);
        Assert.Equal(2d, etw.MatchedPerSecond);
        Assert.Equal(2d, etw.WrittenPerSecond);
        Assert.Equal(4, etw.HandoffDroppedEvents);
        Assert.Equal(7, etw.OsLostEvents);
    }

    [Fact]
    public void SelfOverheadTracker_DoesNotReportRatesAcrossCounterReset()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new SelfOverheadTracker(1, () => []);
        var provider = Guid.NewGuid();
        tracker.Capture(now, [Coverage(provider, 100, 50, 25, 0, 0)]);

        var afterReset = tracker.Capture(now.AddSeconds(5), [Coverage(provider, 1, 1, 1, 0, 0)]);

        Assert.Null(Assert.Single(afterReset.Etw).ReceivedPerSecond);
    }

    [Fact]
    public void EtwCoverage_SeparatesOsLossFromHandoffDrops()
    {
        var tracker = new EtwCoverageTracker("provider", Guid.NewGuid());
        tracker.Dropped();
        tracker.Dropped();
        tracker.OsLost(9);

        var snapshot = tracker.Snapshot();
        Assert.Equal(2, snapshot.BufferDroppedEvents);
        Assert.Equal(9, snapshot.OsLostEvents);
        Assert.Contains("handoffDropped=2, osLost=9", EtwCoverageTracker.Detail(snapshot));
    }

    [Fact]
    public void DwmTracker_EmitsStartupBaselineAndOnlyRealTransitions()
    {
        var results = new Queue<(int, bool)>([(0, true), (0, true), (0, false)]);
        var tracker = new DwmStateTracker(() => results.Dequeue());
        var now = DateTimeOffset.UtcNow;

        var baseline = tracker.Poll(now);
        var unchanged = tracker.Poll(now.AddSeconds(1));
        var transition = tracker.Poll(now.AddSeconds(2));

        Assert.NotNull(baseline);
        Assert.Contains("baseline", baseline.Description);
        Assert.Null(unchanged);
        Assert.NotNull(transition);
        Assert.False(transition.IsCompositionEnabled);
        Assert.Equal("Collecting", tracker.HealthStatus);
    }

    [Fact]
    public void DwmTracker_ReportsProbeFailureAsUnavailable()
    {
        var tracker = new DwmStateTracker(() => (unchecked((int)0x80070005), false));

        Assert.Null(tracker.Poll(DateTimeOffset.UtcNow));
        Assert.False(tracker.IsAvailable);
        Assert.Equal("Unavailable", tracker.HealthStatus);
        Assert.Contains("80070005", tracker.HealthDetail);
    }

    [Fact]
    public void PageFileTracker_RecordsConfigurationAndCommitLimitChanges()
    {
        var file = new PageFileInfo("C:\\pagefile.sys", "C:\\", 8L << 30, 2L << 30, 3L << 30,
            4L << 30, 12L << 30, 100L << 30, false);
        var tracker = new PageFileTracker(() => new PageFileTracker.PageFileConfiguration(true, [file], null));
        var now = DateTimeOffset.UtcNow;

        var baseline = tracker.Capture(now, null, "Baseline");
        var firstLimit = tracker.ObserveCommitLimit(now.AddSeconds(1), 32L << 30);
        var change = tracker.ObserveCommitLimit(now.AddSeconds(2), 40L << 30);

        Assert.True(baseline.AutomaticManagedPagefile);
        Assert.Same(file, Assert.Single(baseline.PageFiles));
        Assert.Null(firstLimit);
        Assert.NotNull(change);
        Assert.Equal("CommitLimitChanged", change.Reason);
        Assert.Equal(32L << 30, change.PreviousCommitLimitBytes);
        Assert.Equal(40L << 30, change.CommitLimitBytes);
    }

    [Fact]
    public void GpuEngineHeartbeat_CarriesIdleAndSuppressionStatusWithoutActiveProcesses()
    {
        var idle = new GpuEngineSample(DateTimeOffset.UtcNow, [], "Idle", 14);
        var suppressed = new GpuEngineSample(DateTimeOffset.UtcNow, [], "SuppressedByPressure", 0, "critical pressure");

        Assert.Empty(idle.Processes);
        Assert.Equal(14, idle.InstanceCount);
        Assert.Equal("SuppressedByPressure", suppressed.Status);
        Assert.Equal("critical pressure", suppressed.Error);
    }

    [Fact]
    public void CpuFallback_ClampsPdhValueAndIdentifiesSource()
    {
        using var tracker = new CpuFallbackTracker(() => 120f);

        var sample = tracker.Capture(DateTimeOffset.UtcNow);

        Assert.Equal(100f, sample.TotalUtilizationPercent);
        Assert.Equal("WindowsPdhProcessorTotal", sample.Source);
        Assert.Equal("Collecting", sample.Status);
    }

    [Fact]
    public void CpuFallback_ReportsReadFailureWithoutThrowing()
    {
        using var tracker = new CpuFallbackTracker(() => throw new InvalidOperationException("counter unavailable"));

        var sample = tracker.Capture(DateTimeOffset.UtcNow);

        Assert.Null(sample.TotalUtilizationPercent);
        Assert.Equal("Degraded", sample.Status);
        Assert.Contains("counter unavailable", sample.Error);
    }

    private static EtwCoverageSnapshot Coverage(Guid provider, long received, long matched, long written, long dropped, long lost)
        => new("provider", provider, true, true, false, received, matched, written, dropped,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, lost);
}
