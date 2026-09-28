using System.Diagnostics;
using CrashScope.Desktop;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public class LiveSamplePresentationTests
{
    [Theory]
    [InlineData(RecorderState.Stopped, LiveDataState.Stopped)]
    [InlineData(RecorderState.Starting, LiveDataState.Starting)]
    [InlineData(RecorderState.Stopping, LiveDataState.Stopping)]
    [InlineData(RecorderState.Faulted, LiveDataState.Faulted)]
    public void LifecycleOverridesOldSamples(RecorderState recorder, LiveDataState expected) =>
        Assert.Equal(expected, LiveSamplePresentation.Evaluate(recorder, true, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NoTimestampIsWaiting(bool current) => Assert.Equal(LiveDataState.Waiting,
        LiveSamplePresentation.Evaluate(RecorderState.Recording, current, null, TimeSpan.FromSeconds(1)));
    [Theory] [InlineData(0, LiveDataState.Fresh)] [InlineData(4.99, LiveDataState.Fresh)] [InlineData(5, LiveDataState.Stale)]
    [InlineData(99, LiveDataState.Stale)] [InlineData(-1, LiveDataState.Stale)]
    public void FreshnessHasExplicitBoundary(double seconds, LiveDataState expected) => Assert.Equal(expected,
        LiveSamplePresentation.Evaluate(RecorderState.Recording, true, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1)));
    [Theory] [InlineData(0, 5)] [InlineData(1, 5)] [InlineData(2, 6)] [InlineData(10, 30)]
    public void ThresholdFollowsActualCadence(int seconds, int expected) => Assert.Equal(TimeSpan.FromSeconds(expected), LiveSamplePresentation.StaleAfter(TimeSpan.FromSeconds(seconds)));
    [Fact] public void OtherSessionDoesNotReuseFreshData() => Assert.Equal(LiveDataState.Waiting,
        LiveSamplePresentation.Evaluate(RecorderState.Recording, false, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
    [Theory] [InlineData(null, false)] [InlineData("", false)] [InlineData("old", false)] [InlineData("current", true)]
    public void SnapshotMustBelongToSession(string? id, bool expected)
    {
        var snapshot = new LiveSampleSnapshot("current", Array.Empty<SensorCatalogEntry>(), new float?[] { 80 }, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), TimeSpan.Zero);
        Assert.Equal(expected, LiveSamplePresentation.BelongsToSession(snapshot, id));
    }
    [Fact] public void ReceiptDoesNotResetOldCaptureAge()
    {
        var snapshot = new LiveSampleSnapshot("current", Array.Empty<SensorCatalogEntry>(), new float?[] { 80 }, DateTimeOffset.UtcNow.AddSeconds(-10), Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(10));
        Assert.True(snapshot.Age >= TimeSpan.FromSeconds(10));
    }
    [Fact] public void FutureCaptureStaysUncertainUntilReplaced()
    {
        var snapshot = new LiveSampleSnapshot("current", Array.Empty<SensorCatalogEntry>(), new float?[] { 80 }, DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp() - Stopwatch.Frequency * 10, TimeSpan.FromSeconds(-1));
        Assert.True(snapshot.Age < TimeSpan.Zero);
    }
    [Fact] public void NullSnapshotNeverMatches() => Assert.False(LiveSamplePresentation.BelongsToSession(null, "current"));
}
