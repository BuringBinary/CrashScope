using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class GpuTransitionDetectorTests
{
    private static readonly DateTimeOffset Start = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SensorCatalogEntry GpuLoadSensor(int id = 0)
        => new(id, "Gpu", "AMD Radeon RX 9070 XT", "Load", "GPU Core (0)", "GpuLoad");

    private static IReadOnlyList<TelemetrySnapshot> Telemetry(params float[] values)
        => values.Select((v, i) => new TelemetrySnapshot(Start.AddSeconds(i), new ForegroundState(1, "game", 0), [v])).ToList();

    [Fact]
    public void Detect_SuddenLoadDrop_ReportsStepTransition()
    {
        var values = Enumerable.Repeat(80f, 10).Concat(Enumerable.Repeat(5f, 10)).ToArray();
        var transitions = GpuTransitionDetector.Detect([GpuLoadSensor()], Telemetry(values));

        var step = transitions.Single(t => !t.IsTrend);
        Assert.Equal("Load", step.SensorType);
        Assert.True(step.FromValue > 70);
        Assert.True(step.ToValue < 10);
    }

    [Fact]
    public void Detect_StableLoad_NoTransitions()
    {
        var values = Enumerable.Repeat(80f, 30).ToArray();
        var transitions = GpuTransitionDetector.Detect([GpuLoadSensor()], Telemetry(values));

        Assert.Empty(transitions);
    }

    [Fact]
    public void Detect_GradualDrift_ReportsTrend()
    {
        // Slow decline crossing the 25% absolute threshold over more than 60 s.
        var values = Enumerable.Range(0, 120).Select(i => 90f - i * 0.5f).ToArray();
        var transitions = GpuTransitionDetector.Detect([GpuLoadSensor()], Telemetry(values));

        Assert.Contains(transitions, t => t.IsTrend);
    }

    [Fact]
    public void Detect_TooFewSamples_NoTransitions()
    {
        var values = new[] { 90f, 10f, 80f, 5f, 70f };
        var transitions = GpuTransitionDetector.Detect([GpuLoadSensor()], Telemetry(values));

        Assert.Empty(transitions);
    }
}

