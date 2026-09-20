using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class P0CollectionSafetyTests
{
    [Fact]
    public void RecorderOptions_DefaultsGpuKernelEtwToDisabled()
    {
        var options = new RecorderOptions();
        Assert.False(options.EnableDxgKrnlEtw);
        Assert.True(options.EnableSystemEtw);
    }

    [Theory]
    [InlineData(89.9f, "Normal")]
    [InlineData(90f, "Warning")]
    [InlineData(95f, "Throttled")]
    [InlineData(98f, "Critical")]
    public void CommitPressurePolicy_UsesDocumentedThresholds(float percent, string expected)
        => Assert.Equal(expected, CommitPressurePolicy.Classify(percent, new RecorderOptions()).ToString());

    [Fact]
    public void InvalidCommitPressureThresholdsAreRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecorderOptions(CommitWarningPercent: 98, CommitThrottlePercent: 95).Validate());

    [Fact]
    public void GpuCounterInstanceParser_PreservesAdapterAndEngineIdentity()
    {
        var ok = GpuCounterInstanceParser.TryParse(
            "pid_11248_luid_0x00000000_0x0000D67B_phys_1_eng_3_engtype_3D", out var item);

        Assert.True(ok);
        Assert.Equal(11248, item.ProcessId);
        Assert.Equal("0x00000000_0x0000D67B", item.AdapterLuid);
        Assert.Equal(1, item.PhysicalAdapterIndex);
        Assert.Equal(3, item.EngineIndex);
        Assert.Equal("3D", item.EngineType);
    }

    [Fact]
    public void GpuCounterInstanceParser_KeepsLegacyPidButMarksIdentityUnknown()
    {
        Assert.True(GpuCounterInstanceParser.TryParse("pid_42_legacy_engtype_Copy", out var item));
        Assert.Equal(42, item.ProcessId);
        Assert.Null(item.AdapterLuid);
        Assert.Null(item.PhysicalAdapterIndex);
        Assert.Equal("Copy", item.EngineType);
    }

    [Fact]
    public void ResourceExhaustionParser_CapturesSystemAndProcessUserData()
    {
        const string xml = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <UserData><MemoryExhaustionInfo>
                <SystemInfo><SystemCommitLimit>41999097856</SystemCommitLimit><SystemCommitCharge>41899986944</SystemCommitCharge>
                  <ProcessCommitCharge>31805566976</ProcessCommitCharge><PagedPoolUsage>1330241536</PagedPoolUsage>
                  <PhysicalMemorySize>24819101696</PhysicalMemorySize><PhysicalMemoryUsage>19842236416</PhysicalMemoryUsage>
                  <NonPagedPoolUsage>1991241728</NonPagedPoolUsage><Processes>311</Processes></SystemInfo>
                <ProcessInfo><Process_1><Name>game.exe</Name><ID>11248</ID><CreationTime>2026-09-18T11:11:52.7563860Z</CreationTime>
                  <CommitCharge>15948550144</CommitCharge><HandleCount>4318</HandleCount><Version>4.24.2.0</Version><TypeInfo>201</TypeInfo>
                </Process_1></ProcessInfo>
              </MemoryExhaustionInfo></UserData>
            </Event>
            """;

        var item = ResourceExhaustionEventTracker.Parse(xml, DateTimeOffset.Parse("2026-09-18T21:30:48+08:00"), 10965, "low virtual memory");

        Assert.Equal(41999097856, item.SystemCommitLimitBytes);
        Assert.Equal(41899986944, item.SystemCommitChargeBytes);
        Assert.Equal(311, item.ProcessCount);
        var process = Assert.Single(item.TopProcesses);
        Assert.Equal(11248, process.ProcessId);
        Assert.Equal(15948550144, process.CommitBytes);
        Assert.Equal(4318, process.HandleCount);
        Assert.Equal(xml, item.RawXml);
    }
}
