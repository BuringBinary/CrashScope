using System.Xml.Linq;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

/// <summary>
/// OxyPlot cannot bind to DynamicResource, so the dashboard re-resolves chart colours
/// from the theme on every switch. That only works if both themes actually declare
/// every key the charts ask for. These tests guard that contract without needing WPF.
/// </summary>
public sealed class ChartPaletteTests
{
    private static readonly string[] ThemeFiles = { "DarkTheme.xaml", "LightTheme.xaml" };

    private static string ThemeDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CrashScope.Desktop", "Themes");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        var local = Path.Combine(AppContext.BaseDirectory, "Themes");
        Assert.True(Directory.Exists(local),
            "Could not locate the Themes directory from " + AppContext.BaseDirectory);
        return local;
    }

    private static HashSet<string> KeysIn(string themeFile)
    {
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var doc = XDocument.Load(Path.Combine(ThemeDirectory(), themeFile));
        return doc.Root!.Elements()
            .Select(e => (string?)e.Attribute(x + "Key"))
            .Where(k => !string.IsNullOrEmpty(k))
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void EveryChartKeyExistsInTheme(string themeFile)
    {
        var keys = KeysIn(themeFile);
        var missing = ChartPalette.RequiredKeys.Where(k => !keys.Contains(k)).ToArray();
        Assert.True(missing.Length == 0,
            themeFile + " is missing chart palette keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void BothThemesDeclareTheSameKeys()
    {
        var dark = KeysIn("DarkTheme.xaml");
        var light = KeysIn("LightTheme.xaml");
        Assert.Equal(dark.OrderBy(k => k, StringComparer.Ordinal),
                     light.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public void SeverityAndTintKeysAreDeclared(string themeFile)
    {
        var keys = KeysIn(themeFile);
        foreach (var key in new[]
                 {
                     "SeverityCritical", "SeverityHigh", "SeverityMedium",
                     "SeverityLow", "SeverityInfo", "SeverityNeutral",
                     "CpuTint", "GpuTint", "RamTint", "WarningTint"
                 })
        {
            Assert.True(keys.Contains(key), themeFile + " is missing " + key);
        }
    }

    [Fact]
    public void RequiredKeysAreUniqueAndNonEmpty()
    {
        Assert.All(ChartPalette.RequiredKeys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
        Assert.Equal(ChartPalette.RequiredKeys.Count, ChartPalette.RequiredKeys.Distinct().Count());
    }

    [Fact]
    public void MissingResourceResolvesToAVisibleColourInsteadOfThrowing()
    {
        // No WPF Application in the test host, so every lookup takes the fallback path.
        var color = ChartPalette.Resolve("DefinitelyNotARealResourceKey");
        Assert.True(color.A > 0, "Fallback chart colour must not be fully transparent.");
    }

    [Fact]
    public void AlphaIsPreservedWhenConvertingToOxy()
    {
        var argb = System.Windows.Media.Color.FromArgb(0x26, 0xA5, 0xA0, 0xFF);
        var oxy = ChartPalette.ToOxy(argb);
        Assert.Equal(0x26, oxy.A);
        Assert.Equal(0xA5, oxy.R);
        Assert.Equal(0xA0, oxy.G);
        Assert.Equal(0xFF, oxy.B);
    }
}
