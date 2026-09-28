using System.Diagnostics;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;

namespace CrashScope.Desktop;

public sealed record LiveSampleSnapshot(string? SessionId, IReadOnlyList<SensorCatalogEntry> Catalog,
    IReadOnlyList<float?> Values, DateTimeOffset SampleAt, long ReceivedStamp, TimeSpan AgeOnReceipt)
{
    // An impossible future capture must not become "fresh" merely by waiting.
    public TimeSpan Age => AgeOnReceipt < TimeSpan.Zero ? AgeOnReceipt : AgeOnReceipt + Stopwatch.GetElapsedTime(ReceivedStamp);
}

public enum LiveDataState { Stopped, Starting, Stopping, Faulted, Waiting, Fresh, Stale }

public static class LiveSamplePresentation
{
    // UI freshness tolerance, not a collector timeout: >= five seconds or three scheduled ticks.
    public static TimeSpan StaleAfter(TimeSpan sampleInterval) =>
        TimeSpan.FromSeconds(Math.Max(5, Math.Max(0, sampleInterval.TotalSeconds) * 3));

    public static LiveDataState Evaluate(RecorderState state, bool currentSessionSample, TimeSpan? age, TimeSpan interval) => state switch
    {
        RecorderState.Stopped => LiveDataState.Stopped,
        RecorderState.Starting => LiveDataState.Starting,
        RecorderState.Stopping => LiveDataState.Stopping,
        RecorderState.Faulted => LiveDataState.Faulted,
        _ when !currentSessionSample || age is null => LiveDataState.Waiting,
        // Negative capture age is clock uncertainty, not proof of a fresh sample.
        _ when age.Value < TimeSpan.Zero || age.Value >= StaleAfter(interval) => LiveDataState.Stale,
        _ => LiveDataState.Fresh
    };

    public static bool BelongsToSession(LiveSampleSnapshot? snapshot, string? sessionId) =>
        snapshot is not null && !string.IsNullOrEmpty(sessionId) && snapshot.SessionId == sessionId;
}
