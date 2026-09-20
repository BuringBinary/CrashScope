using System.Management;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class PageFileTracker
{
    private readonly Func<PageFileConfiguration> _capture;
    private PageFileConfiguration _last = new(null, [], null);
    private long? _lastCommitLimit;

    public string HealthStatus { get; private set; } = "AwaitingSample";
    public string HealthDetail { get; private set; } = "Waiting for page-file configuration.";

    public PageFileTracker() : this(CaptureConfiguration) { }
    internal PageFileTracker(Func<PageFileConfiguration> capture) => _capture = capture;

    public PageFileSnapshot Capture(DateTimeOffset now, long? commitLimitBytes, string reason)
    {
        try { _last = _capture(); }
        catch (Exception ex) { _last = new PageFileConfiguration(null, _last.PageFiles, $"{ex.GetType().Name}: {ex.Message}"); }
        var previous = _lastCommitLimit;
        if (commitLimitBytes.HasValue) _lastCommitLimit = commitLimitBytes;
        SetHealth();
        return new PageFileSnapshot(now, reason, _last.AutomaticManagedPagefile, commitLimitBytes ?? _lastCommitLimit,
            previous, _last.PageFiles, _last.Error);
    }

    public PageFileSnapshot? ObserveCommitLimit(DateTimeOffset now, long commitLimitBytes)
    {
        if (!_lastCommitLimit.HasValue)
        {
            _lastCommitLimit = commitLimitBytes;
            return null;
        }
        if (_lastCommitLimit.Value == commitLimitBytes) return null;
        var previous = _lastCommitLimit;
        _lastCommitLimit = commitLimitBytes;
        return new PageFileSnapshot(now, "CommitLimitChanged", _last.AutomaticManagedPagefile,
            commitLimitBytes, previous, _last.PageFiles, _last.Error);
    }

    private void SetHealth()
    {
        HealthStatus = _last.Error is null ? "Collecting" : "Degraded";
        HealthDetail = _last.Error ??
            $"automaticManaged={_last.AutomaticManagedPagefile?.ToString() ?? "unknown"}, pageFiles={_last.PageFiles.Count}.";
    }

    private static PageFileConfiguration CaptureConfiguration()
    {
        bool? automatic = null;
        var errors = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                automatic = item["AutomaticManagedPagefile"] as bool?;
                item.Dispose();
                break;
            }
        }
        catch (Exception ex) { errors.Add("Win32_ComputerSystem: " + ex.Message); }

        var settings = new Dictionary<string, (long? Initial, long? Maximum)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                var name = item["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    settings[name] = (Megabytes(item["InitialSize"]), Megabytes(item["MaximumSize"]));
                item.Dispose();
            }
        }
        catch (Exception ex) { errors.Add("Win32_PageFileSetting: " + ex.Message); }

        var files = new List<PageFileInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage, TempPageFile FROM Win32_PageFileUsage");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                var name = item["Name"]?.ToString() ?? "unknown";
                settings.TryGetValue(name, out var configured);
                var volume = Path.GetPathRoot(name);
                files.Add(new PageFileInfo(name, volume, Megabytes(item["AllocatedBaseSize"]),
                    Megabytes(item["CurrentUsage"]), Megabytes(item["PeakUsage"]),
                    configured.Initial, configured.Maximum, VolumeFreeBytes(volume), item["TempPageFile"] as bool?));
                item.Dispose();
            }
        }
        catch (Exception ex) { errors.Add("Win32_PageFileUsage: " + ex.Message); }

        return new PageFileConfiguration(automatic, files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Count == 0 ? null : string.Join(" | ", errors));
    }

    private static long? Megabytes(object? value)
    {
        try { return checked(Convert.ToInt64(value) * 1024L * 1024L); }
        catch { return null; }
    }

    private static long? VolumeFreeBytes(string? volume)
    {
        if (string.IsNullOrWhiteSpace(volume)) return null;
        try
        {
            var drive = new DriveInfo(volume);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch { return null; }
    }

    internal sealed record PageFileConfiguration(bool? AutomaticManagedPagefile,
        IReadOnlyList<PageFileInfo> PageFiles, string? Error);
}
