using CrashScope.Core.Analysis;
using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class GraphicsContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ClassifiedApp_InFinalSnapshot_RunningAtEnd()
    {
        var dir = Directory.CreateTempSubdirectory("csgcb");
        try
        {
            var snapshot = new ProcessSnapshot(Now, [new ProcessInfo(100, "todesk"), new ProcessInfo(200, "chrome")]);
            var context = GraphicsContextBuilder.Build(dir.FullName, [], [], [snapshot]);

            var todesk = context.GraphicsApps.First(a => a.ProcessName == "todesk");
            Assert.Equal(GraphicsAppCategory.RemoteControl, todesk.Category);
            Assert.True(todesk.RunningAtEnd);
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Build_StartAfterFinalSnapshot_CountsAsRunning()
    {
        var dir = Directory.CreateTempSubdirectory("csgcb");
        try
        {
            var snapshot = new ProcessSnapshot(Now.AddSeconds(-60), [new ProcessInfo(1, "explorer")]);
            var events = new[] { new ProcessEvent(Now.AddSeconds(-30), "start", 300, "parsecd") };
            var context = GraphicsContextBuilder.Build(dir.FullName, [], events, [snapshot]);

            var parsec = context.GraphicsApps.First(a => a.ProcessName == "parsecd");
            Assert.True(parsec.RunningAtEnd);
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Build_UnclassifiedApps_AreIgnored()
    {
        var dir = Directory.CreateTempSubdirectory("csgcb");
        try
        {
            var snapshot = new ProcessSnapshot(Now, [new ProcessInfo(1, "notarealappzzz")]);
            var context = GraphicsContextBuilder.Build(dir.FullName, [], [], [snapshot]);

            Assert.Empty(context.GraphicsApps);
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }
}

