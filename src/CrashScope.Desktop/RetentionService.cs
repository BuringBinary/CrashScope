using System.IO;
using System.Text.Json;

namespace CrashScope.Desktop;

public sealed class RetentionService
{
    private readonly string _configPath;
    private readonly string _sessionsRoot;
    private readonly string _incidentsRoot;

    public RetentionService(string rootDirectory)
    {
        _configPath = Path.Combine(rootDirectory, "retention.json");
        _sessionsRoot = Path.Combine(rootDirectory, "sessions");
        _incidentsRoot = Path.Combine(rootDirectory, "incidents");

        if (File.Exists(_configPath))
            Settings = JsonSerializer.Deserialize<RetentionSettings>(
                File.ReadAllText(_configPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        else
            Settings = new RetentionSettings();

        Directory.CreateDirectory(rootDirectory);
    }

    public RetentionSettings Settings { get; }

    public void Save() =>
        File.WriteAllText(_configPath,
            JsonSerializer.Serialize(Settings, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

    public (int SessionsDeleted, int IncidentsDeleted, long BytesFreed) Enforce()
    {
        int sessionsDeleted = 0, incidentsDeleted = 0;
        long bytesFreed = 0;

        if (Settings.MaxRetentionDays > 0)
        {
            var cutoff = DateTime.Now.AddDays(-Settings.MaxRetentionDays);

            sessionsDeleted += PurgeOldDirectories(_sessionsRoot, cutoff, ref bytesFreed);
            incidentsDeleted += PurgeOldDirectories(_incidentsRoot, cutoff, ref bytesFreed);
        }

        if (Settings.MaxTotalSizeMb > 0)
        {
            var maxBytes = Settings.MaxTotalSizeMb * 1024L * 1024L;

            (var sDeleted, var sBytes) = EnforceTotalSize(_sessionsRoot, maxBytes / 2);
            sessionsDeleted += sDeleted;
            bytesFreed += sBytes;

            (var iDeleted, var iBytes) = EnforceTotalSize(_incidentsRoot, maxBytes / 2);
            incidentsDeleted += iDeleted;
            bytesFreed += iBytes;
        }

        return (sessionsDeleted, incidentsDeleted, bytesFreed);
    }

    private static int PurgeOldDirectories(string root, DateTime cutoff, ref long bytesFreed)
    {
        if (!Directory.Exists(root)) return 0;
        var deleted = 0;

        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                var creationTime = Directory.GetCreationTime(dir);
                if (creationTime < cutoff)
                {
                    var size = GetDirectorySize(dir);
                    Directory.Delete(dir, recursive: true);
                    bytesFreed += size;
                    deleted++;
                }
            }
            catch
            {
                // skip locked files
            }
        }

        return deleted;
    }

    private static (int Deleted, long BytesFreed) EnforceTotalSize(string root, long maxBytes)
    {
        if (!Directory.Exists(root)) return (0, 0);

        var dirs = Directory.GetDirectories(root)
            .Select(dir =>
            {
                try { return (Path: dir, Time: Directory.GetCreationTime(dir)); }
                catch { return (Path: dir, Time: DateTime.MinValue); }
            })
            .OrderBy(d => d.Time)
            .ToList();

        long totalSize = 0;
        foreach (var dir in dirs)
        {
            try { totalSize += GetDirectorySize(dir.Path); }
            catch { }
        }

        var deleted = 0;
        long bytesFreed = 0;

        foreach (var (path, _) in dirs)
        {
            if (totalSize <= maxBytes) break;

            try
            {
                var size = GetDirectorySize(path);
                Directory.Delete(path, recursive: true);
                totalSize -= size;
                bytesFreed += size;
                deleted++;
            }
            catch { }
        }

        return (deleted, bytesFreed);
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; }
                catch { }
            }
        }
        catch { }
        return size;
    }

    public long GetCurrentUsage()
    {
        long total = 0;
        total += GetDirectorySize(_sessionsRoot);
        total += GetDirectorySize(_incidentsRoot);
        return total;
    }
}

public sealed class RetentionSettings
{
    public int MaxRetentionDays { get; set; } = 30;
    public int MaxTotalSizeMb { get; set; } = 512;
}