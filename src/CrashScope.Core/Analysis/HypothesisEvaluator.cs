using System.Globalization;
using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public sealed record SessionFacts(
    DateTimeOffset? LastTelemetrySampleAt,
    double TelemetryLagSeconds,
    long FinalIdleSeconds,
    float? FinalGpuLoadPercent,
    bool HasGpuSensors,
    DateTimeOffset LastHeartbeatAt);

public sealed record EvaluationResult(
    IReadOnlyList<FailureHypothesis> Hypotheses,
    IReadOnlyList<string> OpenQuestions);

public static class HypothesisEvaluator
{
    private const int FullConfidenceEvidenceMass = 4;
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(10);

    public static EvaluationResult Evaluate(
        IReadOnlyList<WindowsEventEvidence> events,
        IReadOnlyList<ExternalEvidenceFile> external,
        IReadOnlyList<SensorTransition> transitions,
        SessionFacts facts,
        GraphicsContext? graphics,
        IReadOnlyList<EtwDxgKrnlEvent>? etwEvents,
        IReadOnlyList<EtwProviderEvent>? etwKernelPower,
        IReadOnlyList<EtwProviderEvent>? etwWhea,
        IReadOnlyList<StorageSample>? storageEvents)
    {
        var signals = ExtractSignals(events, external, transitions, etwEvents, etwKernelPower, etwWhea, storageEvents);

        var scored = new List<(FailureMode Mode, List<EvidenceItem> Evidence)>
        {
            (FailureMode.HardFreeze, EvaluateHardFreeze(signals, facts, graphics)),
            (FailureMode.BlueScreen, EvaluateBlueScreen(signals)),
            (FailureMode.DriverTimeoutRecovery, EvaluateDriverTimeout(signals, graphics)),
            (FailureMode.HardwareError, EvaluateHardwareError(signals)),
            (FailureMode.AppCrash, EvaluateAppCrash(signals)),
            (FailureMode.PowerLoss, EvaluatePowerLoss(signals))
        }
            .Where(c => c.Evidence.Count > 0)
            .Select(c => Score(c.Mode, c.Evidence))
            .ToList();

        var positiveTotal = scored.Where(h => h.Score > 0).Sum(h => h.Score);
        var hypotheses = scored
            .Select(h =>
            {
                if (h.Score <= 0 || positiveTotal <= 0)
                    return h;

                var strength = h.Evidence.Where(e => e.Supports).Sum(e => e.Weight);
                var massCap = Math.Min(1.0, strength / (double)FullConfidenceEvidenceMass);
                return h with { Confidence = massCap * (h.Score / (double)positiveTotal) };
            })
            .OrderByDescending(h => h.Score)
            .ToList();

        if (hypotheses.Count == 0 || hypotheses.All(h => h.Score <= 0))
            hypotheses.Insert(0, new FailureHypothesis(FailureMode.Unclassified, 0, 0, []));

        var openQuestions = BuildOpenQuestions(signals, facts, graphics);
        return new EvaluationResult(hypotheses, openQuestions);
    }

    private static FailureHypothesis Score(FailureMode mode, List<EvidenceItem> evidence)
    {
        var strength = evidence.Where(e => e.Supports).Sum(e => e.Weight);
        var opposition = evidence.Where(e => !e.Supports).Sum(e => e.Weight);
        var score = strength - opposition;

        return new FailureHypothesis(mode, score, 0, evidence);
    }

