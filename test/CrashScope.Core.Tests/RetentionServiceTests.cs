using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class RetentionServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public RetentionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"crashscope-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void DefaultSettings_MaxRetention30Days_MaxSize512Mb()
    {
        var service = new RetentionService(_tempRoot);

        Assert.Equal(30, service.Settings.MaxRetentionDays);
        Assert.Equal(512, service.Settings.MaxTotalSizeMb);
    }

    [Fact]
    public void Save_SavesSettingsToDisk()
    {
        var service = new RetentionService(_tempRoot);
        service.Settings.MaxRetentionDays = 7;
        service.Settings.MaxTotalSizeMb = 256;
        service.Save();

        var configPath = Path.Combine(_tempRoot, "retention.json");
        Assert.True(File.Exists(configPath));

        var reloaded = new RetentionService(_tempRoot);
        Assert.Equal(7, reloaded.Settings.MaxRetentionDays);
        Assert.Equal(256, reloaded.Settings.MaxTotalSizeMb);
    }

    [Fact]
    public void Enforce_RemovesOldDirectories()
    {
        var service = new RetentionService(_tempRoot);
        service.Settings.MaxRetentionDays = 1;
        service.Settings.MaxTotalSizeMb = 0;

        var sessionsDir = Path.Combine(_tempRoot, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var oldDir = Path.Combine(sessionsDir, "20200101-000000");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "test.txt"), "data");
        Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-10));

        var (sessionsDeleted, _, bytesFreed) = service.Enforce();

        Assert.Equal(1, sessionsDeleted);
        Assert.True(bytesFreed > 0);
        Assert.False(Directory.Exists(oldDir));
    }

    [Fact]
    public void Enforce_KeepsRecentDirectories()
    {
        var service = new RetentionService(_tempRoot);
        service.Settings.MaxRetentionDays = 1;
        service.Settings.MaxTotalSizeMb = 0;

        var sessionsDir = Path.Combine(_tempRoot, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var recentDir = Path.Combine(sessionsDir, "recentsession");
        Directory.CreateDirectory(recentDir);
        File.WriteAllText(Path.Combine(recentDir, "test.txt"), "data");
        Directory.SetCreationTime(recentDir, DateTime.Now);

        var (deleted, _, _) = service.Enforce();

        Assert.Equal(0, deleted);
        Assert.True(Directory.Exists(recentDir));
    }

    [Fact]
    public void GetCurrentUsage_ReturnsTotalBytes()
    {
        var sessionsDir = Path.Combine(_tempRoot, "sessions");
        Directory.CreateDirectory(sessionsDir);
        File.WriteAllText(Path.Combine(sessionsDir, "file1.txt"), new string('x', 1024));

        var service = new RetentionService(_tempRoot);
        var usage = service.GetCurrentUsage();

        Assert.True(usage >= 1024);
    }
}