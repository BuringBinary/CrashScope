using System.Text.Json;
using CrashScope.Core.Models;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class DiagnosticExportRedactionTests
{
    private static readonly DateTimeOffset Now = new(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // Fixed identity so these cases never depend on the machine or account running them.
    private static readonly ExportIdentity TestIdentity = new("TESTPC-01", "alice", "CORP");

    private static ExportRedactor Redactor(string app = "qq", ExportIdentity? identity = null) =>
        new("/nonexistent", AnalysisWithApp(app), identity ?? TestIdentity);

    private static string JsonValue(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetString()!;
    }

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

    [Fact]
    public void Redact_JsonEscapedProfilePath_IsMaskedAndJsonStaysValid()
    {
        var json = JsonSerializer.Serialize(new { sessionDirectory = @"C:\Users\bob\AppData\Local\CrashScope\sessions\s1" });
        Assert.Contains(@"C:\\Users\\bob", json);

        var result = Redactor().Redact(json, jsonEscaped: true);

        Assert.DoesNotContain("bob", result);
        Assert.Equal(@"[USERPROFILE]\[USERNAME]\AppData\Local\CrashScope\sessions\s1",
            JsonValue(result, "sessionDirectory"));
    }

    [Fact]
    public void Redact_EscapedNonAsciiProfileFolder_IsMasked()
    {
        var json = JsonSerializer.Serialize(new { path = @"C:\Users\张三\Documents", other = "D:/Users/李四/x" });
        Assert.DoesNotContain("张三", json);

        var result = Redactor().Redact(json, jsonEscaped: true);

        Assert.Equal(@"[USERPROFILE]\[USERNAME]\Documents", JsonValue(result, "path"));
        Assert.Equal("[USERPROFILE]/[USERNAME]/x", JsonValue(result, "other"));
    }

    [Fact]
    public void Redact_ForwardSlashProfilePath_IsMasked()
    {
        Assert.Equal("[USERPROFILE]/[USERNAME]/Documents/x.txt", Redactor().Redact("C:/Users/bob/Documents/x.txt"));
    }

    [Fact]
    public void Redact_MachineAndDomainQualifiedAccounts_MaskTheAccountName()
    {
        var redactor = Redactor();

        Assert.Equal(@"User: [HOSTNAME]\[USERNAME] Session ID: 2", redactor.Redact(@"User: TESTPC-01\bob Session ID: 2"));
        Assert.Equal(@"[DOMAIN]\[USERNAME] logged on", redactor.Redact(@"corp\carol logged on"));

        var json = JsonSerializer.Serialize(new { message = "User: TESTPC-01\\bob\r\nSession ID: 2" });
        Assert.Equal("User: [HOSTNAME]\\[USERNAME]\r\nSession ID: 2",
            JsonValue(redactor.Redact(json, jsonEscaped: true), "message"));
    }

    [Fact]
    public void Redact_MachineNameBeforeJsonEscape_KeepsJsonValid()
    {
        // In JSON a single backslash always starts an escape, never MACHINE\user.
        var json = JsonSerializer.Serialize(new { message = "Computer: TESTPC-01\r\n计算机TESTPC-01上" });

        var result = Redactor().Redact(json, jsonEscaped: true);

        Assert.Equal("Computer: [HOSTNAME]\r\n计算机[HOSTNAME]上", JsonValue(result, "message"));
    }

    [Fact]
    public void Redact_CurrentUserName_IsMaskedInTextAndJson()
    {
        var redactor = Redactor();
        Assert.Equal("User: [USERNAME]\nDomain: [HOSTNAME]", redactor.Redact("User: alice\nDomain: TESTPC-01"));

        var json = JsonSerializer.Serialize(new { message = "Logon:\r\nalice" });
        Assert.Equal("Logon:\r\n[USERNAME]", JsonValue(redactor.Redact(json, jsonEscaped: true), "message"));

        var cjk = Redactor(identity: new ExportIdentity("TESTPC-01", "张三", "TESTPC-01"));
        var cjkJson = JsonSerializer.Serialize(new { owner = "张三", note = "由张三启动" });
        var cjkResult = cjk.Redact(cjkJson, jsonEscaped: true);
        Assert.Equal("[USERNAME]", JsonValue(cjkResult, "owner"));
        Assert.Equal("由[USERNAME]启动", JsonValue(cjkResult, "note"));
        Assert.Equal("由[USERNAME]启动", cjk.Redact("由张三启动"));
    }

    [Fact]
    public void Redact_Ipv6Addresses_ButNotClockTimes()
    {
        var redactor = Redactor();

        Assert.Equal("src [IP_ADDRESS] dst [IP_ADDRESS] lo [IP_ADDRESS]",
            redactor.Redact("src fe80::1c2d:3e4f:5a6b:7c8d%12 dst 2001:db8:85a3:0:0:8a2e:370:7334 lo ::1"));
        Assert.Equal("mapped ::ffff:[IP_ADDRESS]", redactor.Redact("mapped ::ffff:192.168.1.10"));
        Assert.Equal("源地址[IP_ADDRESS]，目标[IP_ADDRESS]", redactor.Redact("源地址10.0.0.8，目标fe80::2"));

        const string times = "at 16:21:41, 2026-09-28T16:21:41+08:00, uptime 0:05:10, ratio 3:2";
        Assert.Equal(times, redactor.Redact(times));
    }

    [Fact]
    public void Redact_AddressesRightAfterJsonEscapes_AreMasked()
    {
        var json = JsonSerializer.Serialize(new { message = "Address:\n10.1.2.3\nfe80::1" });

        Assert.Equal("Address:\n[IP_ADDRESS]\n[IP_ADDRESS]",
            JsonValue(Redactor().Redact(json, jsonEscaped: true), "message"));
    }

    [Fact]
    public void Redact_ProcessNameRightAfterJsonEscape_IsMasked()
    {
        var json = JsonSerializer.Serialize(new { message = "Faulting application 'toodesk.exe'\r\ntoodesk exited" });
        Assert.Contains(@"\u0027toodesk", json);

        var result = Redactor("toodesk").Redact(json, jsonEscaped: true);

        Assert.DoesNotContain("toodesk", result);
        Assert.Equal("Faulting application '[PROCESS_1].exe'\r\n[PROCESS_1] exited", JsonValue(result, "message"));
    }

    [Fact]
    public void Redact_NonAsciiProcessName_GetsOneTokenInJsonAndText()
    {
        var dir = Directory.CreateTempSubdirectory("csredact");
        try
        {
            var line = JsonSerializer.Serialize(new { processName = "三角洲行动" });
            File.WriteAllText(Path.Combine(dir.FullName, "process-events-tail.jsonl"), line);
            var redactor = new ExportRedactor(dir.FullName, AnalysisWithApp("qq"), TestIdentity);

            var token = JsonValue(redactor.Redact(line, jsonEscaped: true), "processName");

            Assert.StartsWith("[PROCESS_", token);
            Assert.Equal($"start: {token} (PID 7)", redactor.Redact("start: 三角洲行动 (PID 7)"));
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Fact]
    public void Redact_ProcessNamedHostname_KeepsEarlierTokens()
    {
        Assert.Equal("machine [HOSTNAME] ran [PROCESS_1]", Redactor("hostname").Redact("machine TESTPC-01 ran hostname"));
    }

    [Fact]
    public void Passthrough_ChangesNothing()
    {
        const string input = @"C:\Users\bob\x 10.0.0.1 fe80::1 TESTPC-01\bob";

        Assert.Equal(input, ExportRedactor.Passthrough.Redact(input));
        Assert.Equal(input, ExportRedactor.Passthrough.Redact(input, jsonEscaped: true));
    }
}

