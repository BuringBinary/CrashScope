using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class HypothesisEvaluatorEtwTests
{
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SessionFacts DefaultFacts => new(
        Now.AddSeconds(-5),
        5,
        60,
        85f,
        true,
        Now);

    [Fact]
    public void HardFreeze_TdrTimedOutWithoutRecovery_GetsStrongSupport()
    {
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-10), 1, "TdrTimedOut", 1234, "game.exe", "Reason=Timeout")
        };

        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            etwEvents, null, null);

        var freeze = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardFreeze);
        Assert.NotNull(freeze);
        var tdrEvidence = freeze.Evidence.FirstOrDefault(e => e.Description.Contains("TdrTimedOut") && e.Supports);
        Assert.NotNull(tdrEvidence);
        Assert.Equal(3, tdrEvidence.Weight);
    }

    [Fact]
    public void HardFreeze_TdrTimedOutWithRecovery_GetsOpposingEvidence()
    {
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-12), 1, "TdrTimedOut", 1234, "game.exe", null),
            new(Now.AddSeconds(-10), 3, "TdrRecoverFromTimeout", 0, "System", "Status=0x0")
        };

        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            etwEvents, null, null);

        var freeze = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardFreeze);
        Assert.NotNull(freeze);
        var opposeEvidence = freeze.Evidence.FirstOrDefault(e =>
            e.Description.Contains("TDR timeout WITH recovery") && !e.Supports);
        Assert.NotNull(opposeEvidence);
        Assert.Equal(2, opposeEvidence.Weight);
    }

    [Fact]
    public void HardFreeze_AdapterStateChange_GetsPositiveEvidence()
    {
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-8), 253, "AdapterStateChange", null, null, "State=Stopped")
        };

        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            etwEvents, null, null);

        var freeze = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardFreeze);
        Assert.NotNull(freeze);
        var adapterEvidence = freeze.Evidence.FirstOrDefault(e =>
            e.Description.Contains("adapter state change") && e.Supports);
        Assert.NotNull(adapterEvidence);
    }

    [Fact]
    public void DriverTimeout_TdrRecoveryAtKernelLevel_GetsStrongSupport()
    {
        var events = new WindowsEventEvidence[]
        {
            new(Now.AddSeconds(-5), "Display", 4101, "Warning", "TDR occurred", null)
        };
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-7), 3, "TdrRecoverFromTimeout", 0, "System", "Status=0x0")
        };

        var result = HypothesisEvaluator.Evaluate(
            events, [], [], DefaultFacts, GraphicsContext.Empty,
            etwEvents, null, null);

        var tdr = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.DriverTimeoutRecovery);
        Assert.NotNull(tdr);
        var kernelEvidence = tdr.Evidence.FirstOrDefault(e =>
            e.Description.Contains("TDR recovery confirmed at kernel level") && e.Supports);
        Assert.NotNull(kernelEvidence);
        Assert.Equal(3, kernelEvidence.Weight);
    }

    [Fact]
    public void DriverTimeout_EngineReset_GetsPositiveEvidence()
    {
        var events = new WindowsEventEvidence[]
        {
            new(Now.AddSeconds(-5), "Display", 4101, "Warning", "TDR occurred", null)
        };
        var etwEvents = new EtwDxgKrnlEvent[]
        {
            new(Now.AddSeconds(-8), 800, "TdrEngineReset", 0, "System", "EngineType=3D")
        };

        var result = HypothesisEvaluator.Evaluate(
            events, [], [], DefaultFacts, GraphicsContext.Empty,
            etwEvents, null, null);

        var tdr = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.DriverTimeoutRecovery);
        Assert.NotNull(tdr);
        var engineEvidence = tdr.Evidence.FirstOrDefault(e =>
            e.Description.Contains("GPU engine reset") && e.Supports);
        Assert.NotNull(engineEvidence);
    }

    [Fact]
    public void HardFreeze_KernelPowerSuspend_GetsPositiveEvidence()
    {
        var etwKernelPower = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-Kernel-Power", Now.AddSeconds(-15),
                10, "SuspendStart", "Info", 4, "System", null)
        };

        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            null, etwKernelPower, null);

        var freeze = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardFreeze);
        Assert.NotNull(freeze);
        var suspendEvidence = freeze.Evidence.FirstOrDefault(e =>
            e.Description.Contains("suspend event") && e.Supports);
        Assert.NotNull(suspendEvidence);
        Assert.Equal(2, suspendEvidence.Weight);
    }

    [Fact]
    public void HardFreeze_ThermalEvent_GetsPositiveEvidence()
    {
        var etwKernelPower = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-Kernel-Power", Now.AddSeconds(-10),
                20, "ThermalZoneOverheated", "Error", null, null, "Temperature=105")
        };

        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            null, etwKernelPower, null);

        var freeze = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardFreeze);
        Assert.NotNull(freeze);
        var thermalEvidence = freeze.Evidence.FirstOrDefault(e =>
            e.Description.Contains("thermal event") && e.Supports);
        Assert.NotNull(thermalEvidence);
        Assert.Equal(2, thermalEvidence.Weight);
    }

    [Fact]
    public void HardFreeze_WheaRealTimeErrors_GetsStrongSupport()
    {
        var etwWhea = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-WHEA-Logger", Now.AddSeconds(-3),
                1, "GenericErrorEvent", "Error", null, null, "Severity=Fatal")
        };

        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            null, null, etwWhea);

        var freeze = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardFreeze);
        Assert.NotNull(freeze);
        var wheaEvidence = freeze.Evidence.FirstOrDefault(e =>
            e.Description.Contains("WHEA hardware error") && e.Supports);
        Assert.NotNull(wheaEvidence);
        Assert.Equal(3, wheaEvidence.Weight);
    }

    [Fact]
    public void PowerLoss_SuspendEvents_Opposes()
    {
        var etwKernelPower = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-Kernel-Power", Now.AddSeconds(-20),
                10, "SuspendStart", "Info", 4, "System", null)
        };

        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            null, etwKernelPower, null);

        var powerLoss = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.PowerLoss);
        Assert.NotNull(powerLoss);
        var opposeEvidence = powerLoss.Evidence.FirstOrDefault(e =>
            e.Description.Contains("system entered sleep") && !e.Supports);
        Assert.NotNull(opposeEvidence);
        Assert.Equal(2, opposeEvidence.Weight);
    }

    [Fact]
    public void HardwareError_WheaRealTime_GetsStrongSupport()
    {
        var events = new WindowsEventEvidence[]
        {
            new(Now.AddSeconds(-5), "Microsoft-Windows-WHEA-Logger", 19,
                "Error", "Fatal hardware error", null)
        };
        var etwWhea = new EtwProviderEvent[]
        {
            new("Microsoft-Windows-WHEA-Logger", Now.AddSeconds(-3),
                1, "GenericErrorEvent", "Error", null, null, "Severity=Fatal")
        };

        var result = HypothesisEvaluator.Evaluate(
            events, [], [], DefaultFacts, GraphicsContext.Empty,
            null, null, etwWhea);

        var hwError = result.Hypotheses.FirstOrDefault(h => h.Mode == FailureMode.HardwareError);
        Assert.NotNull(hwError);
        var realtimeEvidence = hwError.Evidence.FirstOrDefault(e =>
            e.Description.Contains("Real-time ETW WHEA") && e.Supports);
        Assert.NotNull(realtimeEvidence);
        Assert.Equal(3, realtimeEvidence.Weight);
    }

    [Fact]
    public void NoEtwEvents_StillProducesHypotheses()
    {
        var result = HypothesisEvaluator.Evaluate(
            [], [], [], DefaultFacts, GraphicsContext.Empty,
            null, null, null);

        Assert.NotEmpty(result.Hypotheses);
    }
}