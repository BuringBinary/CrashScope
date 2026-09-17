using System.Security.Principal;
using CrashScope.Core.Infrastructure;
using System.Text.Json;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class SessionJournal
{
    private readonly string _statePath;
    private volatile SessionState _state;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SessionJournal(string rootDirectory, string sessionDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _statePath = Path.Combine(rootDirectory, "last-session.json");
        PreviousState = ReadPrevious(rootDirectory);
        SessionRecovery.Preserve(rootDirectory, PreviousState);

        var now = DateTimeOffset.Now;
        _state = new SessionState(
            Guid.NewGuid().ToString("N"),
            now,
            now,
            false,
            sessionDirectory,
            IsElevated(),
            typeof(SessionJournal).Assembly.GetName().Version?.ToString() ?? "0.1.0",
            NeverStarted: true);
        Persist(_state);
    }

    public SessionState? PreviousState { get; }
    public SessionState Current => _state;

    public void Heartbeat(DateTimeOffset now)
    {
        // A heartbeat proves the recorder actually ran: from here on, an unclean
        // end is a genuine incident, never a "persisted then died" false positive.
        Persist(_state with { LastHeartbeatAt = now, CleanShutdown = false, NeverStarted = false });
    }

    public void MarkClean()
    {
        Persist(_state with { LastHeartbeatAt = DateTimeOffset.Now, CleanShutdown = true, NeverStarted = false });
    }

    /// <summary>Read-only, fail-closed load: corruption must not look like a fresh install.</summary>
    public static SessionState? ReadPrevious(string stateDirectory)
    {
        var path = Path.Combine(stateDirectory, "last-session.json");
        if (!File.Exists(path)) return null;
        // Readers must allow the writer's atomic rename while they finish reading the old inode.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var state = JsonSerializer.Deserialize<SessionState>(stream, AtomicJsonFile.Options)
            ?? throw new InvalidDataException("Session state is empty; preserve it for manual recovery.");
        SessionRecovery.ValidateId(state.SessionId);
        return state;
    }

    public void MarkFault(string error)
    {
        Persist(_state with { CleanShutdown = false, NeverStarted = false, RecorderError = error });
    }

    private void Persist(SessionState next)
    {
        AtomicJsonFile.Write(_statePath, next);
        // Publish only the committed state. A failed write must not look clean/recoverable in memory.
        _state = next;
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
