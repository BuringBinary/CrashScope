using System.Text.Json;
using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class CollectionChainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-18T11:00:00+08:00");
    private static EtwCoverageTracker Coverage() => new("TestProvider", Guid.Parse("802ec45a-1e99-4b83-9920-87c98277ba9d"));
    private static DiskMetrics Disk(string name) => new(name, null, null, null, null, null, null, null);
    private static SensorCatalogEntry Cpu(int id, string type, string name) => new(id, "Cpu", "9700X", type, name, $"/cpu/{id}");

    [Fact]
    public void EnabledWithoutEvents_IsStartedNotObserved()
    {
        var c = Coverage(); c.Enabled(); c.Processing(true);
        Assert.Equal("Started", EtwCoverageTracker.Status(c.Snapshot()));
        Assert.Equal(0, c.Snapshot().WrittenEvents);
        Assert.Contains("coverage is not verified", EtwCoverageTracker.Detail(c.Snapshot()));
    }

    [Fact]
    public void FilteredOutEvents_ProveReceivingButNotPersistedCoverage()
    {
        var c = Coverage(); c.Enabled(); c.Received(Now);
        Assert.Equal("Receiving", EtwCoverageTracker.Status(c.Snapshot()));
        Assert.Equal(1, c.Snapshot().ReceivedEvents);
        Assert.Equal(0, c.Snapshot().MatchedEvents);
        Assert.Equal(Now, c.Snapshot().LastEventAt);
    }

    [Fact]
    public void MatchedEvents_AreWrittenOnlyOnceOnDispose()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CrashScope-chain-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "events.jsonl");
        try
        {
            var c = Coverage(); c.Enabled();
            var writer = new CrashSafeJsonlWriter(path);
            var sink = new EtwEventSink<int>(c, e => writer.Write(e), writer);
            c.Received(Now); sink.Record(1, Now);
            c.Received(Now); sink.Record(2, Now);
            // Do not drain: old monitors duplicated these records on stop.
            sink.Dispose(); sink.Dispose(); sink.Record(3, Now);
            Assert.Equal(new[] { "1", "2" }, File.ReadAllLines(path));
            Assert.Equal(2, c.Snapshot().WrittenEvents);
            Assert.Equal("Observed", EtwCoverageTracker.Status(c.Snapshot()));
            Assert.Empty(sink.Drain());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Drain_IsAnExactlyOnceHandoffNotAnotherWrite()
    {
        var c = Coverage(); var writes = 0;
        using var sink = new EtwEventSink<int>(c, _ => writes++);
        sink.Record(1, Now); sink.Record(2, Now);
        Assert.Equal(new[] { 1, 2 }, sink.Drain()); Assert.Empty(sink.Drain());
        Assert.Equal(2, writes);
    }

    [Fact]
    public void QueueOverflow_DoesNotDiscardDurableWrites()
    {
        var c = Coverage(); var writes = 0;
        using var sink = new EtwEventSink<int>(c, _ => writes++, capacity: 3);
        for (var i = 0; i < 10; i++) { c.Received(Now); sink.Record(i, Now); }
        Assert.Equal(10, writes); Assert.Equal(7, c.Snapshot().BufferDroppedEvents);
        Assert.Equal(new[] { 7, 8, 9 }, sink.Drain());
        Assert.Equal(10, c.Snapshot().ReceivedEvents); Assert.Equal(10, c.Snapshot().MatchedEvents);
    }

    [Fact]
    public void WriteFailure_IsNotCountedAsWritten_AndStaysVisibleAfterStop()
    {
        var c = Coverage(); c.Enabled(); c.Received(Now);
        using var sink = new EtwEventSink<int>(c, _ => throw new IOException("disk full"));
        sink.Record(1, Now); c.Stopped();
        Assert.Equal(1, c.Snapshot().MatchedEvents); Assert.Equal(0, c.Snapshot().WrittenEvents);
        Assert.Null(c.Snapshot().LastWrittenAt);
        Assert.Equal("Error", EtwCoverageTracker.Status(c.Snapshot()));
        Assert.Contains("disk full", c.Snapshot().LastError);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void PermissionErrors_RemainDistinctAndAreNotOverwritten(int variant)
    {
        Exception ex = variant switch { 0 => new UnauthorizedAccessException("denied"),
            1 => new System.Runtime.InteropServices.COMException("denied", unchecked((int)0x80070005)),
            _ => new System.ComponentModel.Win32Exception(5) };
        var c = Coverage(); c.Error("Enable", ex); c.Error("Shutdown", new IOException("other"));
        Assert.Equal("PermissionDenied", EtwCoverageTracker.Status(c.Snapshot()));
        Assert.Contains("Enable", c.Snapshot().LastError);
    }

    [Fact]
    public void Providers_HaveIndependentEnableAndErrorStates()
    {
        var first = Coverage(); var second = Coverage();
        first.Error("Enable", new UnauthorizedAccessException()); second.Enabled();
        Assert.Equal("PermissionDenied", EtwCoverageTracker.Status(first.Snapshot()));
        Assert.Equal("Started", EtwCoverageTracker.Status(second.Snapshot()));
    }

    [Fact]
    public void StoppedCoverage_RetainsCountsButDoesNotClaimProcessing()
    {
        var c = Coverage(); c.Enabled(); c.Processing(true); c.Received(Now); c.Matched(); c.Written(Now); c.Stopped();
        Assert.True(c.Snapshot().Stopped); Assert.False(c.Snapshot().Processing);
        Assert.Equal(1, c.Snapshot().WrittenEvents); Assert.Equal(Now, c.Snapshot().LastWrittenAt);
    }

    [Fact]
    public void CoverageCounters_AreThreadSafe()
    {
        var c = Coverage();
        Parallel.For(0, 1000, _ => { c.Received(Now); c.Matched(); c.Written(Now); });
        Assert.Equal(1000, c.Snapshot().ReceivedEvents); Assert.Equal(1000, c.Snapshot().WrittenEvents);
    }

    [Fact]
    public void HealthSchema_ReadsLegacyRowsAndRoundtripsCoverage()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var old = JsonSerializer.Deserialize<CollectorHealth>("{\"collector\":\"ETW\",\"status\":\"Started\",\"detail\":\"legacy\",\"changedAt\":\"2026-09-18T00:00:00Z\"}", json)!;
        Assert.Null(old.Etw);
        var item = old with { Etw = Coverage().Snapshot() };
        Assert.Equal(item, JsonSerializer.Deserialize<CollectorHealth>(JsonSerializer.Serialize(item, json), json));
    }

    [Fact]
    public void HealthPersistence_UpdatesWhenCoverageChangesEvenWithSameStatus()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CrashScope-health-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tracker = new CollectorHealthTracker(Path.Combine(dir, "health.json")); var c = Coverage();
            tracker.Set("ETW", "Receiving", "unchanged detail", c.Snapshot()); c.Received(Now);
            tracker.Set("ETW", "Receiving", "unchanged detail", c.Snapshot());
            Assert.Equal(1, tracker.Snapshot().Single().Etw!.ReceivedEvents);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Monitors_DisposeWithoutStarting_DoesNotOpenEtw()
    {
        using var gpu = new EtwDxgKrnlMonitor();
        using var system = new EtwRealTimeMonitor(new ProviderConfig(Guid.NewGuid(), "test", "test.jsonl", _ => true));
        gpu.Stop(); system.Stop();
        Assert.False(gpu.IsRunning); Assert.False(system.IsRunning);
        Assert.True(gpu.Coverage.Stopped);
    }

    [Fact]
    public void DuplicateProviderFiles_AreRejectedBeforeStarting()
    {
        var p = new ProviderConfig(Guid.NewGuid(), "a", "same.jsonl", _ => true);
        Assert.Throws<ArgumentException>(() => new EtwRealTimeMonitor(p, p with { ProviderGuid = Guid.NewGuid() }));
    }

    [Fact]
    public void WindowsCounters_AreEmittedWithoutLhmDevices()
    {
        var sample = StorageSampleBuilder.Build(Now, [], new Dictionary<int, DiskPerformance> { [0] = new(0, 0, 2, 0) },
            new Dictionary<int, string?> { [0] = "INTEL SSD" });
        var disk = Assert.Single(sample.Disks);
        Assert.Equal(0, disk.DiskIndex); Assert.Equal("INTEL SSD", disk.Model);
        Assert.Equal(2, disk.AvgWriteLatencyMs); Assert.True(StorageSampleBuilder.HasMetrics(disk));
        Assert.Null(disk.TemperatureCelsius); Assert.Null(disk.HealthStatus);
        Assert.Contains("PhysicalDisk", disk.PerformanceSource);
    }

    [Fact]
    public void InventoryOnly_IsNotTreatedAsValidMetrics()
    {
        var sample = StorageSampleBuilder.Build(Now, [], new Dictionary<int, DiskPerformance>(), new Dictionary<int, string?> { [0] = "SSD" });
        Assert.False(StorageSampleBuilder.HasMetrics(Assert.Single(sample.Disks)));
    }

    [Fact]
    public void UnknownHardwareIndex_DoesNotBorrowAnotherDiskMetrics()
    {
        var sample = StorageSampleBuilder.Build(Now, [Disk("Unknown") with { TemperatureCelsius = 45 }],
            new Dictionary<int, DiskPerformance> { [0] = new(80, 4, 5, 3) }, new Dictionary<int, string?>());
        Assert.Equal(2, sample.Disks.Count);
        Assert.Null(sample.Disks[0].DiskTimePercent); Assert.Null(sample.Disks[0].DiskIndex);
        Assert.Equal(80, sample.Disks[1].DiskTimePercent);
    }

    [Fact]
    public void ExplicitIndexes_KeepPhysicalDiskMetricsSeparate()
    {
        var sample = StorageSampleBuilder.Build(Now, [Disk("SSD (Disk 1)"), Disk("HDD (Disk 0)")],
            new Dictionary<int, DiskPerformance> { [0] = new(10, 1, 2, 0), [1] = new(90, 100, 200, 5) }, new Dictionary<int, string?>());
        Assert.Equal(90, sample.Disks[0].DiskTimePercent); Assert.Equal(10, sample.Disks[1].DiskTimePercent);
        Assert.Equal(2, sample.Disks.Count);
    }

    [Fact]
    public void AmbiguousModelNames_DoNotGuessDiskIdentity()
    {
        var sample = StorageSampleBuilder.Build(Now, [Disk("Same SSD")], new Dictionary<int, DiskPerformance>(),
            new Dictionary<int, string?> { [0] = "Same SSD", [1] = "Same SSD" });
        Assert.Null(sample.Disks[0].DiskIndex); Assert.Equal(3, sample.Disks.Count);
    }

    [Theory]
    [InlineData("0 C: D:", 0)] [InlineData("12", 12)] [InlineData("_Total", null)]
    [InlineData("C:", null)] [InlineData("123abc", null)] [InlineData("99999999999999999999", null)]
    public void OnlyPhysicalInstanceIndexes_AreAccepted(string instance, int? expected)
        => Assert.Equal(expected, StorageSampleBuilder.InstanceIndex(instance));

    [Theory]
    [InlineData(0f, true)] [InlineData(-1f, false)] [InlineData(float.NaN, false)] [InlineData(float.PositiveInfinity, false)]
    public void CounterValidity_PreservesIdleZero(float value, bool valid)
        => Assert.Equal(valid, StorageSampleBuilder.Valid(value).HasValue);

    [Fact]
    public void LegacyStorageJson_RemainsReadable()
    {
        var d = JsonSerializer.Deserialize<DiskMetrics>("{\"name\":\"Disk 0\",\"diskTimePercent\":0}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(d.DiskIndex); Assert.Null(d.PerformanceSource); Assert.Equal(0, d.DiskTimePercent);
    }

    [Fact]
    public void MissingCpuCatalog_IsNotReportedAsHealthy()
    {
        var (status, detail) = CpuSensorAssessment.Assess([], []);
        Assert.Equal("Degraded", status); Assert.Contains("NotExposed", detail);
        Assert.Contains("cannot be inferred", detail);
    }

    [Fact]
    public void CpuAssessment_UsesSensorIdsAndPreservesRawValues()
    {
        float?[] values = [0, null, 0, 0, 0];
        var result = CpuSensorAssessment.Assess([Cpu(0,"Load","Total"),Cpu(2,"Temperature","Tctl"),Cpu(3,"Clock","Cores (Average)"),Cpu(4,"Power","Package")], values);
        Assert.Equal("Degraded", result.Status); Assert.Contains("Load=Available", result.Detail);
        Assert.Contains("Temperature=Unavailable", result.Detail); Assert.Equal(0, values[2]);
    }

    [Fact]
    public void CpuAssessment_AllValidMetricsCanBeCollecting()
    {
        var result = CpuSensorAssessment.Assess([Cpu(0,"Load","Total"),Cpu(1,"Temperature","Tctl"),Cpu(2,"Clock","Cores (Average)"),Cpu(3,"Power","Package")], [0,45,4000,30]);
        Assert.Equal("Collecting", result.Status);
    }

    [Theory]
    [InlineData(-1)] [InlineData(999)]
    public void CpuAssessment_OutOfRangeIdsAreUnavailable(int id)
    {
        var result = CpuSensorAssessment.Assess([Cpu(id,"Temperature","Tctl")], [45]);
        Assert.Contains("Temperature=Unavailable", result.Detail);
    }
}
