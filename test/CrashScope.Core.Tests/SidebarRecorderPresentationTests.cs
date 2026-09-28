using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class SidebarRecorderPresentationTests
{
    [Theory]
    [InlineData(LiveDataState.Stopped, "stopped", "stoppedHint", SidebarRecorderAction.Start, false, false)]
    [InlineData(LiveDataState.Starting, "starting", "startingHint", SidebarRecorderAction.Busy, true, false)]
    [InlineData(LiveDataState.Stopping, "stopping", "stoppingHint", SidebarRecorderAction.Busy, true, false)]
    [InlineData(LiveDataState.Faulted, "faulted", "faultedHint", SidebarRecorderAction.Retry, true, true)]
    [InlineData(LiveDataState.Waiting, "waiting", "waitingHint", SidebarRecorderAction.Stop, true, false)]
    [InlineData(LiveDataState.Stale, "stale", "staleHint", SidebarRecorderAction.Stop, true, false)]
    [InlineData(LiveDataState.Fresh, "recording", "recordingHint", SidebarRecorderAction.Stop, false, false)]
    public void MapsDataTrustAndPrimaryAction(
        LiveDataState state, string stateKey, string hintKey, SidebarRecorderAction action,
        bool warning, bool faulted)
    {
        var view = SidebarRecorderPresentation.From(state);
        Assert.Equal(stateKey, view.StateKey);
        Assert.Equal(hintKey, view.HintKey);
        Assert.Equal(action, view.Action);
        Assert.Equal(warning, view.IsWarning);
        Assert.Equal(faulted, view.IsFaulted);
    }

    [Fact]
    public void FreshRecordingOffersStopButNotWarning()
    {
        var view = SidebarRecorderPresentation.From(LiveDataState.Fresh);
        Assert.Equal(SidebarRecorderAction.Stop, view.Action);
        Assert.False(view.IsWarning);
        Assert.False(view.IsFaulted);
    }

    [Fact]
    public void FaultedRecordingOffersRetryAndKeepsFaultVisible()
    {
        var view = SidebarRecorderPresentation.From(LiveDataState.Faulted);
        Assert.Equal(SidebarRecorderAction.Retry, view.Action);
        Assert.True(view.IsWarning);
        Assert.True(view.IsFaulted);
    }
}
