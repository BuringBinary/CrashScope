using CrashScope.Core.Models;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class DiagnosticExportRedactionTests
{
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static IncidentAnalysis AnalysisWithApp(string processName) => new(
        new SessionState("session", Now, Now, false, "dir", true, "0.9.0"),
        null, 0, 0, null,
        [], [], [], [],
        new GraphicsContext(
            [new GraphicsAppPresence(processName, GraphicsAppCategory.RemoteControl, Now, Now, true)],
            [], [], [], [], [], [], null));

    [Fact]
    public void Redact_MasksGraphicsAppProcessNames_WithConsistentToken()
    {
        var redactor = new ExportRedactor("/nonexistent", AnalysisWithApp("toodesk"));
        var result = redactor.Redact("Foreground: toodesk (PID 1234) then start: toodesk (PID 1234)");

        Assert.Equal("Foreground: [PROCESS_1] (PID 1234) then start: [PROCESS_1] (PID 1234)", result);
    }

    [Fact]
    public void Redact_CollectsProcessNamesFromJsonlTails()
    {
        var dir = Directory.CreateTempSubdirectory("csredact");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "process-events-tail.jsonl"),
                """{"processName":"gameprocess","processName":"toodesk"}""");
            var redactor = new ExportRedactor(dir.FullName, AnalysisWithApp("toodesk"));
            var result = redactor.Redact("start: gameprocess (PID 9)");

            Assert.DoesNotContain("gameprocess", result);
            Assert.Contains("[PROCESS_", result);
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Redact_LongestNameWins_OverSubstring()
    {
        var dir = Directory.CreateTempSubdirectory("csredact");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "x.jsonl"),
                """{"processName":"steam"}""");
            var redactor = new ExportRedactor(dir.FullName, AnalysisWithApp("steamwebhelper"));
            var result = redactor.Redact("steamwebhelper and steam");

            Assert.DoesNotContain("steamwebhelper", result);
            Assert.Contains("[PROCESS_", result);
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Redact_MasksUserProfilePaths_WithUsernameToken()
    {
        var redactor = new ExportRedactor("/nonexistent", AnalysisWithApp("qq"));
        var result = redactor.Redact(@"C:\Users\JohnDoe\AppData\Local\test");

        Assert.Equal(@"[USERPROFILE]\[USERNAME]\AppData\Local\test", result);
    }

    [Fact]
    public void Redact_MasksIpAddresses()
    {
        var redactor = new ExportRedactor("/nonexistent", AnalysisWithApp("qq"));
        var result = redactor.Redact("10.0.0.1 and 172.16.0.5");

        Assert.Equal("[IP_ADDRESS] and [IP_ADDRESS]", result);
    }

    [Fact]
    public void Redact_MasksHostname()
    {
        var redactor = new ExportRedactor("/nonexistent", AnalysisWithApp("qq"));
        var result = redactor.Redact($"machine {Environment.MachineName} reported");

        Assert.Equal("machine [HOSTNAME] reported", result);
    }

    [Fact]
    public void Redact_NonRedactableContent_Unchanged()
    {
        var redactor = new ExportRedactor("/nonexistent", AnalysisWithApp("someuniquetoken123"));
        var input = "CPU temperature: 75C\nGPU load: 90%\nMemory: 8.2 GB / 16 GB";
        var result = redactor.Redact(input);

        Assert.Equal(input, result);
    }
}

