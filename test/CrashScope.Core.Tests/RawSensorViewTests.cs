using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public class RawSensorViewTests
{
    private static SensorCatalogEntry E(int id, string hw, string hwName, string st, string sName)
        => new(id, hw, hwName, st, sName, $"/{hwName}/{sName}");

    private static readonly SensorCatalogEntry[] Catalog =
    [
        E(0, "Cpu", "AMD Ryzen 7 9700X", "Load", "CPU Total"),
        E(1, "Cpu", "AMD Ryzen 7 9700X", "Temperature", "Core (Tctl/Tdie)"),
        E(2, "GpuAti", "AMD Radeon RX 9070 XT", "Power", "GPU Power"),
        E(3, "GpuAti", "AMD Radeon RX 9070 XT", "Temperature", "GPU Core"),
        E(4, "Memory", "Generic Memory", "Data", "Used")
    ];

    [Fact]
    public void EmptyCatalog_ProducesNoRows()
        => Assert.Empty(RawSensorView.Build([], null, null));

    [Fact]
    public void Values_AreIndexedById_NotByFilteredPosition()
    {
        var values = new float?[] { 10f, 20f, 30f, 44.5f, 50f };
        var row = Assert.Single(RawSensorView.Build(Catalog, values, "GPU Core"));
        Assert.Equal(3, row.Id);
        Assert.True(row.HasValue);
        Assert.Equal("44.5 °C", row.ValueText);
    }

    [Fact]
    public void Search_MatchesHardwareSensorAndType_CaseInsensitive()
    {
        Assert.Equal(2, RawSensorView.Build(Catalog, null, "ryzen").Count);
        Assert.Single(RawSensorView.Build(Catalog, null, "tctl"));
        Assert.Equal(2, RawSensorView.Build(Catalog, null, "gpuati").Count);
    }

    [Fact]
    public void HardwareTypeFilter_IsExactButCaseInsensitive()
    {
        Assert.Equal(2, RawSensorView.Build(Catalog, null, null, "GPUATI").Count);
        Assert.Empty(RawSensorView.Build(Catalog, null, null, "Gpu"));
        Assert.Equal(5, RawSensorView.Build(Catalog, null, null, "All").Count);
    }

    [Fact]
    public void Search_And_TypeFilter_Combine()
    {
        var row = Assert.Single(RawSensorView.Build(Catalog, null, "core", "Cpu"));
        Assert.Equal(1, row.Id);
    }

    [Fact]
    public void MissingShortOrNullAndNonFiniteValues_RenderAsDash()
    {
        var rows = RawSensorView.Build(Catalog, new float?[] { null, float.NaN }, null);
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("--", r.ValueText);
            Assert.False(r.HasValue);
        });
    }

    [Fact]
    public void Units_FollowSensorType()
    {
        Assert.Equal("45.0 °C", RawSensorView.FormatValue("Temperature", 45f));
        Assert.Equal("12.3 %", RawSensorView.FormatValue("Load", 12.34f));
        Assert.Equal("30.0 %", RawSensorView.FormatValue("Control", 30f));
        Assert.Equal("49.9 W", RawSensorView.FormatValue("Power", 49.9f));
        Assert.Equal("3200.0 MHz", RawSensorView.FormatValue("Clock", 3200f));
        Assert.Equal("1.2 V", RawSensorView.FormatValue("Voltage", 1.2f));
        Assert.Equal("1200.0 RPM", RawSensorView.FormatValue("Fan", 1200f));
        Assert.Equal("5.4 GB", RawSensorView.FormatValue("Data", 5.4f));
        Assert.Equal("128.0 MB", RawSensorView.FormatValue("SmallData", 128f));
        Assert.Equal("7.0", RawSensorView.FormatValue("SomethingElse", 7f));
        Assert.Equal("--", RawSensorView.FormatValue("Temperature", null));
        Assert.Equal("--", RawSensorView.FormatValue("Temperature", float.PositiveInfinity));
    }

    [Fact]
    public void ListHardwareTypes_IsDistinctAndSorted()
        => Assert.Equal(new[] { "Cpu", "GpuAti", "Memory" }, RawSensorView.ListHardwareTypes(Catalog));
}
