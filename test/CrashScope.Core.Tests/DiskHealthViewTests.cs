using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public class DiskHealthViewTests
{
    private static DiskMetrics D(string name, string? model = null, float? temp = null, string? health = null,
        float? diskTime = null, float? read = null, float? write = null, float? queue = null,
        int? index = null, string? perf = null, string? sensors = null)
        => new(name, model, temp, health, diskTime, read, write, queue, index, perf, sensors);

    [Fact]
    public void EmptyList_ProducesNoRows()
        => Assert.Empty(DiskHealthView.Build([]));

    [Fact]
    public void AllFields_FormatWithUnits()
    {
        var row = DiskHealthView.Build([D("0 C:", "INTEL SSDPEKNW512G8", 42.5f, "Healthy",
            3.24f, 0.29f, 0.51f, 2f, 0, "PhysicalDisk", "LibreHardwareMonitor")]).Single();
        Assert.Equal("42.5 °C", row.TemperatureText);
        Assert.Equal("Healthy", row.SmartHealthText);
        Assert.Equal("3.2 %", row.DiskTimeText);
        Assert.Equal("0.29 ms", row.ReadLatencyText);
        Assert.Equal("0.51 ms", row.WriteLatencyText);
        Assert.Equal("2", row.QueueDepthText);
        Assert.Equal("Disk 0", row.IndexText);
        Assert.Equal("perf: PhysicalDisk · sensors: LibreHardwareMonitor", row.SourcesText);
        Assert.True(row.HasAnyMetric);
    }

    [Fact]
    public void MissingAndNonFinite_RenderAsDash()
    {
        var row = DiskHealthView.Build([D("0", null, float.NaN, "  ", null, float.PositiveInfinity)]).Single();
        Assert.Equal("Unknown model", row.Model);
        Assert.Equal("--", row.TemperatureText);
        Assert.Equal("--", row.SmartHealthText);
        Assert.Equal("--", row.ReadLatencyText);
        Assert.Equal("--", row.DiskTimeText);
        Assert.Equal("--", row.SourcesText);
        Assert.Equal("Index unknown", row.IndexText);
        Assert.False(row.HasAnyMetric);
    }

    [Fact]
    public void ValidZero_StaysVisible()
    {
        var row = DiskHealthView.Build([D("0", queue: 0f, diskTime: 0.4f)]).Single();
        Assert.Equal("0", row.QueueDepthText);
        Assert.Equal("0.4 %", row.DiskTimeText);
        Assert.True(row.HasAnyMetric);
    }

    [Fact]
    public void Rows_SortByIndex_UnknownLast_ThenByName()
    {
        var rows = DiskHealthView.Build(
        [
            D("Zeta", index: 2),
            D("Alpha", index: 0),
            D("Beta", index: null),
            D("Mid", index: 0)
        ]);
        Assert.Equal(["Alpha", "Mid", "Zeta", "Beta"], rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void SourcesText_JoinsOnlyPresentParts()
    {
        var row = DiskHealthView.Build([D("0", perf: "PhysicalDisk")]).Single();
        Assert.Equal("perf: PhysicalDisk", row.SourcesText);
        var row2 = DiskHealthView.Build([D("0", sensors: "LHM")]).Single();
        Assert.Equal("sensors: LHM", row2.SourcesText);
    }

    [Fact]
    public void SummarizeCollector_HasThreeShapes()
    {
        Assert.Equal("Storage collector: unknown", DiskHealthView.SummarizeCollector(null, null));
        Assert.Equal("Storage collector: NoData", DiskHealthView.SummarizeCollector("NoData", " "));
        Assert.Equal("Storage collector: Degraded — some counter failed", DiskHealthView.SummarizeCollector("Degraded", "some counter failed"));
    }
}
