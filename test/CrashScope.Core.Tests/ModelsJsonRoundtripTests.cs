using CrashScope.Core.Models;
using System.Text.Json;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class ModelsJsonRoundtripTests
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SessionState_SerializesAndDeserializes()
    {
        var session = new SessionState(
            "abc123def456",
            new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 6, 1, 13, 0, 0, TimeSpan.Zero),
            false,
            @"C:\data\sessions\20250601-120000",
            true,
            "0.4.0");

        var json = JsonSerializer.Serialize(session, _json);
        var roundtrip = JsonSerializer.Deserialize<SessionState>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Equal(session.SessionId, roundtrip.SessionId);
        Assert.Equal(session.CleanShutdown, roundtrip.CleanShutdown);
        Assert.Equal(session.Elevated, roundtrip.Elevated);
        Assert.Equal(session.AgentVersion, roundtrip.AgentVersion);
    }

    [Fact]
    public void FailureHypothesis_SerializationRoundtrip()
    {
        var hypothesis = new FailureHypothesis(
            FailureMode.HardFreeze,
            10,
            0.85,
            [
                new EvidenceItem(true, 5, "GPU disappeared from sensors"),
                new EvidenceItem(false, 2, "CPU was under light load")
            ]);

        var json = JsonSerializer.Serialize(hypothesis, _json);
        var roundtrip = JsonSerializer.Deserialize<FailureHypothesis>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Equal(FailureMode.HardFreeze, roundtrip.Mode);
        Assert.Equal(10, roundtrip.Score);
        Assert.Equal(0.85, roundtrip.Confidence, precision: 3);
        Assert.Equal(2, roundtrip.Evidence.Count);
        Assert.True(roundtrip.Evidence[0].Supports);
        Assert.Equal(5, roundtrip.Evidence[0].Weight);
    }

    [Fact]
    public void IncidentAnalysis_SerializationRoundtrip()
    {
        var analysis = new IncidentAnalysis(
            new SessionState("test1", DateTimeOffset.Now, DateTimeOffset.Now, false, "dir", false, "0.4.0"),
            DateTimeOffset.Now,
            1.5,
            120,
            95.5f,
            [
                new SensorTransition(
                    DateTimeOffset.Now, "GPU", "Core Load", "Load",
                    45f, 0f, 0.5, true, "GPU load dropped to zero")
            ],
            [
                new TimelineEntry(DateTimeOffset.Now, "GPU", "GPU sensor vanished")
            ],
            [
                new FailureHypothesis(FailureMode.DriverTimeoutRecovery, 7, 0.7, [
                    new EvidenceItem(true, 3, "TDR detected")
                ])
            ],
            ["Why did the GPU drop?", "Was driver updated recently?"],
            GraphicsContext.Empty);

        var json = JsonSerializer.Serialize(analysis, _json);
        var roundtrip = JsonSerializer.Deserialize<IncidentAnalysis>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Single(roundtrip.Hypotheses);
        Assert.Equal(FailureMode.DriverTimeoutRecovery, roundtrip.Hypotheses[0].Mode);
        Assert.Single(roundtrip.GpuTransitions);
        Assert.Equal(2, roundtrip.OpenQuestions.Count);
    }

    [Fact]
    public void TelemetrySnapshot_SerializationRoundtrip()
    {
        var snapshot = new TelemetrySnapshot(
            DateTimeOffset.Now,
            new ForegroundState(1234, "notepad.exe", 5),
            [45.2f, 60.1f, null, 8192f]);

        var json = JsonSerializer.Serialize(snapshot, _json);
        var roundtrip = JsonSerializer.Deserialize<TelemetrySnapshot>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Equal(1234, roundtrip.Foreground.ProcessId);
        Assert.Equal("notepad.exe", roundtrip.Foreground.ProcessName);
        Assert.Equal(4, roundtrip.SensorValues.Count);
        Assert.Equal(45.2f, roundtrip.SensorValues[0]);
        Assert.Null(roundtrip.SensorValues[2]);
    }

    [Fact]
    public void NullForegroundState_HandlesGracefully()
    {
        var snapshot = new TelemetrySnapshot(
            DateTimeOffset.Now,
            new ForegroundState(null, null, 300),
            [1f, 2f]);

        var json = JsonSerializer.Serialize(snapshot, _json);
        var roundtrip = JsonSerializer.Deserialize<TelemetrySnapshot>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Null(roundtrip.Foreground.ProcessId);
        Assert.Null(roundtrip.Foreground.ProcessName);
        Assert.Equal(300, roundtrip.Foreground.IdleSeconds);
    }

    [Fact]
    public void FailureMode_SerializesAsString()
    {
        var modes = new[]
        {
            FailureMode.HardFreeze,
            FailureMode.BlueScreen,
            FailureMode.DriverTimeoutRecovery,
            FailureMode.HardwareError,
            FailureMode.AppCrash,
            FailureMode.PowerLoss,
            FailureMode.Unclassified
        };

        foreach (var mode in modes)
        {
            var json = JsonSerializer.Serialize(mode, _json);
            Assert.Contains(mode.ToString(), json);
            var roundtrip = JsonSerializer.Deserialize<FailureMode>(json, _json);
            Assert.Equal(mode, roundtrip);
        }
    }
}