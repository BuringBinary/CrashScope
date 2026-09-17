using System.Text.Json;
using CrashScope.Core.Diagnostics;
using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

/// <summary>
/// Durable recovery queue. Callers of the Owned methods must hold the data-root lease.
/// A new session may replace last-session.json only after the previous state is queued.
/// </summary>
public static class SessionRecovery
{
    internal static string PendingDirectory(string stateDirectory) => Path.Combine(stateDirectory, "recovery-pending");
    private static string Receipt(string stateDirectory, string id) => Path.Combine(stateDirectory, "recovery-completed", id + ".json");

    internal static void Preserve(string stateDirectory, SessionState? previous)
    {
        if (previous is not { CleanShutdown: false, NeverStarted: false }) return;
        ValidateId(previous.SessionId);
        if (File.Exists(Receipt(stateDirectory, previous.SessionId))) return;
        var path = Path.Combine(PendingDirectory(stateDirectory), previous.SessionId + ".json");
        if (!File.Exists(path)) AtomicJsonFile.Write(path, previous);
        else
        {
            var saved = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), AtomicJsonFile.Options);
            if (saved is null || saved.SessionId != previous.SessionId || saved.SessionDirectory != previous.SessionDirectory)
                throw new InvalidDataException("Existing recovery job is invalid; keep last-session.json intact.");
        }
    }

    internal static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidDataException("Invalid session id; refusing to overwrite or recover ambiguous state.");
    }

    public static IncidentAnalysis? Recover(string stateDirectory, string incidentsRoot) => Recover(stateDirectory, incidentsRoot, null);

    internal static IncidentAnalysis? Recover(string stateDirectory, string incidentsRoot,
        Func<SessionState, string, IncidentAnalysis>? collector)
    {
        using var lease = DataRootLease.TryAcquire(stateDirectory);
        // Opening a Desktop must never recover (or rewrite) a live Agent's journal.
        return lease is null ? null : RecoverOwned(stateDirectory, incidentsRoot, collector);
    }

    internal static IncidentAnalysis? RecoverOwned(string stateDirectory, string incidentsRoot,
        Func<SessionState, string, IncidentAnalysis>? collector = null)
    {
        collector ??= CrashEvidenceCollector.CollectForUncleanSession;
        Preserve(stateDirectory, SessionJournal.ReadPrevious(stateDirectory));
        var pendingDirectory = PendingDirectory(stateDirectory);
        if (!Directory.Exists(pendingDirectory)) return null;
        IncidentAnalysis? latest = null;
        foreach (var pending in Directory.GetFiles(pendingDirectory, "*.json").Order())
        {
            var previous = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(pending), AtomicJsonFile.Options)
                ?? throw new InvalidDataException($"Invalid recovery state: {pending}");
            ValidateId(previous.SessionId);
            var receipt = Receipt(stateDirectory, previous.SessionId);
            if (File.Exists(receipt)) { File.Delete(pending); continue; }

            // The final directory is published only after all artifacts were written and flushed.
            // A crash after the directory rename but before the receipt is safely replayable.
            var final = Path.Combine(incidentsRoot, $"recovered-{previous.SessionId}");
            if (Directory.Exists(final))
            {
                latest = JsonSerializer.Deserialize<IncidentAnalysis>(File.ReadAllText(Path.Combine(final, "analysis.json")), AtomicJsonFile.Options)
                    ?? throw new InvalidDataException("Published incident has no analysis.");
            }
            else
            {
                var staging = Path.Combine(stateDirectory, "recovery-work", previous.SessionId);
                // Only this validated session's staging directory is discarded, never its source evidence.
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);
                latest = collector(previous, staging);
                foreach (var required in new[] { "analysis.json", "summary.md", "timeline.md" })
                    if (!File.Exists(Path.Combine(staging, required)))
                        throw new InvalidDataException($"Incomplete recovery: missing {required}");
                foreach (var path in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    file.Flush(flushToDisk: true);
                }
                Directory.CreateDirectory(incidentsRoot);
                Directory.Move(staging, final);
            }
            if (latest.PreviousSession.SessionId != previous.SessionId ||
                !File.Exists(Path.Combine(final, "summary.md")) || !File.Exists(Path.Combine(final, "timeline.md")))
                throw new InvalidDataException("Published incident does not match its recovery job.");
            AtomicJsonFile.Write(receipt, new { previous.SessionId, IncidentDirectory = final, CompletedAt = DateTimeOffset.UtcNow });
            File.Delete(pending);
        }
        return latest;
    }

    internal static HashSet<string> ProtectedSessions(string stateDirectory)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = SessionJournal.ReadPrevious(stateDirectory);
        // Conservatively protect the last session even if clean, until another session replaces it.
        if (current is not null) paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(current.SessionDirectory)));
        var pending = PendingDirectory(stateDirectory);
        if (Directory.Exists(pending))
            foreach (var path in Directory.GetFiles(pending, "*.json"))
            {
                var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), AtomicJsonFile.Options)
                    ?? throw new InvalidDataException("Invalid pending state; retention must fail closed.");
                paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(state.SessionDirectory)));
            }
        return paths;
    }
}
