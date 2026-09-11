using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CrashScope.Agent.Models;

namespace CrashScope.Agent.Monitoring;

public sealed class SessionJournal
{
    private readonly string _statePath;
    private SessionState _state;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SessionJournal(string rootDirectory, string sessionDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        _statePath = Path.Combine(rootDirectory, "last-session.json");
        PreviousState = LoadPrevious();

        var now = DateTimeOffset.Now;
        _state = new SessionState(
            Guid.NewGuid().ToString("N"),
            now,
            now,
            false,
            sessionDirectory,
            IsElevated(),
            typeof(SessionJournal).Assembly.GetName().Version?.ToString() ?? "0.1.0");
        Persist();
    }

    public SessionState? PreviousState { get; }
    public SessionState Current => _state;

    public void Heartbeat(DateTimeOffset now)
    {
        _state = _state with { LastHeartbeatAt = now, CleanShutdown = false };
        Persist();
    }

    public void MarkClean()
    {
        _state = _state with { LastHeartbeatAt = DateTimeOffset.Now, CleanShutdown = true };
        Persist();
    }

    private SessionState? LoadPrevious()
    {
        try
        {
            if (!File.Exists(_statePath)) return null;
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(_statePath), _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_state, _jsonOptions);
        var bytes = new UTF8Encoding(false).GetBytes(json);
        var temp = _statePath + ".tmp";

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.Read,
                   bufferSize: 4096, options: FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, _statePath, overwrite: true);
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
