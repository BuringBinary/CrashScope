

using CrashScope.Core.Infrastructure;

namespace CrashScope.Core.Actions;

/// <summary>
/// One audited system mutation. Records are append-only: a rollback never edits
/// an earlier row, it appends a new row that references the action it undid.
/// </summary>
public sealed record ActionRecord(
    string ActionId,
    DateTimeOffset Timestamp,
    string Action,
    string Target,
    string? OldValue,
    string NewValue,
    string Caller,
    string Reason,
    string Status,
    string? Error,
    bool Reversible);

/// <summary>
/// Append-only write-through journal (actions.jsonl in the agent state directory):
/// the forensic trail of every change CrashScope itself made to the system.
/// Reuses CrashSafeJsonlWriter so a crash mid-write cannot corrupt the trail.
/// </summary>
public sealed class ActionJournal
{
    private static readonly System.Text.Json.JsonSerializerOptions ReadOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly object _gate = new();
    private CrashSafeJsonlWriter? _writer;

    public ActionJournal(string stateDirectory)
    {
        _path = Path.Combine(stateDirectory, "actions.jsonl");
    }

    public void Append(ActionRecord record)
    {
        lock (_gate)
        {
            _writer ??= new CrashSafeJsonlWriter(_path);
            _writer.Write(record);
        }
    }

    /// <summary>Newest first. Corrupt lines are skipped; a missing file reads as empty.</summary>
    public IReadOnlyList<ActionRecord> ReadRecent(int max = 200)
    {
        var records = new List<ActionRecord>();
        try
        {
            if (!File.Exists(_path))
                return records;

            // The writer holds a write-through handle (FileShare.Read). A plain
            // File.ReadLines would request FileShare.Read on our side and fail the
            // open with a sharing violation, because the existing handle has Write
            // access - so read with FileShare.ReadWrite explicitly.
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    // Web casing on both sides: Write serializes camelCase, so the
                    // read side must match or every line silently fails to parse.
                    var record = System.Text.Json.JsonSerializer.Deserialize<ActionRecord>(line, ReadOptions);
                    if (record is not null)
                        records.Add(record);
                }
                catch
                {
                    // A torn or foreign line must not erase the rest of the trail.
                }
            }
        }
        catch
        {
            // Read failures degrade to "whatever parsed so far".
        }

        records.Reverse();
        return records.Count <= max ? records : records.Take(max).ToList();
    }
}
