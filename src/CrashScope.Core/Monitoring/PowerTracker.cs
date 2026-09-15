using System.Globalization;
using System.Runtime.InteropServices;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class PowerTracker
{
    private const int SmRemoteSession = 0x1000;

    private static readonly Dictionary<string, string> WellKnownSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["381b4222-f694-41f0-9685-ff5bb260df2e"] = "Balanced",
        ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = "High performance",
        ["a1841308-3541-4fab-b81f-f747f9315dea"] = "Power saver",
        ["ded574e5-64a2-421d-b2f6-9bdd36baaa53"] = "Power saver (legacy)",
        ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = "Ultimate performance"
    };

    private string? _schemeGuid;
    private bool? _isRemote;

    public PowerSchemeEvent? PollScheme(DateTimeOffset now)
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var guidPtr) != 0)
            return null;

        string guidText;
        try
        {
            guidText = Marshal.PtrToStructure<Guid>(guidPtr).ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(guidPtr);
        }

        if (string.Equals(_schemeGuid, guidText, StringComparison.OrdinalIgnoreCase))
            return null;

        var previous = _schemeGuid;
        _schemeGuid = guidText;
        var name = WellKnownSchemes.GetValueOrDefault(guidText, "custom plan");

        var change = previous is null ? "baseline" : "changed";
        var description = previous is null
            ? $"power scheme baseline: {name}"
            : $"power scheme switched to {name}";
        return new PowerSchemeEvent(now, change, guidText, name, description);
    }

    public RemoteSessionEvent? PollRemoteSession(DateTimeOffset now)
    {
        bool isRemote;
        try
        {
            isRemote = GetSystemMetrics(SmRemoteSession) != 0;
        }
        catch
        {
            return null;
        }

        if (_isRemote.HasValue && _isRemote.Value == isRemote)
            return null;

        _isRemote = isRemote;
        var description = _isRemote.Value
            ? "session became remote (RDP takeover or remote logon)"
            : "session is local (console)";
        return new RemoteSessionEvent(now, isRemote, description);
    }

    public static string SchemeDisplayName(string schemeGuid)
        => WellKnownSchemes.TryGetValue(schemeGuid, out var name)
            ? name
            : $"custom plan ({schemeGuid})";

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
