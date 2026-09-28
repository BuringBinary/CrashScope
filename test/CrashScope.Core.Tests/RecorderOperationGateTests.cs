using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public class RecorderOperationGateTests
{
    [Fact] public async Task DuplicateRequestDoesNotRunTwice()
    {
        var gate = new RecorderOperationGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = gate.RunAsync(true, () => { calls++; return release.Task; });
        Assert.Equal(1, gate.Pending);
        Assert.False(await gate.RunAsync(false, () => { calls++; return Task.CompletedTask; }));
        release.SetResult(); Assert.True(await first); Assert.Equal(1, calls); Assert.Equal(0, gate.Pending);
    }
    [Theory] [InlineData(true, 1)] [InlineData(false, 2)]
    public async Task AnnouncesBusyBeforeWork(bool start, int state)
    {
        var gate = new RecorderOperationGate(); var changes = new List<int>();
        gate.Changed += () => changes.Add(gate.Pending);
        await gate.RunAsync(start, () => { Assert.Equal(state, gate.Pending); return Task.CompletedTask; });
        Assert.Equal(new[] { state, 0 }, changes);
    }
    [Fact] public async Task FailureReleasesGateAndRetryClearsError()
    {
        var gate = new RecorderOperationGate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(true, () => throw new InvalidOperationException("fixture failure")));
        Assert.Equal(0, gate.Pending); Assert.Equal("fixture failure", gate.LastError);
        Assert.True(await gate.RunAsync(true, () => Task.CompletedTask)); Assert.Null(gate.LastError);
    }
}
