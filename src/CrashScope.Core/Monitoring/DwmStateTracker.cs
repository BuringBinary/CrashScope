using System.Runtime.InteropServices;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

/// <summary>
/// Tracks DWM composition state and emits an event only on a real transition.
/// </summary>
/// <remarks>
/// <c>DwmIsCompositionEnabled</c> is the only reliable user-mode signal available here.
/// Under the WDDM driver model DWM composition is hardware accelerated whenever it is
/// enabled, so <see cref="DwmStateEvent.IsHardwareRendering"/> is reported as equal to
/// <see cref="DwmStateEvent.IsCompositionEnabled"/> instead of being measured
/// independently.
/// <para>
/// Known limitation: a fallback to software rendering while composition stays enabled
/// is therefore not detectable by this tracker. The field is kept for JSONL schema
/// stability. If a real incident ever needs the distinction, the honest source is the
/// DWM process' GPU engine utilisation (already captured in gpu-engines.jsonl), not a
/// second copy of the composition flag.
/// </para>
/// </remarks>
public sealed class DwmStateTracker
{
    private readonly Func<(int HResult, bool Enabled)> _probe;
    private bool? _lastCompositionEnabled;

    public bool IsAvailable { get; private set; } = true;
    public string HealthStatus { get; private set; } = "AwaitingSample";
    public string HealthDetail { get; private set; } = "Waiting for DWM composition baseline.";

    public DwmStateTracker() : this(Probe) { }
    internal DwmStateTracker(Func<(int HResult, bool Enabled)> probe) => _probe = probe;

    public DwmStateEvent? Poll(DateTimeOffset now)
    {
        var result = _probe();
        if (result.HResult != 0)
        {
            IsAvailable = false;
            HealthStatus = "Unavailable";
            HealthDetail = $"DwmIsCompositionEnabled failed with HRESULT 0x{result.HResult:X8}.";
            return null;
        }
        IsAvailable = true;
        var compositionEnabled = result.Enabled;
        HealthStatus = "Collecting";
        HealthDetail = compositionEnabled ? "DWM composition is enabled." : "DWM composition is disabled.";

        if (_lastCompositionEnabled is bool previous)
        {
            if (previous == compositionEnabled)
                return null;

            _lastCompositionEnabled = compositionEnabled;
            var description = compositionEnabled
                ? "DWM composition was re-enabled; GPU hardware rendering restored"
                : "DWM composition was disabled; GPU fell back to software rendering";
            return new DwmStateEvent(now, compositionEnabled, compositionEnabled, description);
        }

        _lastCompositionEnabled = compositionEnabled;
        var baseline = compositionEnabled
            ? "DWM hardware composition is active (baseline)"
            : "DWM composition is disabled; system may be using software rendering (baseline)";
        return new DwmStateEvent(now, compositionEnabled, compositionEnabled, baseline);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool pfEnabled);

    private static (int HResult, bool Enabled) Probe()
    {
        var result = DwmIsCompositionEnabled(out var enabled);
        return (result, enabled);
    }
}
