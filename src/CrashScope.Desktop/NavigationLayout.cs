using System.Collections.ObjectModel;

namespace CrashScope.Desktop;

public enum NavigationGroup
{
    Monitoring,
    Diagnostics,
    Tools,
    Advanced,
    Settings
}

public sealed record NavigationEntry(string Route, NavigationGroup Group, bool IsPinned = false);

/// <summary>Stable route metadata used by the shell and by non-visual navigation tests.</summary>
public static class NavigationLayout
{
    public static IReadOnlyList<NavigationEntry> Entries { get; } = new ReadOnlyCollection<NavigationEntry>(new[]
    {
        new NavigationEntry("Home", NavigationGroup.Monitoring),
        new NavigationEntry("Dashboard", NavigationGroup.Monitoring),
        new NavigationEntry("LiveMonitor", NavigationGroup.Monitoring),
        new NavigationEntry("Incidents", NavigationGroup.Diagnostics),
        new NavigationEntry("Timeline", NavigationGroup.Diagnostics),
        new NavigationEntry("Hardware", NavigationGroup.Tools),
        new NavigationEntry("DiskHealth", NavigationGroup.Tools),
        new NavigationEntry("SpeedTest", NavigationGroup.Tools),
        new NavigationEntry("AgentMonitor", NavigationGroup.Advanced),
        new NavigationEntry("AgentIncidents", NavigationGroup.Advanced),
        new NavigationEntry("AgentActions", NavigationGroup.Advanced),
        new NavigationEntry("Settings", NavigationGroup.Settings, IsPinned: true)
    });

    public static NavigationGroup GroupFor(string? route) =>
        Entries.FirstOrDefault(entry => entry.Route == route)?.Group ?? NavigationGroup.Monitoring;

    public static bool IsAdvanced(string? route) => GroupFor(route) == NavigationGroup.Advanced;

    public static bool IsPinned(string? route) =>
        Entries.FirstOrDefault(entry => entry.Route == route)?.IsPinned == true;

    public static bool ShouldExpandAdvanced(string? route) => IsAdvanced(route);
}
