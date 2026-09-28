using CrashScope.Desktop.Services;
using Xunit;

namespace CrashScope.Core.Tests;

/// <summary>
/// Covers the preference-to-palette mapping. The registry read and the SystemEvents hook
/// need a real OS/UI thread, so the mapping was kept as a pure static to be testable here.
/// </summary>
public class ThemeServiceResolveTests
{
    [Theory]
    [InlineData("Dark", false, "Dark")]
    [InlineData("Dark", true, "Dark")]
    [InlineData("Light", false, "Light")]
    [InlineData("Light", true, "Light")]
    public void ExplicitPreferenceIgnoresSystemFlag(string pref, bool sysLight, string expected)
    {
        Assert.Equal(expected, ThemeService.ResolveEffectiveTheme(pref, sysLight));
    }

    [Theory]
    [InlineData(true, "Light")]
    [InlineData(false, "Dark")]
    public void SystemPreferenceFollowsTheFlag(bool sysLight, string expected)
    {
        Assert.Equal(expected, ThemeService.ResolveEffectiveTheme("System", sysLight));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Solarized")]
    [InlineData("dark")]
    public void UnknownPreferenceFallsBackToDark(string? pref)
    {
        // Case-sensitive on purpose: the stored value comes from AvailableThemes, so a
        // differently-cased string means corrupted settings and should not silently pass.
        Assert.Equal("Dark", ThemeService.ResolveEffectiveTheme(pref, systemUsesLight: false));
        Assert.Equal("Dark", ThemeService.ResolveEffectiveTheme(pref, systemUsesLight: true));
    }

    [Fact]
    public void AvailableThemesExposesExactlyTheThreeSupportedValues()
    {
        Assert.Equal(new[] { "Dark", "Light", "System" }, ThemeService.SupportedThemes);
    }

    [Fact]
    public void ResolveNeverReturnsTheSystemSentinel()
    {
        // "System" is a preference, never a palette - ApplyTheme would not find a dictionary.
        foreach (var pref in ThemeService.SupportedThemes)
        {
            Assert.NotEqual("System", ThemeService.ResolveEffectiveTheme(pref, true));
            Assert.NotEqual("System", ThemeService.ResolveEffectiveTheme(pref, false));
        }
    }
}

