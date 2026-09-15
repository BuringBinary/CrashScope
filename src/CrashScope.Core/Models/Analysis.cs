namespace CrashScope.Core.Models;

public enum FailureMode
{
    HardFreeze,
    BlueScreen,
    DriverTimeoutRecovery,
    HardwareError,
    AppCrash,
    PowerLoss,
    Unclassified
}

public sealed record ExternalEvidenceFile(
    string Kind,
    string Path,
    DateTimeOffset Timestamp,
    long SizeBytes,
    string? EventType,
    string? Detail);

public sealed record SensorTransition(
    DateTimeOffset Timestamp,
    string HardwareName,
    string SensorName,
    string SensorType,
    float FromValue,
    float ToValue,
    double ElapsedSeconds,
    bool IsTrend,
    string Description);

public sealed record TimelineEntry(
    DateTimeOffset Timestamp,
    string Category,
    string Description);

public sealed record EvidenceItem(bool Supports, int Weight, string Description);

public sealed record FailureHypothesis(
    FailureMode Mode,
    int Score,
    double Confidence,
    IReadOnlyList<EvidenceItem> Evidence);

public enum GraphicsAppCategory
{
    GameLauncher,
    GameOverlay,
    RemoteControl,
    HardwareAcceleratedApp,
    CaptureTool
}

public sealed record GraphicsAppPresence(
    string ProcessName,
    GraphicsAppCategory Category,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    bool RunningAtEnd);

public sealed record GraphicsContext(
    IReadOnlyList<GraphicsAppPresence> GraphicsApps,
    IReadOnlyList<DisplayTopologyEvent> DisplayChanges,
    IReadOnlyList<PowerSchemeEvent> PowerSchemeChanges,
    IReadOnlyList<RemoteSessionEvent> RemoteSessionChanges,
    IReadOnlyList<DriverInventoryEntry> DriverInventory,
    IReadOnlyList<WindowsEventEvidence> DriverChangeEvents,
    IReadOnlyList<WindowsEventEvidence> RemoteLogonEvents,
    GpuEngineSample? FinalGpuEngines)
{
    public static readonly GraphicsContext Empty = new(
        [], [], [], [], [], [], [], null);

    public DisplayTopologyEvent? LatestDisplayChange =>
        DisplayChanges.Count > 0 ? DisplayChanges[^1] : null;
}

public sealed record IncidentAnalysis(
    SessionState PreviousSession,
    DateTimeOffset? LastTelemetrySampleAt,
    double TelemetryLagSeconds,
    long FinalIdleSeconds,
    float? FinalGpuLoadPercent,
    IReadOnlyList<SensorTransition> GpuTransitions,
    IReadOnlyList<TimelineEntry> Timeline,
    IReadOnlyList<FailureHypothesis> Hypotheses,
    IReadOnlyList<string> OpenQuestions,
    GraphicsContext Graphics);
