using CrashScope.Core.Models;
using System.Text.Json;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class EtwModelJsonRoundtripTests
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void EtwDxgKrnlEvent_SerializesAndDeserializes()
    {
        var evt = new EtwDxgKrnlEvent(
            new DateTimeOffset(2025, 6, 1, 12, 30, 45, TimeSpan.Zero),
            700,
            "TdrEngineResetPrepare",
            1234,
            "game.exe",
            "EngineType=3D; NodeId=0");

        var json = JsonSerializer.Serialize(evt, _json);
        var roundtrip = JsonSerializer.Deserialize<EtwDxgKrnlEvent>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Equal(evt.Timestamp, roundtrip.Timestamp);
        Assert.Equal(700, roundtrip.EventId);
        Assert.Equal("TdrEngineResetPrepare", roundtrip.EventName);
        Assert.Equal(1234, roundtrip.ProcessId);
        Assert.Equal("game.exe", roundtrip.ProcessName);
        Assert.Equal("EngineType=3D; NodeId=0", roundtrip.Detail);
    }

    [Fact]
    public void EtwDxgKrnlEvent_NullOptionalFields_HandlesGracefully()
    {
        var evt = new EtwDxgKrnlEvent(
            DateTimeOffset.Now,
            1,
            "TdrTimedOut",
            null,
            null,
            null);

        var json = JsonSerializer.Serialize(evt, _json);
        var roundtrip = JsonSerializer.Deserialize<EtwDxgKrnlEvent>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Null(roundtrip.ProcessId);
        Assert.Null(roundtrip.ProcessName);
        Assert.Null(roundtrip.Detail);
    }

    [Fact]
    public void EtwDxgKrnlEvent_TdrRecovery_RoundtripsCorrectly()
    {
        var evt = new EtwDxgKrnlEvent(
            new DateTimeOffset(2025, 7, 15, 18, 5, 0, TimeSpan.Zero),
            3,
            "TdrRecoverFromTimeout",
            0,
            "System",
            "Status=0x0; RecoverySuccessful=true");

        var json = JsonSerializer.Serialize(evt, _json);
        var roundtrip = JsonSerializer.Deserialize<EtwDxgKrnlEvent>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Equal(3, roundtrip.EventId);
        Assert.Contains("RecoverySuccessful=true", roundtrip.Detail);
    }

    [Fact]
    public void EtwProviderEvent_SerializesAndDeserializes()
    {
        var evt = new EtwProviderEvent(
            "Microsoft-Windows-Kernel-Power",
            new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero),
            15,
            "SuspendStart",
            "Info",
            4,
            "System",
            "SuspendReason=IdleTimeout; DeepestSleepState=S4");

        var json = JsonSerializer.Serialize(evt, _json);
        var roundtrip = JsonSerializer.Deserialize<EtwProviderEvent>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Equal("Microsoft-Windows-Kernel-Power", roundtrip.ProviderName);
        Assert.Equal(15, roundtrip.EventId);
        Assert.Equal("SuspendStart", roundtrip.EventName);
        Assert.Equal("Info", roundtrip.Level);
        Assert.Equal(4, roundtrip.ProcessId);
        Assert.Equal("System", roundtrip.ProcessName);
    }

    [Fact]
    public void EtwProviderEvent_NullOptionalFields_HandlesGracefully()
    {
        var evt = new EtwProviderEvent(
            "Microsoft-Windows-WHEA-Logger",
            DateTimeOffset.Now,
            1,
            "ErrorEvent",
            "Error",
            null,
            null,
            null);

        var json = JsonSerializer.Serialize(evt, _json);
        var roundtrip = JsonSerializer.Deserialize<EtwProviderEvent>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Null(roundtrip.ProcessId);
        Assert.Null(roundtrip.ProcessName);
        Assert.Null(roundtrip.Detail);
    }

    [Fact]
    public void EtwProviderEvent_WheaError_RoundtripsCorrectly()
    {
        var evt = new EtwProviderEvent(
            "Microsoft-Windows-WHEA-Logger",
            new DateTimeOffset(2025, 8, 20, 22, 10, 33, TimeSpan.Zero),
            5,
            "GenericErrorEvent",
            "Error",
            null,
            null,
            "ErrorSource=PCIe; Severity=Fatal");

        var json = JsonSerializer.Serialize(evt, _json);
        var roundtrip = JsonSerializer.Deserialize<EtwProviderEvent>(json, _json);

        Assert.NotNull(roundtrip);
        Assert.Equal("Error", roundtrip.Level);
        Assert.Contains("PCIe", roundtrip.Detail);
    }

    [Fact]
    public void ProviderConfig_Equality_WorksForMatchingGuids()
    {
        var a = new Monitoring.ProviderConfig(
            Guid.NewGuid(), "TestProvider", "test.jsonl",
            _ => true);
        var b = a with { };

        Assert.Equal(a, b);
    }
}