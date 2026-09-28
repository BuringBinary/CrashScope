
using CrashScope.Desktop.Services;
using Xunit;

namespace CrashScope.Core.Tests;

/// <summary>
/// Exercises the real registry read on this machine. Asserts self-consistency rather than a
/// fixed value, so it stays valid whichever way the developer's OS theme is set.
/// </summary>
public class ThemeServiceSystemReadTests
{
    [Fact]
    public void RegistryReadAgreesWithResolvedPalette()
    {
        var usesLight = ThemeService.ReadSystemUsesLightTheme();
        var resolved = ThemeService.ResolveEffectiveTheme(ThemeService.System, usesLight);
        Assert.Equal(usesLight ? "Light" : "Dark", resolved);
    }

    [Fact]
    public void RegistryReadIsStableAcrossCalls()
    {
        Assert.Equal(ThemeService.ReadSystemUsesLightTheme(), ThemeService.ReadSystemUsesLightTheme());
    }
}
