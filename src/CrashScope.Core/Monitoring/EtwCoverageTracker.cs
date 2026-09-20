namespace CrashScope.Core.Monitoring;

public sealed record EtwCoverageSnapshot(
    string Provider, Guid ProviderId, bool Enabled, bool Processing, bool Stopped,
    long ReceivedEvents, long MatchedEvents, long WrittenEvents, long BufferDroppedEvents,
    DateTimeOffset? LastEventAt, DateTimeOffset? LastWrittenAt,
    string? ErrorKind, string? LastError, long OsLostEvents = 0);

/// <summary>Cumulative coverage, not a claim that absent events establish a healthy machine.</summary>
internal sealed class EtwCoverageTracker(string provider, Guid providerId)
{
    private readonly object _gate = new();
    private EtwCoverageSnapshot _state = new(provider, providerId, false, false, false,
        0, 0, 0, 0, null, null, null, null);

    public EtwCoverageSnapshot Snapshot() { lock (_gate) return _state; }
    public void Enabled() { lock (_gate) _state = _state with { Enabled = true }; }
    public void Processing(bool value) { lock (_gate) _state = _state with { Processing = value }; }
    public void Stopped() { lock (_gate) _state = _state with { Processing = false, Stopped = true }; }
    public void Received(DateTimeOffset at) { lock (_gate) _state = _state with { ReceivedEvents = _state.ReceivedEvents + 1, LastEventAt = at }; }
    public void Matched() { lock (_gate) _state = _state with { MatchedEvents = _state.MatchedEvents + 1 }; }
    public void Written(DateTimeOffset at) { lock (_gate) _state = _state with { WrittenEvents = _state.WrittenEvents + 1, LastWrittenAt = at }; }
    public void Dropped() { lock (_gate) _state = _state with { BufferDroppedEvents = _state.BufferDroppedEvents + 1 }; }
    public void OsLost(long value) { lock (_gate) _state = _state with { OsLostEvents = Math.Max(0, value) }; }
    public void Error(string operation, Exception ex)
    {
        var denied = ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005)
            || ex is System.ComponentModel.Win32Exception { NativeErrorCode: 5 };
        lock (_gate)
        {
            // Do not overwrite the actionable provider permission failure with a later shutdown error.
            if (_state.ErrorKind == "PermissionDenied") return;
            _state = _state with { ErrorKind = denied ? "PermissionDenied" : "Error",
                LastError = $"{operation}: {ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}" };
        }
    }

    public static string Status(EtwCoverageSnapshot s) => s.ErrorKind ??
        (s.WrittenEvents > 0 ? "Observed" : s.ReceivedEvents > 0 ? "Receiving" : s.Enabled ? "Started" : "NotStarted");

    public static string Detail(EtwCoverageSnapshot s) =>
        $"{s.Provider}: enabled={s.Enabled}, processing={s.Processing}, stopped={s.Stopped}; " +
        $"received={s.ReceivedEvents}, matched={s.MatchedEvents}, written={s.WrittenEvents}, " +
        $"handoffDropped={s.BufferDroppedEvents}, osLost={s.OsLostEvents}. " +
        (s.LastError ?? (s.WrittenEvents == 0 ? "No matching event was persisted; coverage is not verified." :
            "Matching events were persisted; this does not establish complete event coverage.")) +
        " Handoff drops are separate from OS ETW losses.";
}
