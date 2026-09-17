using CrashScope.Core.Monitoring;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class DisplayTopologyTrackerTests
{
    private static DisplayInfo Mon(string device = @"\\.\DISPLAY1", string monitor = "Dell U2723QE", bool virtualDisplay = false, int w = 2560, int h = 1440)
        => new(device, monitor, virtualDisplay, w, h);

    [Fact]
    public void SameTopology_IdenticalTopologies_True()
    {
        Assert.True(DisplayTopologyTracker.SameTopology([Mon()], [Mon()]));
    }

    [Fact]
    public void SameTopology_DifferentResolution_False()
    {
        Assert.False(DisplayTopologyTracker.SameTopology([Mon(w: 1920, h: 1080)], [Mon()]));
    }

    [Fact]
    public void SameTopology_MonitorAdded_False()
    {
        Assert.False(DisplayTopologyTracker.SameTopology([Mon()], [Mon(), Mon(@"\\.\DISPLAY2")]));
    }

    [Theory]
    [InlineData("ToDesk Virtual Display", true)]
    [InlineData("Parsec Virtual Display", true)]
    [InlineData("GameViewer Virtual Display Adapter", true)]
    [InlineData("SAMSUNG Display", false)]
    public void IsVirtualDisplay_KnownMarkers(string monitorName, bool expected)
    {
        Assert.Equal(expected, DisplayTopologyTracker.IsVirtualDisplay(monitorName));
    }

    [Fact]
    public void Describe_Baseline_ContainsMonitorCount()
    {
        var text = DisplayTopologyTracker.Describe(null, [Mon()]);
        Assert.Contains("baseline", text);
        Assert.Contains("Dell U2723QE", text);
    }

    [Fact]
    public void Describe_Changed_ReportsRemovedMonitor()
    {
        var text = DisplayTopologyTracker.Describe([Mon(@"\\.\DISPLAY1"), Mon(@"\\.\DISPLAY2", "Dell U24")], [Mon(@"\\.\DISPLAY1")]);
        Assert.Contains("removed", text);
        Assert.Contains("Dell U24", text);
    }
}