    private sealed record Signals(
        IReadOnlyList<WindowsEventEvidence> KernelPower41,
        long? BugcheckCode,
        long? PowerButtonTimestamp,
        bool HasUnexpectedShutdown,
        bool HasBugcheck1001,
        IReadOnlyList<WindowsEventEvidence> WheaEvents,
        IReadOnlyList<WindowsEventEvidence> TdrEvents,
        IReadOnlyList<ExternalEvidenceFile> WerAppCrash,
        IReadOnlyList<ExternalEvidenceFile> WerBluescreen,
        IReadOnlyList<ExternalEvidenceFile> WerLiveKernelEvent,
        IReadOnlyList<ExternalEvidenceFile> LiveKernelDumps,
        IReadOnlyList<ExternalEvidenceFile> KernelDumps,
        IReadOnlyList<WindowsEventEvidence> ApplicationCrashEvents,
        SensorTransition? GpuLoadCollapse,
        IReadOnlyList<EtwDxgKrnlEvent> EtwTdrEvents,
        IReadOnlyList<EtwDxgKrnlEvent> EtwAdapterEvents,
        IReadOnlyList<EtwDxgKrnlEvent> EtwEngineResetEvents,
        IReadOnlyList<EtwDxgKrnlEvent> EtwPowerEvents,
        IReadOnlyList<EtwProviderEvent> EtwKernelPowerSuspend,
        IReadOnlyList<EtwProviderEvent> EtwKernelPowerResume,
        IReadOnlyList<EtwProviderEvent> EtwKernelPowerThermal,
        IReadOnlyList<EtwProviderEvent> EtwWheaAll,
        IReadOnlyList<DiskMetrics> FinalDiskMetrics,
        float? MaxDiskTimePercent,
        float? MaxReadLatencyMs,
        float? MaxWriteLatencyMs,
        float? MaxQueueDepth)
    {
        public bool HasSystemDeathEvidence => KernelPower41.Count > 0 || HasUnexpectedShutdown || KernelDumps.Count > 0;
        public bool HasStoragePressure => (MaxDiskTimePercent ?? 0) >= 95 || (MaxQueueDepth ?? 0) >= 8;
    }

