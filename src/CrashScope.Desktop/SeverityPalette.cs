using System.Windows;
using System.Windows.Media;
// UseWindowsForms puts System.Drawing and System.Windows.Forms in the global usings,
// so bare Application/Brush/Color are ambiguous (CS0104). Pin them to WPF.
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace CrashScope.Desktop;

/// <summary>
/// Resolves severity/category brushes from the active theme dictionary instead of
/// hardcoding hex in code-behind, so Light/Dark switching also recolors list badges.
/// Falls back to a neutral brush when a key is missing rather than throwing.
/// </summary>
public static class SeverityPalette
{
    public const string Critical = "SeverityCritical";
    public const string High = "SeverityHigh";
    public const string Medium = "SeverityMedium";
    public const string Low = "SeverityLow";
    public const string Info = "SeverityInfo";
    public const string Neutral = "SeverityNeutral";

    private static readonly SolidColorBrush Fallback = CreateFallback();

    private static SolidColorBrush CreateFallback()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA7));
        brush.Freeze();
        return brush;
    }

    /// <summary>Look up a themed brush by resource key.</summary>
    public static Brush Resolve(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        return Fallback;
    }

    /// <summary>Map an incident mode to a severity resource key.</summary>
    public static string KeyForMode(string? mode) => mode switch
    {
        "HardFreeze" => Critical,
        "HardwareError" => Critical,
        "BlueScreen" => Medium,
        "DriverTimeoutRecovery" => High,
        "AppCrash" => Info,
        "PowerLoss" => Neutral,
        _ => Neutral
    };

    /// <summary>Map a timeline category to a severity resource key.</summary>
    public static string KeyForCategory(string? category) => category switch
    {
        "Telemetry" => Medium,
        "GPU" => Info,
        "Process" => Critical,
        "App" => Critical,
        "Windows Event" => High,
        "External Evidence" => High,
        _ => Neutral
    };

    /// <summary>Map a recommendation priority (0-100) to a severity resource key.</summary>
    public static string KeyForPriority(int priority) => priority switch
    {
        >= 90 => Critical,
        >= 75 => High,
        >= 55 => Medium,
        _ => Low
    };
}
