using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class TimelineBuilderEtwTests
{
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SessionState DefaultSession => new(
        "testsession123",
        Now.AddHours(-1),
        Now,
        false,
        @"C:\test\session",
        true,
        "0.6.0");

    [Fact]
    public void EtwGpuEvents_AppearAsEtwGpuCategory()
    {
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-30), 1, "TdrTimedOut", 1234, "game.exe", "Reason=Timeout"),
            new(Now.AddSeconds(-20), 3, "TdrRecoverFromTimeout", 0, "System", "Status=0x0")
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            etwEvents, null, null);

        var gpuEntries = timeline.Where(e => e.Category == "ETW-GPU").ToList();
        Assert.Equal(2, gpuEntries.Count);
        Assert.Contains(gpuEntries, e => e.Description.Contains("TdrTimedOut"));
        Assert.Contains(gpuEntries, e => e.Description.Contains("TdrRecoverFromTimeout"));
    }

    [Fact]
    public void EtwKernelPowerEvents_AppearAsEtwPowerCategory()
    {
        var etwKernelPower = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-Kernel-Power", Now.AddSeconds(-45),
                10, "SuspendStart", "Info", 4, "System", null),
            new("Microsoft-Windows-Kernel-Power", Now.AddSeconds(-40),
                11, "SuspendEnd", "Info", 4, "System", null)
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, etwKernelPower, null);

        var powerEntries = timeline.Where(e => e.Category == "ETW-Power").ToList();
        Assert.Equal(2, powerEntries.Count);
        Assert.Contains(powerEntries, e => e.Description.Contains("SuspendStart"));
        Assert.Contains(powerEntries, e => e.Description.Contains("SuspendEnd"));
    }

    [Fact]
    public void EtwWheaEvents_AppearAsEtwWheaCategory()
    {
        var etwWhea = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-WHEA-Logger", Now.AddSeconds(-10),
                1, "GenericErrorEvent", "Error", null, null, "Severity=Fatal")
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, etwWhea);

        var wheaEntries = timeline.Where(e => e.Category == "ETW-WHEA").ToList();
        Assert.Single(wheaEntries);
        Assert.Contains("GenericErrorEvent", wheaEntries[0].Description);
        Assert.Contains("Fatal", wheaEntries[0].Description);
    }

    [Fact]
    public void EtwEvents_AreOrderedByTimestamp()
    {
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-50), 800, "TdrEngineReset", null, null, null),
            new(Now.AddSeconds(-30), 1, "TdrTimedOut", null, null, null),
        };
        var etwKernelPower = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-Kernel-Power", Now.AddSeconds(-40),
                10, "SuspendStart", "Info", null, null, null),
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            etwEvents, etwKernelPower, null);

        var etwEntries = timeline.Where(e =>
            e.Category.StartsWith("ETW-", StringComparison.OrdinalIgnoreCase)).ToList();

        for (var i = 1; i < etwEntries.Count; i++)
            Assert.True(etwEntries[i].Timestamp >= etwEntries[i - 1].Timestamp);
    }

    [Fact]
    public void EtwEvents_RespectNullInputs()
    {
        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null);

        var etwEntries = timeline.Where(e => e.Category.StartsWith("ETW-", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(etwEntries);

        Assert.NotEmpty(timeline);
    }

    [Fact]
    public void EtwProviderEvent_LevelAppearsInDescription()
    {
        var etwKernelPower = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-Kernel-Power", Now.AddSeconds(-15),
                25, "ThermalZoneOverheated", "Error", null, null, "Temp=110C")
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, etwKernelPower, null);

        var powerEntries = timeline.Where(e => e.Category == "ETW-Power").ToList();
        Assert.Single(powerEntries);
        Assert.Contains("[Error]", powerEntries[0].Description);
        Assert.Contains("ThermalZoneOverheated", powerEntries[0].Description);
    }

    [Fact]
    public void EtwDxgKrnlEvent_ProcessNameAppearsInDescription()
    {
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-20), 500, "SubmitCommandBuffer", 5678, "renderer.exe", "Engine=3D")
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            etwEvents, null, null);

        var gpuEntry = timeline.First(e => e.Category == "ETW-GPU");
        Assert.Contains("renderer.exe", gpuEntry.Description);
        Assert.Contains("SubmitCommandBuffer", gpuEntry.Description);
    }
}