    private static Signals ExtractSignals(
        IReadOnlyList<WindowsEventEvidence> events,
        IReadOnlyList<ExternalEvidenceFile> external,
        IReadOnlyList<SensorTransition> transitions,
        IReadOnlyList<EtwDxgKrnlEvent>? etwEvents,
        IReadOnlyList<EtwProviderEvent>? etwKernelPower,
        IReadOnlyList<EtwProviderEvent>? etwWhea,
        IReadOnlyList<StorageSample>? storageEvents)
    {
        var kernelPower41 = events
            .Where(e => e.Id == 41 && IsProvider(e, "Microsoft-Windows-Kernel-Power"))
            .ToList();

        var bugcheckCode = kernelPower41
            .Select(e => ParseNumeric(e.EventData?.GetValueOrDefault("BugcheckCode")))
            .FirstOrDefault(code => code.HasValue);
        var powerButton = kernelPower41
            .Select(e => ParseNumeric(e.EventData?.GetValueOrDefault("PowerButtonTimestamp")))
            .FirstOrDefault(timestamp => timestamp.HasValue);

        return new Signals(
            KernelPower41: kernelPower41,
            BugcheckCode: bugcheckCode,
            PowerButtonTimestamp: powerButton,
            HasUnexpectedShutdown: events.Any(e => e.Id == 6008),
            HasBugcheck1001: events.Any(e => e.Id == 1001 && IsProvider(e, "Microsoft-Windows-WER-SystemErrorReporting")),
            WheaEvents: events.Where(e => IsProvider(e, "Microsoft-Windows-WHEA-Logger")).ToList(),
            TdrEvents: events.Where(e => e.Id == 4101).ToList(),
            WerAppCrash: external.Where(f => f.Kind == "WER" && string.Equals(f.EventType, "AppCrash", StringComparison.OrdinalIgnoreCase)).ToList(),
            WerBluescreen: external.Where(f => f.Kind == "WER" && string.Equals(f.EventType, "Bluescreen", StringComparison.OrdinalIgnoreCase)).ToList(),
            WerLiveKernelEvent: external.Where(f => f.Kind == "WER" && string.Equals(f.EventType, "LiveKernelEvent", StringComparison.OrdinalIgnoreCase)).ToList(),
            LiveKernelDumps: external.Where(f => f.Kind == "LiveKernelReport").ToList(),
            KernelDumps: external.Where(f => f.Kind is "Minidump" or "MemoryDump").ToList(),
            ApplicationCrashEvents: events.Where(e =>
                (e.Id == 1000 && IsProvider(e, "Application Error")) ||
                (e.Id == 1001 && IsProvider(e, "Windows Error Reporting"))).ToList(),
            GpuLoadCollapse: transitions.FirstOrDefault(t =>
                string.Equals(t.SensorType, "Load", StringComparison.OrdinalIgnoreCase) &&
                t.FromValue >= 30 && t.ToValue <= 10),
            EtwTdrEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "TdrTimedOut" or "TdrPrepareRecovery" or "TdrRecoverFromTimeout").ToList(),
            EtwAdapterEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "AdapterStateChange" or "DeviceStateChange" or "AdapterReset").ToList(),
            EtwEngineResetEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "TdrEngineReset" or "TdrEngineResetPrepare" or "TdrResetFromTimeout").ToList(),
            EtwPowerEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "PowerComponentStateChange" or "PowerComponentState").ToList(),
            EtwKernelPowerSuspend: (etwKernelPower ?? []).Where(e =>
                e.EventName is "SuspendStart" or "SuspendEnd").ToList(),
            EtwKernelPowerResume: (etwKernelPower ?? []).Where(e =>
                e.EventName is "ResumeStart" or "ResumeEnd").ToList(),
            EtwKernelPowerThermal: (etwKernelPower ?? []).Where(e =>
                e.EventName.Contains("Thermal", StringComparison.OrdinalIgnoreCase)).ToList(),
            EtwWheaAll: (etwWhea ?? []).ToList());

        var storage = (storageEvents ?? []);
        var finalDiskMetrics = storage.Count > 0 ? storage[^1].Disks : new List<DiskMetrics>();
        float? maxDiskTime = null, maxReadLat = null, maxWriteLat = null, maxQueue = null;
        foreach (var sample in storage)
        {
            foreach (var disk in sample.Disks)
            {
                maxDiskTime = Max(maxDiskTime, disk.DiskTimePercent);
                maxReadLat = Max(maxReadLat, disk.AvgReadLatencyMs);
                maxWriteLat = Max(maxWriteLat, disk.AvgWriteLatencyMs);
                maxQueue = Max(maxQueue, disk.QueueDepth);
            }
        }

        return new Signals(
            KernelPower41: kernelPower41,
            BugcheckCode: bugcheckCode,
            PowerButtonTimestamp: powerButton,
            HasUnexpectedShutdown: events.Any(e => e.Id == 6008),
            HasBugcheck1001: events.Any(e => e.Id == 1001 && IsProvider(e, "Microsoft-Windows-WER-SystemErrorReporting")),
            WheaEvents: events.Where(e => IsProvider(e, "Microsoft-Windows-WHEA-Logger")).ToList(),
            TdrEvents: events.Where(e => e.Id == 4101).ToList(),
            WerAppCrash: external.Where(f => f.Kind == "WER" && string.Equals(f.EventType, "AppCrash", StringComparison.OrdinalIgnoreCase)).ToList(),
            WerBluescreen: external.Where(f => f.Kind == "WER" && string.Equals(f.EventType, "Bluescreen", StringComparison.OrdinalIgnoreCase)).ToList(),
            WerLiveKernelEvent: external.Where(f => f.Kind == "WER" && string.Equals(f.EventType, "LiveKernelEvent", StringComparison.OrdinalIgnoreCase)).ToList(),
            LiveKernelDumps: external.Where(f => f.Kind == "LiveKernelReport").ToList(),
            KernelDumps: external.Where(f => f.Kind is "Minidump" or "MemoryDump").ToList(),
            ApplicationCrashEvents: events.Where(e =>
                (e.Id == 1000 && IsProvider(e, "Application Error")) ||
                (e.Id == 1001 && IsProvider(e, "Windows Error Reporting"))).ToList(),
            GpuLoadCollapse: transitions.FirstOrDefault(t =>
                string.Equals(t.SensorType, "Load", StringComparison.OrdinalIgnoreCase) &&
                t.FromValue >= 30 && t.ToValue <= 10),
            EtwTdrEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "TdrTimedOut" or "TdrPrepareRecovery" or "TdrRecoverFromTimeout").ToList(),
            EtwAdapterEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "AdapterStateChange" or "DeviceStateChange" or "AdapterReset").ToList(),
            EtwEngineResetEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "TdrEngineReset" or "TdrEngineResetPrepare" or "TdrResetFromTimeout").ToList(),
            EtwPowerEvents: (etwEvents ?? []).Where(e =>
                e.EventName is "PowerComponentStateChange" or "PowerComponentState").ToList(),
            EtwKernelPowerSuspend: (etwKernelPower ?? []).Where(e =>
                e.EventName is "SuspendStart" or "SuspendEnd").ToList(),
            EtwKernelPowerResume: (etwKernelPower ?? []).Where(e =>
                e.EventName is "ResumeStart" or "ResumeEnd").ToList(),
            EtwKernelPowerThermal: (etwKernelPower ?? []).Where(e =>
                e.EventName.Contains("Thermal", StringComparison.OrdinalIgnoreCase)).ToList(),
            EtwWheaAll: (etwWhea ?? []).ToList(),
            FinalDiskMetrics: finalDiskMetrics,
            MaxDiskTimePercent: maxDiskTime,
            MaxReadLatencyMs: maxReadLat,
            MaxWriteLatencyMs: maxWriteLat,
            MaxQueueDepth: maxQueue);
    }

    private static List<EvidenceItem> EvaluateHardFreeze(Signals signals, SessionFacts facts, GraphicsContext? graphics)
    {
        var evidence = new List<EvidenceItem>();

        if (signals.KernelPower41.Count > 0)
        {
            if (signals.BugcheckCode == 0)
                Add(evidence, true, 2, $"Kernel-Power 41 with BugcheckCode 0 at {Times(signals.KernelPower41)} �?system stopped without a bugcheck");
            else if (signals.BugcheckCode > 0)
                Add(evidence, false, 2, $"Kernel-Power 41 recorded BugcheckCode {signals.BugcheckCode} �?points to a blue screen, not a silent freeze");
            else
                Add(evidence, true, 1, $"Kernel-Power 41 present at {Times(signals.KernelPower41)} (bugcheck code unavailable)");
        }

        if (signals.HasUnexpectedShutdown)
            Add(evidence, true, 1, "Event 6008 �?Windows recorded an unexpected shutdown");

        if (signals.PowerButtonTimestamp > 0)
            Add(evidence, true, 1, "Kernel-Power 41 PowerButtonTimestamp is set �?the power button was pressed or held, consistent with forcing off a frozen machine");

        if (signals.HasSystemDeathEvidence && facts.LastTelemetrySampleAt.HasValue && facts.TelemetryLagSeconds <= 15)
            Add(evidence, true, 1, $"Telemetry was still being written {facts.TelemetryLagSeconds:0.#}s before the final heartbeat �?instantaneous termination, not a graceful stop");

        if (facts.FinalIdleSeconds >= 300 && facts.FinalGpuLoadPercent >= 20)
            Add(evidence, true, 1, $"User idle for {FormatDuration(facts.FinalIdleSeconds)} with GPU load still at {facts.FinalGpuLoadPercent:0.#}% �?unattended 3D workload right before the end");

        if (signals.GpuLoadCollapse is not null)
            Add(evidence, true, 1, $"GPU load collapsed from {signals.GpuLoadCollapse.FromValue:0.#}% to {signals.GpuLoadCollapse.ToValue:0.#}% at {Time(signals.GpuLoadCollapse.Timestamp)} ?graphics stack was leaving a 3D workload (the motivating scenario)");

        // ETW kernel-level evidence
        if (signals.EtwTdrEvents.Count > 0)
        {
            var timedOut = signals.EtwTdrEvents.Where(e => e.EventName == "TdrTimedOut").ToList();
            var recovered = signals.EtwTdrEvents.Where(e => e.EventName == "TdrRecoverFromTimeout").ToList();

            if (timedOut.Count > 0 && recovered.Count == 0)
                Add(evidence, true, 3, $"Kernel ETW: {timedOut.Count} TdrTimedOut event(s) at {EtwTimes(timedOut)} ?GPU timeout detected at kernel level, but no recovery follow-up recorded before the machine died");
            else if (timedOut.Count > 0 && recovered.Count > 0)
                Add(evidence, false, 2, $"Kernel ETW: TDR timeout WITH recovery at {EtwTimes(recovered)} ?the GPU stack recovered at kernel level, so the freeze was not a simple TDR-only event");
        }

        if (signals.EtwAdapterEvents.Count > 0)
            Add(evidence, true, 1, $"Kernel ETW: {signals.EtwAdapterEvents.Count} GPU adapter state change(s) at {EtwTimes(signals.EtwAdapterEvents)} ?the graphics adapter was changing state near the end");

        if (signals.EtwKernelPowerSuspend.Count > 0)
            Add(evidence, true, 2, $"Real-time ETW: {signals.EtwKernelPowerSuspend.Count} suspend event(s) at {EtwProviderTimes(signals.EtwKernelPowerSuspend)} ?the system was entering/exiting sleep near the end");

        if (signals.EtwKernelPowerThermal.Count > 0)
            Add(evidence, true, 2, $"Real-time ETW: {signals.EtwKernelPowerThermal.Count} thermal event(s) at {EtwProviderTimes(signals.EtwKernelPowerThermal)} ?thermal throttling or over-temp may be contributing");

        if (signals.EtwWheaAll.Count > 0)
            Add(evidence, true, 3, $"Real-time ETW: {signals.EtwWheaAll.Count} WHEA hardware error event(s) at {EtwProviderTimes(signals.EtwWheaAll)} ?kernel-level hardware errors detected in real-time");

        AddGraphicsStackEvidence(evidence, signals, facts, graphics);

        // Storage / IO evidence
        AddStorageEvidence(evidence, signals, facts);

        if (signals.TdrEvents.Count > 0)
            Add(evidence, false, 1, "Display 4101 (TDR) events present �?a driver timeout with recovery normally leaves the system alive");

        if (!signals.HasSystemDeathEvidence)
            Add(evidence, false, 2, "No unexpected-shutdown evidence (Kernel-Power 41 / 6008 / kernel dumps) �?the agent itself may have been terminated while the system kept running");

        return evidence;
    }

    private static void AddGraphicsStackEvidence(
        List<EvidenceItem> evidence,
        Signals signals,
        SessionFacts facts,
        GraphicsContext? graphics)
    {
        if (graphics is null)
            return;

        var remoteApps = graphics.GraphicsApps
            .Where(a => a.Category == GraphicsAppCategory.RemoteControl && a.RunningAtEnd)
            .Select(a => a.ProcessName)
            .ToList();
        if (remoteApps.Count > 0)
            Add(evidence, true, 1, $"Remote-control software was still running at the end ({string.Join(", ", remoteApps)}) �?remote sessions keep the graphics stack active while the user is away");

        var recentDisplayChanges = graphics.DisplayChanges
            .Where(c => !string.Equals(c.Change, "baseline", StringComparison.OrdinalIgnoreCase)
                && facts.LastHeartbeatAt - c.Timestamp < RecentWindow)
            .ToList();
        foreach (var change in recentDisplayChanges)
        {
            Add(evidence, true, 2, $"{change.Description} at {Time(change.Timestamp)} �?display reconfiguration shortly before the end");
            if (change.After.Any(d => d.IsVirtual) || change.Before.Any(d => d.IsVirtual))
                Add(evidence, true, 1, "The display change involves a virtual display driver �?a known trigger for graphics-stack instability");
        }

        var schemeChanges = graphics.PowerSchemeChanges
            .Where(c => string.Equals(c.Change, "changed", StringComparison.OrdinalIgnoreCase)
                && facts.LastHeartbeatAt - c.Timestamp < RecentWindow)
            .ToList();
        foreach (var change in schemeChanges)
            Add(evidence, true, 1, $"{change.Description} at {Time(change.Timestamp)} �?power-state transitions can trigger driver P-state changes");

        var remoteSessionChanges = graphics.RemoteSessionChanges
            .Where(c => c.IsRemote && facts.LastHeartbeatAt - c.Timestamp < RecentWindow)
            .ToList();
        foreach (var change in remoteSessionChanges)
            Add(evidence, true, 1, $"Session entered remote mode at {Time(change.Timestamp)} ?remote logon/takeover was active near the end");
    }

    private static void AddStorageEvidence(
        List<EvidenceItem> evidence,
        Signals signals,
        SessionFacts facts)
    {
        foreach (var disk in signals.FinalDiskMetrics)
        {
            var label = disk.Model ?? disk.Name;

            if (disk.TemperatureCelsius >= 70)
                Add(evidence, true, 2, $"{label} temperature was {disk.TemperatureCelsius:0}°C ?NVMe/SSD overheating may cause controller throttling or reset");
            else if (disk.TemperatureCelsius >= 55)
                Add(evidence, true, 1, $"{label} temperature was {disk.TemperatureCelsius:0}°C ?elevated storage temperature");

            if (!string.IsNullOrEmpty(disk.HealthStatus) && disk.HealthStatus != "100%")
                Add(evidence, true, 2, $"{label} health is {disk.HealthStatus} ?SMART indicates drive degradation");
        }

        if (signals.HasStoragePressure)
        {
            var parts = new List<string>();
            if ((signals.MaxDiskTimePercent ?? 0) >= 95)
                parts.Add($"disk time {signals.MaxDiskTimePercent:0}%");
            if ((signals.MaxQueueDepth ?? 0) >= 8)
                parts.Add($"queue depth {signals.MaxQueueDepth:0}");

            Add(evidence, true, 2, $"Storage was under heavy pressure ({string.Join(", ", parts)}) ?disk I/O saturation may cause system unresponsiveness");
        }

        if ((signals.MaxReadLatencyMs ?? 0) >= 100)
            Add(evidence, true, 2, $"Max disk read latency was {signals.MaxReadLatencyMs:0}ms ?abnormally high read latency (healthy NVMe < 10ms)");
        if ((signals.MaxWriteLatencyMs ?? 0) >= 100)
            Add(evidence, true, 2, $"Max disk write latency was {signals.MaxWriteLatencyMs:0}ms ?abnormally high write latency");

        if (facts.TelemetryLagSeconds > 30 && signals.HasStoragePressure)
            Add(evidence, true, 1, $"Telemetry lag ({facts.TelemetryLagSeconds:0.#}s) combined with storage pressure ?the recorder itself may have been blocked by disk I/O saturation");
    }

    private static float? Max(float? a, float? b)
    {
        if (!a.HasValue) return b;
        if (!b.HasValue) return a;
        return a.Value > b.Value ? a : b;
    }

    private static List<EvidenceItem> EvaluateBlueScreen(Signals signals)
    {
        var evidence = new List<EvidenceItem>();

        if (signals.KernelPower41.Count > 0)
        {
            if (signals.BugcheckCode > 0)
                Add(evidence, true, 2, $"Kernel-Power 41 with BugcheckCode {signals.BugcheckCode} at {Times(signals.KernelPower41)}");
            else if (signals.BugcheckCode == 0)
                Add(evidence, false, 2, "Kernel-Power 41 recorded BugcheckCode 0 �?no blue screen occurred");
        }

        if (signals.HasBugcheck1001)
            Add(evidence, true, 2, "BugCheck report (WER-SystemErrorReporting 1001) �?a bugcheck was recorded");

        if (signals.KernelDumps.Count > 0)
            Add(evidence, true, 1, $"Kernel memory dumps written: {FileNames(signals.KernelDumps)}");

        if (signals.WerBluescreen.Count > 0)
            Add(evidence, true, 1, $"WER bluescreen reports: {Details(signals.WerBluescreen)}");

        if (!signals.HasSystemDeathEvidence)
            Add(evidence, false, 2, "No unexpected-shutdown evidence �?the system kept running");

        return evidence;
    }

    private static List<EvidenceItem> EvaluateDriverTimeout(Signals signals, GraphicsContext? graphics)
    {
        var evidence = new List<EvidenceItem>();

        if (signals.TdrEvents.Count > 0)
            Add(evidence, true, 2, $"Display 4101 (display driver timeout and recovery) at {Times(signals.TdrEvents)}");

        if (signals.EtwTdrEvents.Count > 0)
        {
            var recovered = signals.EtwTdrEvents.Where(e => e.EventName == "TdrRecoverFromTimeout").ToList();
            if (recovered.Count > 0)
                Add(evidence, true, 3, $"Kernel ETW: TDR recovery confirmed at kernel level at {EtwTimes(recovered)} ?the GPU driver stack was reset and recovered");
        }

        if (signals.EtwEngineResetEvents.Count > 0)
            Add(evidence, true, 2, $"Kernel ETW: {signals.EtwEngineResetEvents.Count} GPU engine reset event(s) at {EtwTimes(signals.EtwEngineResetEvents)} ?one or more GPU engines were reset by the driver");

        if (signals.WerLiveKernelEvent.Count > 0)
            Add(evidence, true, 2, $"WER LiveKernelEvent reports: {Details(signals.WerLiveKernelEvent)}");

        if (signals.LiveKernelDumps.Count > 0)
            Add(evidence, true, 1, $"LiveKernelReports dumps: {Details(signals.LiveKernelDumps)}");

        if (graphics?.LatestDisplayChange?.After.Any(d => d.IsVirtual) == true)
            Add(evidence, true, 1, "A virtual display driver was active in the final display topology �?display-driver helper stacks are common TDR contributors");

        if (signals.HasSystemDeathEvidence)
            Add(evidence, false, 1, "The session ended uncleanly �?the machine ultimately died or was forced off, so this was more than a recoverable timeout");

        return evidence;
    }

    private static List<EvidenceItem> EvaluateHardwareError(Signals signals)
    {
        var evidence = new List<EvidenceItem>();

        if (signals.WheaEvents.Count > 0)
        {
            Add(evidence, true, 2, $"WHEA-Logger events at {Times(signals.WheaEvents)} (ids: {Ids(signals.WheaEvents)})");
            if (signals.WheaEvents.Any(e => e.Id is 19 or 20 or 46 or 47))
                Add(evidence, true, 1, "WHEA contains fatal/uncorrected entries (19/20/46/47)");
        }

        if (signals.EtwWheaAll.Count > 0)
            Add(evidence, true, 3, $"Real-time ETW WHEA: {signals.EtwWheaAll.Count} event(s) at {EtwProviderTimes(signals.EtwWheaAll)} ?hardware errors captured at kernel level in real-time, sub-millisecond precision");

        return evidence;
    }

    private static List<EvidenceItem> EvaluateAppCrash(Signals signals)
    {
        var evidence = new List<EvidenceItem>();

        if (signals.WerAppCrash.Count > 0)
            Add(evidence, true, 1, $"WER AppCrash reports in window: {Details(signals.WerAppCrash)}");

        if (signals.ApplicationCrashEvents.Count > 0)
            Add(evidence, true, 1, $"Application log crash events at {Times(signals.ApplicationCrashEvents)}");

        if (signals.HasSystemDeathEvidence)
            Add(evidence, false, 2, "System-level evidence (Kernel-Power 41 / 6008) �?this was more than an application crash");

        return evidence;
    }

    private static List<EvidenceItem> EvaluatePowerLoss(Signals signals)
    {
        var evidence = new List<EvidenceItem>();

        if (signals.KernelPower41.Count > 0 && signals.BugcheckCode == 0)
            Add(evidence, true, 1, "Kernel-Power 41 with BugcheckCode 0 �?instantaneous loss without a bugcheck");

        if (signals.KernelPower41.Count > 0 && signals.BugcheckCode == 0 &&
            signals.WheaEvents.Count == 0 && signals.TdrEvents.Count == 0 && signals.KernelDumps.Count == 0)
            Add(evidence, true, 1, "No WHEA / TDR / kernel dump breadcrumbs �?consistent with an abrupt power cut");

        if (signals.PowerButtonTimestamp > 0)
            Add(evidence, false, 1, "Power button was pressed or held ?a deliberate force-off after a freeze, not an abrupt power loss");

        if (signals.EtwKernelPowerSuspend.Count > 0)
            Add(evidence, false, 2, $"Real-time ETW: {signals.EtwKernelPowerSuspend.Count} suspend event(s) at {EtwProviderTimes(signals.EtwKernelPowerSuspend)} ?the system entered sleep, so this was not a true power loss");

        if (!signals.HasSystemDeathEvidence)
            Add(evidence, false, 2, "No unexpected-shutdown evidence �?the system kept running");

        return evidence;
    }

    private static List<string> BuildOpenQuestions(Signals signals, SessionFacts facts, GraphicsContext? graphics)
    {
        var questions = new List<string>();

        if (!signals.HasSystemDeathEvidence)
            questions.Add("No unexpected-shutdown evidence was found; the agent process itself may have been terminated while the system kept running (e.g. taskkill or logoff).");

        if (!facts.HasGpuSensors)
            questions.Add("No GPU sensors were captured in the previous session; run the agent elevated for full LibreHardwareMonitor coverage.");

        if (facts.LastTelemetrySampleAt is null)
            questions.Add("The telemetry tail is empty �?no hardware context survived for the final minutes.");
        else if (facts.TelemetryLagSeconds > 60)
            questions.Add($"The last telemetry sample is {facts.TelemetryLagSeconds:0.#}s before the final heartbeat �?the recorder or disk I/O stalled before the end.");

        if (signals.TdrEvents.Count > 0)
            questions.Add("Display 4101 (TDR) events occurred; correlate their timestamps with the timeline to see whether the machine recovered and froze later.");

        if (signals.BugcheckCode == 0 && signals.KernelDumps.Count == 0 && signals.HasSystemDeathEvidence)
            questions.Add("No kernel dump was written �?typical for a hard freeze (no bugcheck); verify crash dump settings are enabled for future blue screens.");

        if (graphics is not null)
        {
            if (graphics.FinalGpuEngines is null)
                questions.Add("No per-process GPU engine usage was recorded �?GPU Engine performance counters were unavailable in the previous session.");

            var remoteApps = graphics.GraphicsApps
                .Where(a => a.Category == GraphicsAppCategory.RemoteControl && a.RunningAtEnd)
                .Select(a => a.ProcessName)
                .ToList();
            if (remoteApps.Count > 0)
                questions.Add($"Remote-control software ({string.Join(", ", remoteApps)}) was running at the end; correlate remote session activity with the freeze time to rule out remote-control-induced display transitions.");

            if (graphics.DriverInventory.Count == 0)
                questions.Add("No display driver inventory was recorded for the previous session.");
        }

        if (signals.FinalDiskMetrics.Count == 0)
            questions.Add("No storage/disk metrics were recorded; storage sensors or performance counters may be unavailable.");
        else if (signals.HasStoragePressure)
            questions.Add("Storage was under heavy pressure near the end; disk I/O saturation may be a contributing factor to the freeze.");
        if ((signals.MaxReadLatencyMs ?? 0) >= 100 || (signals.MaxWriteLatencyMs ?? 0) >= 100)
            questions.Add("Abnormally high disk latency was detected; check for failing storage hardware or background I/O-heavy processes.");

        return questions;
    }

    private static void Add(List<EvidenceItem> evidence, bool supports, int weight, string description)
        => evidence.Add(new EvidenceItem(supports, weight, description));

    private static bool IsProvider(WindowsEventEvidence evidence, string provider)
        => string.Equals(evidence.Provider, provider, StringComparison.OrdinalIgnoreCase);

    private static long? ParseNumeric(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Times(IReadOnlyList<WindowsEventEvidence> events)
        => string.Join(", ", events.Where(e => e.TimeCreated.HasValue).Select(e => Time(e.TimeCreated!.Value)).Distinct().Take(3));

    private static string EtwTimes(IReadOnlyList<EtwDxgKrnlEvent> events)
        => string.Join(", ", events.Select(e => Time(e.Timestamp)).Distinct().Take(3));

    private static string EtwProviderTimes(IReadOnlyList<EtwProviderEvent> events)
        => string.Join(", ", events.Select(e => Time(e.Timestamp)).Distinct().Take(3));

    private static string Time(DateTimeOffset timestamp)
        => timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Time(ExternalEvidenceFile file)
        => Time(file.Timestamp);

    private static string Ids(IReadOnlyList<WindowsEventEvidence> events)
        => string.Join(", ", events.Select(e => e.Id).Distinct().Order());

    private static string Details(IReadOnlyList<ExternalEvidenceFile> files)
        => string.Join(", ", files.Select(f => $"{Detail(f)} at {Time(f)}").Take(3));

    private static string Detail(ExternalEvidenceFile file)
        => file.Detail ?? Path.GetFileName(file.Path.TrimEnd('\\', '/'));

    private static string FileNames(IReadOnlyList<ExternalEvidenceFile> files)
        => string.Join(", ", files.Select(f => Path.GetFileName(f.Path.TrimEnd('\\', '/'))).Take(3));

    private static string FormatDuration(long seconds)
        => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}