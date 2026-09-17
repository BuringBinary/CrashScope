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

    // ───── v0.9 GPU VRAM ─────

    [Fact]
    public void GpuVramEvents_AppearAsGpuVramCategory()
    {
        var gpuMemoryEvents = new GpuMemorySample[]
        {
            new(Now.AddSeconds(-35),
            [
                new ProcessGpuMemory(1234, "game.exe", 8L * 1024 * 1024 * 1024, 512L * 1024 * 1024),
                new ProcessGpuMemory(5678, "chrome.exe", 1L * 1024 * 1024 * 1024, 256L * 1024 * 1024)
            ])
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null, gpuMemoryEvents, null, null);

        var vramEntries = timeline.Where(e => e.Category == "GPU-VRAM").ToList();
        Assert.NotEmpty(vramEntries);
        Assert.Contains(vramEntries, e => e.Description.Contains("game.exe"));
        Assert.Contains(vramEntries, e => e.Description.Contains("dedicated"));
    }

    [Fact]
    public void GpuVramEvents_ShowTopProcesses()
    {
        var gpuMemoryEvents = new GpuMemorySample[]
        {
            new(Now.AddSeconds(-35),
            [
                new ProcessGpuMemory(1111, "small.exe", 100L * 1024 * 1024, 50L * 1024 * 1024),
                new ProcessGpuMemory(2222, "big.exe", 10L * 1024 * 1024 * 1024, 200L * 1024 * 1024),
                new ProcessGpuMemory(3333, "med.exe", 2L * 1024 * 1024 * 1024, 100L * 1024 * 1024),
                new ProcessGpuMemory(4444, "tiny.exe", 10L * 1024 * 1024, 5L * 1024 * 1024)
            ])
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null, gpuMemoryEvents, null, null);

        var vramEntries = timeline.Where(e => e.Category == "GPU-VRAM").ToList();
        Assert.Equal(3, vramEntries.Count);
        Assert.Contains(vramEntries, e => e.Description.Contains("big.exe"));
        Assert.Contains(vramEntries, e => e.Description.Contains("med.exe"));
        Assert.Contains(vramEntries, e => e.Description.Contains("small.exe"));
    }

    // ───── v0.9 Memory Pressure ─────

    [Fact]
    public void MemoryPressureEvents_AppearAsMemoryCategory()
    {
        var memoryPressureEvents = new MemoryPressureSample[]
        {
            new(Now.AddSeconds(-40), 30L * 1024 * 1024 * 1024, 32L * 1024 * 1024 * 1024, 512L * 1024 * 1024, 95f)
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null, null, memoryPressureEvents, null);

        var memEntries = timeline.Where(e => e.Category == "Memory").ToList();
        Assert.NotEmpty(memEntries);
        Assert.Contains(memEntries, e => e.Description.Contains("95%"));
    }

    [Fact]
    public void MemoryPressureNormal_NotShown()
    {
        var memoryPressureEvents = new MemoryPressureSample[]
        {
            new(Now.AddSeconds(-40), 20L * 1024 * 1024 * 1024, 32L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024, 62f)
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null, null, memoryPressureEvents, null);

        var memEntries = timeline.Where(e => e.Category == "Memory").ToList();
        Assert.Empty(memEntries);
    }

    // ───── v0.9 DWM State ─────

    [Fact]
    public void DwmStateEvents_AppearAsDwmCategory()
    {
        var dwmStateEvents = new DwmStateEvent[]
        {
            new(Now.AddSeconds(-25), false, false, "GPU hardware acceleration lost")
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null, null, null, dwmStateEvents);

        var dwmEntries = timeline.Where(e => e.Category == "DWM").ToList();
        Assert.NotEmpty(dwmEntries);
        Assert.Contains(dwmEntries, e => e.Description.Contains("software rendering"));
    }

    [Fact]
    public void AllV09Events_AreOrderedByTimestamp()
    {
        var gpuMemoryEvents = new GpuMemorySample[]
        {
            new(Now.AddSeconds(-50),
            [
                new ProcessGpuMemory(1234, "game.exe", 8L * 1024 * 1024 * 1024, 512L * 1024 * 1024)
            ])
        };
        var memoryPressureEvents = new MemoryPressureSample[]
        {
            new(Now.AddSeconds(-40), 30L * 1024 * 1024 * 1024, 32L * 1024 * 1024 * 1024, 512L * 1024 * 1024, 95f)
        };
        var dwmStateEvents = new DwmStateEvent[]
        {
            new(Now.AddSeconds(-30), false, false, "DWM software fallback")
        };

        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null, gpuMemoryEvents, memoryPressureEvents, dwmStateEvents);

        var v09Categories = new[] { "GPU-VRAM", "Memory", "DWM" };
        var v09Entries = timeline.Where(e => v09Categories.Contains(e.Category)).ToList();
        for (var i = 1; i < v09Entries.Count; i++)
            Assert.True(v09Entries[i].Timestamp >= v09Entries[i - 1].Timestamp);
    }

    [Fact]
    public void V09NullInputs_DoNotCrash()
    {
        var timeline = TimelineBuilder.Build(
            DefaultSession, null, [], [], [], [], [], [], GraphicsContext.Empty,
            null, null, null, null, null, null);

        Assert.NotEmpty(timeline);
    }
}