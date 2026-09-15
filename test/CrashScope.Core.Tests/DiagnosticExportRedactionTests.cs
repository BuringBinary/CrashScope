using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class DiagnosticExportRedactionTests
{
    [Theory]
    [InlineData(@"C:\Users\JohnDoe\AppData\Local\test", "[USERPROFILE]\\AppData\\Local\\test")]
    [InlineData(@"Found at C:\Users\admin123\Documents", "Found at [USERPROFILE]\\Documents")]
    [InlineData("No user path here", "No user path here")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void RedactContent_MasksUserProfilePaths(string input, string expected)
    {
        var result = DiagnosticExportService.RedactContent(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Server at 192.168.1.100 responded", "Server at [IP_ADDRESS] responded")]
    [InlineData("10.0.0.1 and 172.16.0.5", "[IP_ADDRESS] and [IP_ADDRESS]")]
    [InlineData("No IPs here", "No IPs here")]
    public void RedactContent_MasksIpAddresses(string input, string expected)
    {
        var result = DiagnosticExportService.RedactContent(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(@"- mysecretapp.exe (1234)", @"- [WINDOW_TITLE] (1234)")]
    [InlineData(@"- Microsoft Visual Studio - Admin (5678)", @"- [WINDOW_TITLE] (5678)")]
    public void RedactContent_MasksWindowTitles(string input, string expected)
    {
        var result = DiagnosticExportService.RedactContent(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RedactContent_CombinedPatterns_AllRedacted()
    {
        var input = @"Logged from C:\Users\TestUser\Desktop on machine 192.168.1.50";
        var result = DiagnosticExportService.RedactContent(input);

        Assert.DoesNotContain("Users\\TestUser", result);
        Assert.Contains("[USERPROFILE]", result);
        Assert.DoesNotContain("192.168.1.50", result);
        Assert.Contains("[IP_ADDRESS]", result);
    }

    [Fact]
    public void RedactContent_NonRedactableContent_Unchanged()
    {
        var input = "CPU temperature: 75°C\nGPU load: 90%\nMemory: 8.2 GB / 16 GB";
        var result = DiagnosticExportService.RedactContent(input);

        Assert.Equal(input, result);
    }
}