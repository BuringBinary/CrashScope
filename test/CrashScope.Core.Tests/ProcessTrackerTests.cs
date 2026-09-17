using System.Diagnostics;
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
}

