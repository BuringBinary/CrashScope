using System.Text.Json;
using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class RealMachineFixTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-18T09:16:54+08:00");
    private static SessionFacts Facts => new(Now, 0, 0, 10, true, Now);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static SensorCatalogEntry Sensor(int id, string hardware, string name, string type, string sensor)
        => new(id, hardware, name, type, sensor, $"/{name}/{id}");

    [Fact]
    public void CombinedResourcePressure_WithoutSystemEvidence_RemainsUnclassified()
    {
        var result = HypothesisEvaluator.Evaluate([], [], [], Facts, GraphicsContext.Empty,
            memoryPressureEvents: [new MemoryPressureSample(Now, 31L << 30, 32L << 30, 100L << 20, 97)]);
        Assert.Equal(FailureMode.Unclassified, result.Hypotheses[0].Mode);
        Assert.All(result.Hypotheses, h => Assert.True(h.Score <= 0));
    }

    [Fact]
    public void KernelTimeout_StillProducesCandidateButNotCertainty()
    {
        var result = HypothesisEvaluator.Evaluate([], [], [], Facts, GraphicsContext.Empty,
            etwEvents: [new EtwDxgKrnlEvent(Now, 1, "TdrTimedOut", 1, "gpu", "timeout")]);
        var freeze = result.Hypotheses.Single(h => h.Mode == FailureMode.HardFreeze);
        Assert.True(freeze.Score > 0);
        Assert.InRange(freeze.Confidence, 0.001, 0.99);
    }

    [Fact]
    public void EmptyCatalogAndRestart_DoNotReuseOldIndicesOrMixAdapters()
    {
        var empty = DesktopSensorMap.Build([]);
        Assert.Equal(-1, empty.GpuPower);
        Assert.Equal(-1, empty.CpuLoad);
        SensorCatalogEntry[] catalog = [
            Sensor(0, "GpuAmd", "Integrated", "Power", "GPU Core"),
            Sensor(1, "GpuAmd", "Integrated", "Load", "GPU Core"),
            Sensor(2, "GpuAmd", "RX 9070 XT", "Control", "GPU Fan"),
            Sensor(3, "GpuAmd", "RX 9070 XT", "Load", "GPU Core"),
            Sensor(4, "Memory", "Virtual Memory", "Data", "Memory Used"),
            Sensor(5, "Memory", "Total Memory", "Data", "Memory Used"),
            Sensor(6, "Cpu", "9700X", "Temperature", "Core (Tctl/Tdie)")
        ];
        var map = DesktopSensorMap.Build(catalog);
        Assert.Equal(3, map.GpuLoad);
        Assert.Equal(-1, map.GpuPower); // must NOT borrow iGPU power
        Assert.Equal(5, map.RamUsed);
        Assert.Equal(6, map.CpuTemp);
        Assert.Equal(-1, DesktopSensorMap.Build([]).GpuLoad);
    }

    [Theory]
    [InlineData("Temperature", "Core (Tctl/Tdie)")]
    [InlineData("Power", "Package")]
    [InlineData("Clock", "Cores (Average)")]
    public void SuspectCpuZero_IsUnavailableForDisplayButInputIsUnchanged(string type, string name)
    {
        var entry = Sensor(0, "Cpu", "9700X", type, name);
        float?[] raw = [0f];
        Assert.Null(SensorValueQuality.ForDisplay(entry, raw[0]));
        Assert.True(SensorValueQuality.HasSuspectReadings([entry], raw));
        Assert.Equal(0f, raw[0]);
        Assert.Equal(50f, SensorValueQuality.ForDisplay(entry, 50f));
    }

    [Theory]
    [InlineData("Cpu", "Load", "CPU Total")]
    [InlineData("Cpu", "Clock", "Core #1 (Effective)")]
    [InlineData("GpuAmd", "Clock", "GPU Core")]
    [InlineData("GpuAmd", "Fan", "GPU Fan")]
    public void ValidIdleZero_IsNotDiscarded(string hardware, string type, string name)
        => Assert.Equal(0f, SensorValueQuality.ForDisplay(Sensor(0, hardware, "Test", type, name), 0f));

    [Fact]
    public void NonFiniteAndMissingValues_AreUnavailable()
    {
        var entry = Sensor(0, "Cpu", "Test", "Load", "CPU Total");
        Assert.Null(SensorValueQuality.ForDisplay(entry, float.NaN));
        Assert.Null(SensorValueQuality.ForDisplay(entry, float.PositiveInfinity));
        Assert.Null(SensorValueQuality.ForDisplay(entry, null));
        Assert.False(SensorValueQuality.HasSuspectReadings([entry], []));
    }

    [Fact]
    public void CollectorHealth_PersistsDistinctStatesWithoutRepeatedWrites()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CrashScope-health-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "collector-health.json");
            var tracker = new CollectorHealthTracker(path);
            tracker.Set("ETW GPU", "PermissionDenied", "Start denied");
            var before = File.ReadAllBytes(path);
            tracker.Set("ETW GPU", "PermissionDenied", "Start denied");
            Assert.Equal(before, File.ReadAllBytes(path));
            tracker.Set("Storage", "NoData", "disks=[]");
            Assert.Equal(2, tracker.Snapshot().Count);
            Assert.Contains("PermissionDenied", File.ReadAllText(path));
            tracker.Set("Storage", "Collecting", "Disk metrics received");
            Assert.Equal("Collecting", tracker.Snapshot().Single(h => h.Collector == "Storage").Status);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void CollectorHealth_FailedPersistenceDoesNotPublishSuccess()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CrashScope-health-blocked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tracker = new CollectorHealthTracker(dir); // a directory cannot be replaced by a JSON file
            var error = Record.Exception(() => tracker.Set("Storage", "Collecting", "metrics"));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Empty(tracker.Snapshot());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadOnlyReplay_DoesNotTrustLegacyRankingOrOverwriteEvidence(bool validEvents)
    {
        var dir = Path.Combine(Path.GetTempPath(), "CrashScope-replay-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Deserialize keeps this fixture independent of optional SessionState constructor fields.
            var session = JsonSerializer.Deserialize<SessionState>("""
                {"sessionId":"bf0ada058c2e4ef68cf19cf9b8376bd4","startedAt":"2026-09-18T09:16:44+08:00",
                 "lastHeartbeatAt":"2026-09-18T09:16:54+08:00","cleanShutdown":false,
                 "sessionDirectory":"unused","elevated":false,"agentVersion":"0.9.0.0","neverStarted":false}
                """, Json)!;
            var stored = new IncidentAnalysis(session, Now, 0, 0, 10, [], [],
                [new FailureHypothesis(FailureMode.HardFreeze, 3, 1, [])], [], GraphicsContext.Empty);
            File.WriteAllText(Path.Combine(dir, "analysis.json"), JsonSerializer.Serialize(stored, Json));
            File.WriteAllText(Path.Combine(dir, "summary.md"), "legacy 100% - preserve");
            File.WriteAllText(Path.Combine(dir, "windows-events.json"), validEvents ? "[]" : "invalid");
            File.WriteAllText(Path.Combine(dir, "external-evidence.json"), "[]");
            File.WriteAllText(Path.Combine(dir, "gpu-memory-tail.jsonl"), JsonSerializer.Serialize(
                new GpuMemorySample(Now, [new ProcessGpuMemory(20572, "GameViewerServer", 33109295104, 12582912)]), Json));
            var before = Directory.GetFiles(dir).ToDictionary(p => p, File.ReadAllBytes);
            var updated = IncidentAnalyzer.ReevaluateReadOnly(stored, dir);
            Assert.Equal(FailureMode.Unclassified, updated.Hypotheses[0].Mode);
            Assert.DoesNotContain(updated.Hypotheses, h => h.Mode == FailureMode.HardFreeze && h.Score > 0);
            Assert.Equal(before.Count, Directory.GetFiles(dir).Length);
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
