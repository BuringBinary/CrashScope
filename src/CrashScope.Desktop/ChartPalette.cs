using System.Windows;
using System.Windows.Media;
using OxyPlot;
// UseWindowsForms puts System.Drawing and System.Windows.Forms in the global usings,
// so bare Application/Color are ambiguous (CS0104). Pin them to WPF.
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace CrashScope.Desktop;

/// <summary>
/// Resolves OxyPlot colours from the active WPF theme dictionary.
/// OxyPlot has no DynamicResource equivalent, so charts must be re-coloured
/// explicitly whenever the theme changes; see DashboardPage.ApplyChartTheme.
/// </summary>
public static class ChartPalette
{
    public const string CpuKey = "CpuColor";
    public const string GpuKey = "GpuColor";
    public const string RamKey = "RamColor";
    public const string ClockKey = "AccentColor";
    public const string PowerKey = "WarningColor";
    public const string DropKey = "DangerColor";
    public const string SurgeKey = "SuccessColor";
    public const string GridKey = "BorderColor";
    public const string MutedKey = "TextSecondary";

    /// <summary>Convert a WPF colour to an OxyColor, preserving alpha.</summary>
    public static OxyColor ToOxy(Color color) => OxyColor.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>
    /// Look up a themed brush and convert it for OxyPlot. Falls back to a visible
    /// neutral grey when the key is missing or is not a solid colour brush, so a
    /// theme gap degrades to a readable chart rather than an invisible one.
    /// </summary>
    public static OxyColor Resolve(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
            return ToOxy(brush.Color);
        return OxyColor.FromRgb(0x8B, 0x95, 0xA7);
    }

    /// <summary>All keys the dashboard charts depend on. Used by tests and by theme audits.</summary>
    public static IReadOnlyList<string> RequiredKeys { get; } = new[]
    {
        CpuKey, GpuKey, RamKey, ClockKey, PowerKey, DropKey, SurgeKey, GridKey, MutedKey
    };
}
