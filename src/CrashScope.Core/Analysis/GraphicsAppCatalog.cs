using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

internal static class GraphicsAppCatalog
{
    private static readonly Dictionary<string, GraphicsAppCategory> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["todesk"] = GraphicsAppCategory.RemoteControl,
        ["sunloginclient"] = GraphicsAppCategory.RemoteControl,
        ["rustdesk"] = GraphicsAppCategory.RemoteControl,
        ["teamviewer"] = GraphicsAppCategory.RemoteControl,
        ["anydesk"] = GraphicsAppCategory.RemoteControl,
        ["parsecd"] = GraphicsAppCategory.RemoteControl,
        ["mstsc"] = GraphicsAppCategory.RemoteControl,
        ["msrdc"] = GraphicsAppCategory.RemoteControl,
        ["winvnc"] = GraphicsAppCategory.RemoteControl,
        ["quickassist"] = GraphicsAppCategory.RemoteControl,
        ["uu"] = GraphicsAppCategory.RemoteControl,
        ["uuremote"] = GraphicsAppCategory.RemoteControl,

        ["wegame"] = GraphicsAppCategory.GameLauncher,
        ["steam"] = GraphicsAppCategory.GameLauncher,
        ["epicgameslauncher"] = GraphicsAppCategory.GameLauncher,
        ["riotclientservices"] = GraphicsAppCategory.GameLauncher,
        ["battle.net"] = GraphicsAppCategory.GameLauncher,
        ["ubisoftconnect"] = GraphicsAppCategory.GameLauncher,
        ["upc"] = GraphicsAppCategory.GameLauncher,
        ["eaapp"] = GraphicsAppCategory.GameLauncher,
        ["eadesktop"] = GraphicsAppCategory.GameLauncher,
        ["goggalaxy"] = GraphicsAppCategory.GameLauncher,
        ["leagueclient"] = GraphicsAppCategory.GameLauncher,

        ["gameoverlayui"] = GraphicsAppCategory.GameOverlay,
        ["rtss"] = GraphicsAppCategory.GameOverlay,
        ["gamebar"] = GraphicsAppCategory.GameOverlay,
        ["gamebarftserver"] = GraphicsAppCategory.GameOverlay,
        ["afterburner"] = GraphicsAppCategory.GameOverlay,

        ["chrome"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["msedge"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["firefox"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["steamwebhelper"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["qq"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["wechat"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["wechatappex"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["dingtalk"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["feishu"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["lark"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["wemeet"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["teams"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["zoom"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["telegram"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["discord"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["douyin"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["bililive"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["potplayer"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["vlc"] = GraphicsAppCategory.HardwareAcceleratedApp,
        ["spotify"] = GraphicsAppCategory.HardwareAcceleratedApp,

        ["obs32"] = GraphicsAppCategory.CaptureTool,
        ["obs64"] = GraphicsAppCategory.CaptureTool,
        ["bdcam"] = GraphicsAppCategory.CaptureTool,
        ["ocamstudio"] = GraphicsAppCategory.CaptureTool
    };

    public static GraphicsAppCategory? Classify(string? processName)
    {
        if (string.IsNullOrEmpty(processName) || !KnownApps.TryGetValue(processName, out var category))
            return null;
        return category;
    }

    public static string CategoryLabel(GraphicsAppCategory category) => category switch
    {
        GraphicsAppCategory.GameLauncher => "game launcher",
        GraphicsAppCategory.GameOverlay => "game overlay",
        GraphicsAppCategory.RemoteControl => "remote-control",
        GraphicsAppCategory.HardwareAcceleratedApp => "hardware-accelerated app",
        GraphicsAppCategory.CaptureTool => "capture tool",
        _ => category.ToString()
    };
}
