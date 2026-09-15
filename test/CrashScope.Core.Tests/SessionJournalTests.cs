using CrashScope.Core.Monitoring;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class SessionJournalTests : IDisposable
{
    private readonly string _tempRoot;

    public SessionJournalTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"crashscope-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Constructor_CreatesStateFile()
    {
        var stateDir = Path.Combine(_tempRoot, "state");
        var sessionDir = Path.Combine(_tempRoot, "session");

        var journal = new SessionJournal(stateDir, sessionDir);

        Assert.True(File.Exists(Path.Combine(stateDir, "last-session.json")));
        Assert.NotNull(journal.Current);
        Assert.Equal(32, journal.Current.SessionId.Length);
        Assert.False(journal.Current.CleanShutdown);
    }

    [Fact]
    public void Heartbeat_UpdatesLastHeartbeatAt()
    {
        var stateDir = Path.Combine(_tempRoot, "state");
        var sessionDir = Path.Combine(_tempRoot, "session");

        var journal = new SessionJournal(stateDir, sessionDir);
        var original = journal.Current.LastHeartbeatAt;

        Thread.Sleep(100);
        journal.Heartbeat(DateTimeOffset.Now);

        Assert.True(journal.Current.LastHeartbeatAt > original);
        Assert.False(journal.Current.CleanShutdown);
    }

    [Fact]
    public void MarkClean_SetsCleanShutdownTrue()
    {
        var stateDir = Path.Combine(_tempRoot, "state");
        var sessionDir = Path.Combine(_tempRoot, "session");

        var journal = new SessionJournal(stateDir, sessionDir);
        journal.Heartbeat(DateTimeOffset.Now);
        Assert.False(journal.Current.CleanShutdown);

        journal.MarkClean();
        Assert.True(journal.Current.CleanShutdown);
    }

    [Fact]
    public void PreviousState_LoadsFromDisk()
    {
        var stateDir = Path.Combine(_tempRoot, "state");
        var sessionDir1 = Path.Combine(_tempRoot, "session1");
        var sessionDir2 = Path.Combine(_tempRoot, "session2");

        var journal1 = new SessionJournal(stateDir, sessionDir1);
        journal1.Heartbeat(DateTimeOffset.Now);
        journal1.MarkClean();

        var journal2 = new SessionJournal(stateDir, sessionDir2);

        Assert.NotNull(journal2.PreviousState);
        Assert.Equal(journal1.Current.SessionId, journal2.PreviousState!.SessionId);
        Assert.True(journal2.PreviousState.CleanShutdown);
    }

    [Fact]
    public void PreviousState_ReturnsNullForFreshInstall()
    {
        var stateDir = Path.Combine(_tempRoot, "fresh-state");

        var journal = new SessionJournal(stateDir, Path.Combine(_tempRoot, "session"));

        Assert.Null(journal.PreviousState);
    }

    [Fact]
    public void MultipleHeartbeats_PersistToDisk()
    {
        var stateDir = Path.Combine(_tempRoot, "state");
        var sessionDir = Path.Combine(_tempRoot, "session");

        var journal = new SessionJournal(stateDir, sessionDir);
        var originalId = journal.Current.SessionId;

        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(50);
            journal.Heartbeat(DateTimeOffset.Now);
        }

        var reloaded = new SessionJournal(stateDir, Path.Combine(_tempRoot, "session2"));

        Assert.NotNull(reloaded.PreviousState);
        Assert.Equal(originalId, reloaded.PreviousState!.SessionId);
        Assert.False(reloaded.PreviousState.CleanShutdown);
    }
}