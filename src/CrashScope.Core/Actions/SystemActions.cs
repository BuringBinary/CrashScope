

using System.Diagnostics;

namespace CrashScope.Core.Actions;

public sealed record ProcessEntry(int Pid, string Name, string Priority);

/// <summary>Typed failure so the RPC layer maps codes 1:1 onto error payloads.</summary>
public sealed class ActionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The mutation surface of CrashScope: every method either succeeds and appends an
/// audit row, or fails and appends a failed row. Nothing mutates without a reason.
/// Rollbacks are append-only too: they reference the action they undo.
/// </summary>
public interface ISystemActions
{
    IReadOnlyList<PowerPlanInfo> ListPowerPlans();

    IReadOnlyList<ProcessEntry> ListProcesses();

    IReadOnlyList<ActionRecord> RecentActions(int max = 200);

    ActionRecord SetPowerPlan(string guid, string caller, string reason);

    ActionRecord SetProcessPriority(int pid, string priority, string caller, string reason);

    ActionRecord Rollback(string actionId, string caller, string reason);
}

public sealed class SystemActions : ISystemActions
{
    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ProcessPriorityClass.Idle),
        nameof(ProcessPriorityClass.BelowNormal),
        nameof(ProcessPriorityClass.Normal),
        nameof(ProcessPriorityClass.AboveNormal),
        nameof(ProcessPriorityClass.High)
    };

    private readonly IPowerPlanService _power;
    private readonly ActionJournal _journal;

    public SystemActions(IPowerPlanService power, string stateDirectory)
    {
        _power = power;
        _journal = new ActionJournal(stateDirectory);
    }

    public IReadOnlyList<PowerPlanInfo> ListPowerPlans() => _power.GetPlans();

    public IReadOnlyList<ProcessEntry> ListProcesses()
    {
        var result = new List<ProcessEntry>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                result.Add(new ProcessEntry(process.Id, process.ProcessName, process.PriorityClass.ToString()));
            }
            catch
            {
                // Exited or access denied between enumeration and read: skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
    }

    public IReadOnlyList<ActionRecord> RecentActions(int max = 200) => _journal.ReadRecent(max);

    public ActionRecord SetPowerPlan(string guid, string caller, string reason)
    {
        RequireReason(reason);
        var plans = _power.GetPlans();
        var target = plans.FirstOrDefault(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase))
            ?? throw new ActionException("unknown-plan", $"No power plan '{guid}'.");

        var previous = _power.GetActive();
        if (previous?.Equals(guid, StringComparison.OrdinalIgnoreCase) == true)
            throw new ActionException("already-active", $"'{target.Name}' is already the active plan.");

        var record = new ActionRecord(
            NewActionId(), DateTimeOffset.Now, "set-power-plan", target.Name,
            previous, target.Guid, caller, reason.Trim(), "applied", null, Reversible: true);

        if (!_power.SetActive(target.Guid))
        {
            var failed = record with { Status = "failed", Error = "PowerSetActiveScheme failed.", Reversible = false };
            _journal.Append(failed);
            throw new ActionException("set-failed", failed.Error!);
        }

        _journal.Append(record);
        return record;
    }

    public ActionRecord SetProcessPriority(int pid, string priority, string caller, string reason)
    {
        RequireReason(reason);
        if (!AllowedPriorities.Contains(priority))
            throw new ActionException("unknown-priority",
                $"'{priority}' is not allowed. Allowed: {string.Join(", ", AllowedPriorities.OrderBy(p => p))}.");

        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch
        {
            throw new ActionException("process-not-found", $"No process with pid {pid}.");
        }

        string name;
        string previous;
        try
        {
            using (process)
            {
                name = process.ProcessName;
                previous = process.PriorityClass.ToString();
                var target = Enum.Parse<ProcessPriorityClass>(priority, ignoreCase: true);
                var record = new ActionRecord(
                    NewActionId(), DateTimeOffset.Now, "set-process-priority", $"{pid}:{name}",
                    previous, target.ToString(), caller, reason.Trim(), "applied", null, Reversible: true);

                try
                {
                    process.PriorityClass = target;
                }
                catch (Exception ex)
                {
                    var failed = record with { Status = "failed", Error = ex.Message, Reversible = false };
                    _journal.Append(failed);
                    throw new ActionException("set-failed", ex.Message);
                }

                _journal.Append(record);
                return record;
            }
        }
        catch (ActionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ActionException("process-not-found", $"pid {pid} exited before the change: {ex.Message}");
        }
    }

    public ActionRecord Rollback(string actionId, string caller, string reason)
    {
        RequireReason(reason);
        var target = _journal.ReadRecent(500).FirstOrDefault(r => r.ActionId == actionId)
            ?? throw new ActionException("rollback-target-not-found", $"No action '{actionId}' in the journal.");
        if (target.Status != "applied")
            throw new ActionException("rollback-failed", $"Action '{actionId}' was not applied (status: {target.Status}).");
        if (!target.Reversible)
            throw new ActionException("rollback-failed", $"Action '{actionId}' is not reversible.");

        switch (target.Action)
        {
            case "set-power-plan":
                var currentPlan = _power.GetActive();
                if (currentPlan?.Equals(target.OldValue, StringComparison.OrdinalIgnoreCase) == true)
                    throw new ActionException("rollback-failed", "The active plan already equals the rollback target.");
                if (!_power.SetActive(target.OldValue!))
                    throw new ActionException("rollback-failed", "Restoring the previous power plan failed.");
                break;

            case "set-process-priority":
                var pid = int.Parse(target.Target.AsSpan(0, target.Target.IndexOf(':')));
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!Enum.TryParse<ProcessPriorityClass>(target.OldValue, out var restored))
                        throw new ActionException("rollback-failed", $"Cannot parse previous priority '{target.OldValue}'.");
                    process.PriorityClass = restored;
                }
                catch (ActionException)
                {
                    throw;
                }
                catch
                {
                    throw new ActionException("rollback-failed", $"Process {target.Target} is gone; priority cannot be restored.");
                }

                break;

            default:
                throw new ActionException("rollback-failed", $"Action type '{target.Action}' has no rollback.");
        }

        var rollback = new ActionRecord(
            NewActionId(), DateTimeOffset.Now, "rollback", target.ActionId,
            target.NewValue, target.OldValue ?? string.Empty, caller, reason.Trim(), "applied", null, Reversible: false);
        _journal.Append(rollback);
        return rollback;
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ActionException("reason-required", "Every mutation needs a reason - that is the forensic contract.");
    }

    private static string NewActionId() => Guid.NewGuid().ToString("N");
}
