namespace CrashScope.Desktop;

public enum SidebarRecorderAction
{
    Start,
    Stop,
    Retry,
    Busy
}

public sealed record SidebarRecorderPresentation(
    string StateKey,
    string HintKey,
    SidebarRecorderAction Action,
    bool IsWarning,
    bool IsFaulted)
{
    public static SidebarRecorderPresentation From(LiveDataState state) => state switch
    {
        LiveDataState.Stopped => new("stopped", "stoppedHint", SidebarRecorderAction.Start, false, false),
        LiveDataState.Starting => new("starting", "startingHint", SidebarRecorderAction.Busy, true, false),
        LiveDataState.Stopping => new("stopping", "stoppingHint", SidebarRecorderAction.Busy, true, false),
        LiveDataState.Faulted => new("faulted", "faultedHint", SidebarRecorderAction.Retry, true, true),
        LiveDataState.Waiting => new("waiting", "waitingHint", SidebarRecorderAction.Stop, true, false),
        LiveDataState.Stale => new("stale", "staleHint", SidebarRecorderAction.Stop, true, false),
        LiveDataState.Fresh => new("recording", "recordingHint", SidebarRecorderAction.Stop, false, false),
        _ => new("stopped", "stoppedHint", SidebarRecorderAction.Start, false, false)
    };
}
