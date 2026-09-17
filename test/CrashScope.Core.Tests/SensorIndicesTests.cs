using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

/// <summary>
/// Sensor catalog index resolution. These indices drive FinalGpuLoadPercent, the GPU-load
/// collapse evidence and the forensic timeline labels, so resolving them against the wrong
/// adapter or the wrong sensor silently produces misleading incident reports.
/// </summary>
public sealed class SensorIndicesTests
{
    private static SensorCatalogEntry Entry(
        int id, string hardwareType, string hardwareName, string sensorType, string sensorName)
        => new(id, hardwareType, hardwareName, sensorType, sensorName, $"/{hardwareName.ToLowerInvariant()}/{sensorType.ToLowerInvariant()}/{id}");

    /// <summary>
    /// Mirrors the real catalog on the target machine: LibreHardwareMonitor reports both the
    /// APU iGPU and the discrete RX 9070 XT as "GpuAmd" and enumerates the iGPU first.
    /// </summary>
    private static readonly SensorCatalogEntry[] HybridCatalog =
    [
        Entry(0, "Cpu", "AMD Ryzen 9 9950X", "Load", "CPU Total"),
        Entry(1, "GpuAmd", "AMD Radeon(TM) Graphics", "Load", "GPU Core"),
        Entry(2, "GpuAmd", "AMD Radeon(TM) Graphics", "Clock", "GPU Core"),
        Entry(3, "GpuAmd", "AMD Radeon(TM) Graphics", "Power", "GPU SoC"),
        Entry(4, "GpuAmd", "AMD Radeon RX 9070 XT", "Load", "GPU Core"),
        Entry(5, "GpuAmd", "AMD Radeon RX 9070 XT", "Clock", "GPU Core"),
        Entry(6, "GpuAmd", "AMD Radeon RX 9070 XT", "Fan", "GPU Fan"),
        Entry(7, "GpuAmd", "AMD Radeon RX 9070 XT", "Control", "GPU Fan"),
        Entry(8, "Memory", "Total Memory", "Data", "Memory Used"),
        Entry(9, "Memory", "Total Memory", "Data", "Memory Available"),
        Entry(10, "Memory", "Virtual Memory", "Data", "Memory Used"),
    ];

    [Fact]
    public void PrimaryGpu_PrefersDiscreteAdapter_WhenLhmDoesNotLabelTheType()
    {
        Assert.Equal("AMD Radeon RX 9070 XT", SensorIndices.PrimaryGpuHardwareName(HybridCatalog));
    }

    [Fact]
    public void GpuCoreLoad_ResolvesAgainstTheDiscreteAdapter()
    {
        var index = SensorIndices.GpuCoreLoad(HybridCatalog);

        Assert.Equal(4, index);
        Assert.Equal("AMD Radeon RX 9070 XT", HybridCatalog[index!.Value].HardwareName);
    }

    [Fact]
    public void GpuCoreClock_ResolvesAgainstTheDiscreteAdapter()
        => Assert.Equal(5, SensorIndices.GpuCoreClock(HybridCatalog));

    [Fact]
    public void PrimaryGpu_HonoursAnExplicitDiscreteHardwareType()
    {
        var catalog = new[]
        {
            Entry(0, "GpuIntegrated", "Intel UHD Graphics", "Load", "GPU Core"),
            Entry(1, "GpuIntegrated", "Intel UHD Graphics", "Fan", "GPU Fan"),
            Entry(2, "GpuDiscrete", "NVIDIA GeForce RTX 4090", "Load", "GPU Core"),
        };

        Assert.Equal("NVIDIA GeForce RTX 4090", SensorIndices.PrimaryGpuHardwareName(catalog));
    }

    [Fact]
    public void PrimaryGpu_FallsBackToFirstAdapter_WhenNothingDistinguishesThem()
    {
        var catalog = new[]
        {
            Entry(0, "GpuNvidia", "Card A", "Load", "GPU Core"),
            Entry(1, "GpuNvidia", "Card B", "Load", "GPU Core"),
        };

        Assert.Equal("Card A", SensorIndices.PrimaryGpuHardwareName(catalog));
    }

    [Fact]
    public void PrimaryGpu_IntegratedOnlyMachine_StillResolves()
    {
        var catalog = new[]
        {
            Entry(0, "GpuIntegrated", "Intel UHD Graphics", "Load", "GPU Core"),
        };

        Assert.Equal("Intel UHD Graphics", SensorIndices.PrimaryGpuHardwareName(catalog));
        Assert.Equal(0, SensorIndices.GpuCoreLoad(catalog));
    }

    [Fact]
    public void MemoryIndices_UsePhysicalRam_NotCommitCharge()
    {
        Assert.Equal(8, SensorIndices.MemoryUsed(HybridCatalog));
        Assert.Equal(9, SensorIndices.MemoryAvailable(HybridCatalog));
    }

    [Fact]
    public void CpuTotalLoad_ResolvesTotalLoadSensor()
        => Assert.Equal(0, SensorIndices.CpuTotalLoad(HybridCatalog));

    [Fact]
    public void NullOrEmptyCatalog_ResolvesToNull()
    {
        Assert.Null(SensorIndices.PrimaryGpuHardwareName(null));
        Assert.Null(SensorIndices.GpuCoreLoad(null));
        Assert.Null(SensorIndices.MemoryUsed([]));
        Assert.False(SensorIndices.HasGpuSensors(null));
        Assert.True(SensorIndices.HasGpuSensors(HybridCatalog));
    }
}
