using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class NavigationLayoutTests
{
    [Fact]
    public void StableRoutesAreUniqueAndIncludePinnedSettings()
    {
        Assert.Equal(NavigationLayout.Entries.Count, NavigationLayout.Entries.Select(entry => entry.Route).Distinct().Count());
        Assert.Equal("Home", NavigationLayout.Entries[0].Route);
        Assert.Equal("Settings", NavigationLayout.Entries[^1].Route);
        Assert.True(NavigationLayout.IsPinned("Settings"));
    }

    [Theory]
    [InlineData("AgentMonitor")]
    [InlineData("AgentIncidents")]
    [InlineData("AgentActions")]
    public void AgentRoutesBelongToAdvancedGroupAndRequestExpansion(string route)
    {
        Assert.Equal(NavigationGroup.Advanced, NavigationLayout.GroupFor(route));
        Assert.True(NavigationLayout.IsAdvanced(route));
        Assert.True(NavigationLayout.ShouldExpandAdvanced(route));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Dashboard")]
    [InlineData("LiveMonitor")]
    [InlineData("Incidents")]
    [InlineData("Timeline")]
    public void PrimaryRoutesRemainNonAdvanced(string route)
        => Assert.False(NavigationLayout.ShouldExpandAdvanced(route));

    [Theory]
    [InlineData("Hardware")]
    [InlineData("DiskHealth")]
    [InlineData("SpeedTest")]
    public void ToolRoutesStayInToolsGroup(string route)
        => Assert.Equal(NavigationGroup.Tools, NavigationLayout.GroupFor(route));

    [Fact]
    public void SettingsIsPinnedAndDoesNotOpenAdvancedGroup()
    {
        Assert.Equal(NavigationGroup.Settings, NavigationLayout.GroupFor("Settings"));
        Assert.True(NavigationLayout.IsPinned("Settings"));
        Assert.False(NavigationLayout.ShouldExpandAdvanced("Settings"));
    }

    // The Recorder page was merged into Home so that starting/stopping a recording has exactly one
    // entry point per surface: the Home page and the always-visible sidebar card.
    [Fact]
    public void RecorderRouteIsRetiredAfterMergeIntoHome()
    {
        Assert.DoesNotContain(NavigationLayout.Entries, entry => entry.Route == "Recorder");
        Assert.Equal("Home", NavigationLayout.Entries[0].Route);
    }

    // Microsoft's NavigationView guidance recommends 5-10 top-level destinations and a shallow
    // hierarchy; groups holding a single item add scanning cost without adding structure.
    [Fact]
    public void EveryNavigationGroupHoldsAtLeastTwoRoutesExceptPinnedSettings()
    {
        var groups = NavigationLayout.Entries
            .Where(entry => !entry.IsPinned)
            .GroupBy(entry => entry.Group);

        Assert.All(groups, group => Assert.True(group.Count() >= 2, $"group {group.Key} has {group.Count()} route(s)"));
    }

    [Fact]
    public void HomeIsGroupedWithTheOtherMonitoringRoutes()
    {
        Assert.Equal(NavigationGroup.Monitoring, NavigationLayout.GroupFor("Home"));
        Assert.Equal(NavigationGroup.Monitoring, NavigationLayout.GroupFor("Dashboard"));
        Assert.Equal(NavigationGroup.Monitoring, NavigationLayout.GroupFor("LiveMonitor"));
    }
}
