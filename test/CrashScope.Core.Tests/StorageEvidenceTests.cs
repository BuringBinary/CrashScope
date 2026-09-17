using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class StorageEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SessionFacts DefaultFacts => new(Now.AddSeconds(-5), 5, 60, 15f, true, Now);

    private static EvaluationResult EvaluateWithStorage(StorageSample sample)
        => HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            null, null, null, [sample], null, null, null);

    [Fact]
    public void HotDisk_WithPressure_AddsHardFreezeEvidence()
    {
        var disk = new DiskMetrics("WDC WD10EZEX (Disk 0)", "WDC WD10EZEX-08WN4A0", 80f, "100%", 99f, 200f, 150f, 12f);
        var result = EvaluateWithStorage(new StorageSample(Now.AddSeconds(-10), [disk]));

        var freeze = result.Hypotheses.First(h => h.Mode == FailureMode.HardFreeze);
        Assert.Contains(freeze.Evidence, e => e.Supports && e.Description.Contains("temperature"));
        Assert.Contains(freeze.Evidence, e => e.Supports && e.Description.Contains("heavy pressure"));
    }

    [Fact]
    public void HighLatency_AddsOpenQuestion()
    {
        var disk = new DiskMetrics("NVMe (Disk 1)", "SAMSUNG 980", null, null, 40f, 250f, 300f, 2f);
        var result = EvaluateWithStorage(new StorageSample(Now.AddSeconds(-10), [disk]));

        Assert.Contains(result.OpenQuestions, q => q.Contains("latency"));
    }

    [Fact]
    public void HealthyDisk_NoStorageEvidence()
    {
        var disk = new DiskMetrics("NVMe (Disk 0)", "SAMSUNG 980", 40f, "100%", 20f, 5f, 8f, 1f);
        var result = EvaluateWithStorage(new StorageSample(Now.AddSeconds(-10), [disk]));

        var freeze = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardFreeze);
        Assert.DoesNotContain(freeze?.Evidence ?? [], e =>
            e.Description.Contains("temperature") || e.Description.Contains("latency"));
    }
}

