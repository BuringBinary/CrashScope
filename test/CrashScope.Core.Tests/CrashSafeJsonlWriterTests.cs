using CrashScope.Core.Infrastructure;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class CrashSafeJsonlWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cswriter").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Write_AppendsOneJsonLinePerCall()
    {
        var path = Path.Combine(_dir, "telemetry.jsonl");
        using (var writer = new CrashSafeJsonlWriter(path))
        {
            writer.Write(new { a = 1 });
            writer.Write(new { a = 2 });
        }

        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void WriteAfterDispose_IsANoOp_InsteadOfThrowing()
    {
        // A recorder tick that is still in flight when RecorderEngine.Stop() disposes the
        // writers must not fault the recording task.
        var path = Path.Combine(_dir, "telemetry.jsonl");
        var writer = new CrashSafeJsonlWriter(path);
        writer.Write(new { a = 1 });
        writer.Dispose();

        var exception = Record.Exception(() => writer.Write(new { a = 2 }));

        Assert.Null(exception);
        Assert.Single(File.ReadAllLines(path));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var path = Path.Combine(_dir, "telemetry.jsonl");
        var writer = new CrashSafeJsonlWriter(path);
        writer.Write(new { a = 1 });

        var exception = Record.Exception(() =>
        {
            writer.Dispose();
            writer.Dispose();
        });

        Assert.Null(exception);
    }
}
