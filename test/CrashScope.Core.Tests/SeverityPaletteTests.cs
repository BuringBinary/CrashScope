using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

/// <summary>
/// Severity colours must resolve through theme resource keys, so Light/Dark switching
/// also recolours code-behind badges. These tests cover the pure key mapping; the brush
/// lookup itself needs a WPF Application and is only checked for its safe fallback.
/// </summary>
public sealed class SeverityPaletteTests
{
    private static readonly string[] DeclaredKeys =
    {
        SeverityPalette.Critical,
        SeverityPalette.High,
        SeverityPalette.Medium,
        SeverityPalette.Low,
        SeverityPalette.Info,
        SeverityPalette.Neutral
    };

    [Theory]
    [InlineData("HardFreeze")]
    [InlineData("BlueScreen")]
    [InlineData("DriverTimeoutRecovery")]
    [InlineData("HardwareError")]
    [InlineData("AppCrash")]
    [InlineData("PowerLoss")]
    [InlineData("Unclassified")]
    [InlineData("")]
    [InlineData(null)]
    public void EveryModeMapsToADeclaredKey(string? mode)
        => Assert.Contains(SeverityPalette.KeyForMode(mode), DeclaredKeys);

    [Theory]
    [InlineData("Telemetry")]
    [InlineData("GPU")]
    [InlineData("Process")]
    [InlineData("App")]
    [InlineData("Windows Event")]
    [InlineData("External Evidence")]
    [InlineData("ETW-WHEA")]
    [InlineData(null)]
    public void EveryCategoryMapsToADeclaredKey(string? category)
        => Assert.Contains(SeverityPalette.KeyForCategory(category), DeclaredKeys);

    [Theory]
    [InlineData(100, "SeverityCritical")]
    [InlineData(90, "SeverityCritical")]
    [InlineData(89, "SeverityHigh")]
    [InlineData(75, "SeverityHigh")]
    [InlineData(74, "SeverityMedium")]
    [InlineData(55, "SeverityMedium")]
    [InlineData(54, "SeverityLow")]
    [InlineData(0, "SeverityLow")]
    [InlineData(-5, "SeverityLow")]
    public void PriorityBandsAreContiguousAndOrdered(int priority, string expected)
        => Assert.Equal(expected, SeverityPalette.KeyForPriority(priority));

    [Fact]
    public void UnknownModeAndCategoryDegradeToNeutralRatherThanCritical()
    {
        Assert.Equal(SeverityPalette.Neutral, SeverityPalette.KeyForMode("NotARealMode"));
        Assert.Equal(SeverityPalette.Neutral, SeverityPalette.KeyForCategory("NotARealCategory"));
    }

    [Fact]
    public void HardFailuresOutrankApplicationLevelFailures()
    {
        Assert.Equal(SeverityPalette.Critical, SeverityPalette.KeyForMode("HardFreeze"));
        Assert.Equal(SeverityPalette.Critical, SeverityPalette.KeyForMode("HardwareError"));
        Assert.Equal(SeverityPalette.Info, SeverityPalette.KeyForMode("AppCrash"));
    }

    [Fact]
    public void MissingResourceFallsBackToAVisibleBrushInsteadOfThrowing()
    {
        // No WPF Application in the test host, so this exercises the fallback path.
        var brush = SeverityPalette.Resolve("DefinitelyNotARealResourceKey");
        Assert.NotNull(brush);
        Assert.True(brush.IsFrozen);
    }
}
