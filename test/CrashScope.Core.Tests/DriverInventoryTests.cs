using System.Text.Json;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class DriverInventoryTests
{
    [Fact]
    public void Entry_OldSessionJson_WithoutIsDiscrete_BindsNull()
    {
        // Sessions captured before the IsDiscrete field existed must still read.
        var json = """
            [{"adapterName":"AMD Radeon RX 7900 XTX","matchingDeviceId":"VEN_1002","driverVersion":"24.5.1","driverDate":null,"vendor":"AMD"}]
            """;

        var entries = JsonSerializer.Deserialize<DriverInventoryEntry[]>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var entry = Assert.Single(entries!);
        Assert.Null(entry.IsDiscrete);
        Assert.Equal("AMD Radeon RX 7900 XTX", entry.AdapterName);
    }

    [Fact]
    public void DetectDiscrete_ClassifiesByReportedMemorySize()
    {
        Assert.True(DriverInventory.DetectDiscrete(16L << 30));   // modern dGPU
        Assert.True(DriverInventory.DetectDiscrete(1L << 30));    // boundary: >= threshold
        Assert.False(DriverInventory.DetectDiscrete(512L << 20)); // AMD iGPU
        Assert.False(DriverInventory.DetectDiscrete(128L << 20)); // Intel iGPU
        Assert.Null(DriverInventory.DetectDiscrete(null));        // not reported (virtual/legacy)
        Assert.Null(DriverInventory.DetectDiscrete(-1));          // corrupt value
    }

    [Fact]
    public void Capture_OnLiveMachine_ReturnsDisplayAdapters()
    {
        // Display-class registry keys exist on every Windows machine with a GPU.
        var entries = DriverInventory.Capture();

        Assert.NotEmpty(entries);
    }
}
