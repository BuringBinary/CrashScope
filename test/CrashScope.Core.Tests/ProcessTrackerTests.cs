using System.Diagnostics;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class ProcessTrackerTests
{
    [Fact]
    public void FirstPoll_InitializesBaseline_ReturnsNoEvents()
    {
        var tracker = new ProcessTracker();
        Assert.Empty(tracker.Poll(DateTimeOffset.Now));
    }

    [Fact]
    public void Snapshot_ContainsCurrentProcesses()
    {
        var tracker = new ProcessTracker();
        tracker.Poll(DateTimeOffset.Now);
        var snapshot = tracker.CaptureSnapshot(DateTimeOffset.Now);

        Assert.NotEmpty(snapshot.Processes);
        Assert.All(snapshot.Processes, p => Assert.False(string.IsNullOrWhiteSpace(p.ProcessName)));
    }

    [Fact]
    public void SecondPoll_DoesNotReportOwnProcessStopped()
    {
        var tracker = new ProcessTracker();
        tracker.Poll(DateTimeOffset.Now);
        var events = tracker.Poll(DateTimeOffset.Now);

        var ourPid = Process.GetCurrentProcess().Id;
        Assert.DoesNotContain(events, e => e.Event == "stop" && e.ProcessId == ourPid);
    }

    [Fact]
    public void Poll_RenamedPid_EmitsStopThenStart()
    {
        IReadOnlyList<ProcessInfo> current = [new(42, "old", DateTimeOffset.Parse("2026-09-19T00:00:00Z"), 100, 80, 5)];
        var tracker = new ProcessTracker(() => current);
        tracker.Poll(DateTimeOffset.Now);
        current = [new(42, "new", DateTimeOffset.Parse("2026-09-19T00:00:00Z"), 200, 90, 6)];

        var events = tracker.Poll(DateTimeOffset.Now);

        Assert.Contains(events, e => e.Event == "stop" && e.ProcessId == 42 && e.ProcessName == "old" && e.PrivateBytes == 100);
        Assert.Contains(events, e => e.Event == "start" && e.ProcessId == 42 && e.ProcessName == "new" && e.PrivateBytes == 200);
    }

    [Fact]
    public void Poll_SameNamePidReuse_UsesCreationTimeAndPreservesLastMetrics()
    {
        var first = DateTimeOffset.Parse("2026-09-19T00:00:00Z");
        IReadOnlyList<ProcessInfo> current = [new(42, "game", first, 8L << 30, 2L << 30, 100)];
        var tracker = new ProcessTracker(() => current);
        tracker.Poll(DateTimeOffset.Now);
        current = [new(42, "game", first.AddMinutes(1), 1L << 30, 512L << 20, 30)];

        var events = tracker.Poll(DateTimeOffset.Now);

        var stopped = Assert.Single(events, e => e.Event == "stop");
        Assert.Equal("pid-reused", stopped.State);
        Assert.Equal(8L << 30, stopped.PrivateBytes);
        Assert.Single(events, e => e.Event == "start");
    }

    [Fact]
    public void Poll_DisappearedProcess_EmitsLastKnownMemoryAndHandleState()
    {
        IReadOnlyList<ProcessInfo> current = [new(7, "game", DateTimeOffset.Now, 1234, 567, 9)];
        var tracker = new ProcessTracker(() => current);
        tracker.Poll(DateTimeOffset.Now);
        current = [];

        var stopped = Assert.Single(tracker.Poll(DateTimeOffset.Now));
        Assert.Equal("exited-or-inaccessible", stopped.State);
        Assert.Equal(1234, stopped.PrivateBytes);
        Assert.Equal(567, stopped.WorkingSetBytes);
        Assert.Equal(9, stopped.HandleCount);
    }
}

