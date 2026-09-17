using System.Diagnostics;
using System.Reflection;
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
        var tracker = new ProcessTracker();
        tracker.Poll(DateTimeOffset.Now); // establish baseline

        var ourPid = Process.GetCurrentProcess().Id;
        var previousField = typeof(ProcessTracker).GetField("_previous", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(previousField);
        var previous = (Dictionary<int, string>)previousField!.GetValue(tracker)!;
        previous[ourPid] = "DefinitelyNotOurName";

        var events = tracker.Poll(DateTimeOffset.Now);

        Assert.Contains(events, e => e.Event == "stop" && e.ProcessId == ourPid && e.ProcessName == "DefinitelyNotOurName");
        Assert.Contains(events, e => e.Event == "start" && e.ProcessId == ourPid && e.ProcessName != "DefinitelyNotOurName");
    }
}

