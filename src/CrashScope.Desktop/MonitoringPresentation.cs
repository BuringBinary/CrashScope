using CrashScope.Core.Monitoring;

namespace CrashScope.Desktop;

/// <summary>Presentation only: never start collectors to fill an empty UI.</summary>
public static class MonitoringPresentation
{
    public static string SampleKey(RecorderState state, bool received, bool usable) => state switch
    {
        RecorderState.Starting => "monitoring.starting",
        RecorderState.Stopping => "monitoring.stopping",
        RecorderState.Faulted => "monitoring.faulted",
        RecorderState.Recording => !received ? "monitoring.samplesWaiting"
            : usable ? "monitoring.samplesLive" : "monitoring.samplesUnavailable",
        _ => "monitoring.samplesStopped"
    };

    public static string EventStatusKey(RecorderState state) => state switch
    {
        RecorderState.Starting => "monitoring.starting",
        RecorderState.Stopping => "monitoring.stopping",
        RecorderState.Faulted => "monitoring.faulted",
        RecorderState.Recording => "monitoring.eventsLive",
        _ => "monitoring.eventsStopped"
    };

    public static string EventEmptyKey(RecorderState state, int bufferedCount, bool anyFilter) =>
        !anyFilter || bufferedCount > 0 ? "monitoring.eventsFiltered"
        : state == RecorderState.Recording ? "monitoring.eventsWaiting" : EventStatusKey(state);

    public static string TrendKey(string direction) => direction switch
    {
        "Improving" => "dashboard.health.improving",
        "Worsening" => "dashboard.health.worsening",
        "Stable" => "dashboard.health.stable",
        _ => "common.unknown"
    };
}
