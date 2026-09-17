using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

/// <summary>
/// Telemetry rows in the forensic timeline. Sensor values are positional, so the labels must
/// come from the session's sensor catalog; the previous value-shape heuristic reported the
/// first value in [0,100] as "CPU" (GPU load qualifies) and the last value above 100 as "RAM"
/// in MB (a temperature in Celsius qualifies).
/// </summary>
public sealed class TimelineTelemetryTests
{
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly SessionState Session = new(
        "session", Now.AddMinutes(-10), Now, false, "dir", true, "0.9.0");

    private static SensorCatalogEntry Entry(
        int id, string hardwareType, string hardwareName, string sensorType, string sensorName)
        => new(id, hardwareType, hardwareName, sensorType, sensorName, $"/{id}");

    private static IReadOnlyList<TimelineEntry> Build(
        IReadOnlyList<SensorCatalogEntry>? catalog,
        params TelemetrySnapshot[] telemetry)
        => TimelineBuilder.Build(Session, catalog, telemetry, [], [], [], [], [], GraphicsContext.Empty);

    [Fact]
    public void TelemetryEntry_LabelsCpuGpuAndRam_FromCatalogIndices()
    {
        var catalog = new[]
        {
            Entry(0, "Cpu", "AMD Ryzen 9", "Load", "CPU Total"),
            Entry(1, "GpuAmd", "AMD Radeon RX 9070 XT", "Load", "GPU Core"),
            Entry(2, "Memory", "Total Memory", "Data", "Memory Used"),
            Entry(3, "Memory", "Total Memory", "Data", "Memory Available"),
        };
        var snapshot = new TelemetrySnapshot(Now.AddSeconds(-5), new ForegroundState(10, "game", 3),
            new float?[] { 12.4f, 97.6f, 12.0f, 20.0f });

        var entry = Assert.Single(Build(catalog, snapshot), e => e.Category == "Telemetry");

        Assert.Equal("game | CPU:12% GPU:98% RAM:12.0/32.0GB", entry.Description);
    }

    [Fact]
    public void TelemetryEntry_DoesNotLabelGpuLoadAsCpu_WhenGpuIsFirstInCatalog()
    {
        var catalog = new[]
        {
            Entry(0, "GpuAmd", "AMD Radeon RX 9070 XT", "Load", "GPU Core"),
            Entry(1, "Cpu", "AMD Ryzen 9", "Load", "CPU Total"),
        };
        var snapshot = new TelemetrySnapshot(Now.AddSeconds(-5), new ForegroundState(null, null, 600),
            new float?[] { 88f, 5f });

        var entry = Assert.Single(Build(catalog, snapshot), e => e.Category == "Telemetry");

        Assert.Contains("CPU:5%", entry.Description);
        Assert.Contains("GPU:88%", entry.Description);
        Assert.DoesNotContain("CPU:88%", entry.Description);
    }

    [Fact]
    public void TelemetryEntry_DoesNotLabelATemperatureAsRam()
    {
        var catalog = new[]
        {
            Entry(0, "Cpu", "AMD Ryzen 9", "Load", "CPU Total"),
            Entry(1, "GpuAmd", "AMD Radeon RX 9070 XT", "Temperature", "GPU Hot Spot"),
        };
        var snapshot = new TelemetrySnapshot(Now.AddSeconds(-5), new ForegroundState(10, "game", 3),
            new float?[] { 7f, 91f });

        var entry = Assert.Single(Build(catalog, snapshot), e => e.Category == "Telemetry");

        Assert.DoesNotContain("RAM", entry.Description);
        Assert.Equal("game | CPU:7%", entry.Description);
    }

    [Fact]
    public void TelemetryEntry_WithoutCatalog_EmitsNothingRatherThanGuessing()
    {
        var snapshot = new TelemetrySnapshot(Now.AddSeconds(-5), new ForegroundState(10, "game", 3),
            new float?[] { 12.4f, 97.6f, 12000f });

        Assert.DoesNotContain(Build(null, snapshot), e => e.Category == "Telemetry");
        Assert.DoesNotContain(Build([], snapshot), e => e.Category == "Telemetry");
    }

    [Fact]
    public void TelemetryEntry_WithOnlyRamUsed_ReportsUsedAlone()
    {
        var catalog = new[] { Entry(0, "Memory", "Total Memory", "Data", "Memory Used") };
        var snapshot = new TelemetrySnapshot(Now.AddSeconds(-5), new ForegroundState(10, "game", 3),
            new float?[] { 11.5f });

        var entry = Assert.Single(Build(catalog, snapshot), e => e.Category == "Telemetry");

        Assert.Equal("game | RAM:11.5GB", entry.Description);
    }

    [Fact]
    public void TelemetryEntry_IgnoresAnOutOfRangeCatalogIndex()
    {
        var catalog = new[] { Entry(99, "Cpu", "AMD Ryzen 9", "Load", "CPU Total") };
        var snapshot = new TelemetrySnapshot(Now.AddSeconds(-5), new ForegroundState(10, "game", 3),
            new float?[] { 12.4f });

        Assert.DoesNotContain(Build(catalog, snapshot), e => e.Category == "Telemetry");
    }
}
