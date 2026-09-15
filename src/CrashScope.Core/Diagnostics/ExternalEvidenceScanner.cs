using CrashScope.Core.Models;

namespace CrashScope.Core.Diagnostics;

public static class ExternalEvidenceScanner
{
    public static IReadOnlyList<ExternalEvidenceFile> Scan(DateTimeOffset start, DateTimeOffset end)
    {
        var results = new List<ExternalEvidenceFile>();
        ScanWerReports(results, start, end);
        ScanDumpDirectories(results, start, end);
        return results;
    }

    private static void ScanWerReports(List<ExternalEvidenceFile> results, DateTimeOffset start, DateTimeOffset end)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var store in new[] { "ReportArchive", "ReportQueue" })
        {
            var storePath = Path.Combine(programData, "Microsoft", "Windows", "WER", store);
            if (!Directory.Exists(storePath))
                continue;

            foreach (var reportDirectory in SafeEnumerateDirectories(storePath))
            {
                try
                {
                    if (!IsInRange(reportDirectory.LastWriteTimeUtc, start.UtcDateTime, end.UtcDateTime))
                        continue;

                    var reportFile = Path.Combine(reportDirectory.FullName, "Report.wer");
                    if (!File.Exists(reportFile))
                        continue;

                    var fields = ParseWerReport(reportFile);
                    fields.TryGetValue("EventType", out var eventType);
                    fields.TryGetValue("AppName", out var appName);
                    fields.TryGetValue("FriendlyEventName", out var friendlyName);

                    var detail = appName ?? friendlyName;
                    results.Add(new ExternalEvidenceFile(
                        "WER",
                        reportDirectory.FullName,
                        reportDirectory.LastWriteTime,
                        DirectorySize(reportDirectory),
                        eventType,
                        detail));
                }
                catch
                {
                    // Unreadable report directory: skip it.
                }
            }
        }
    }

    private static void ScanDumpDirectories(List<ExternalEvidenceFile> results, DateTimeOffset start, DateTimeOffset end)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        ScanDumps(results, "LiveKernelReport", Path.Combine(windows, "LiveKernelReports"), start, end);
        ScanDumps(results, "Minidump", Path.Combine(windows, "Minidump"), start, end);

        var memoryDump = Path.Combine(windows, "MEMORY.DMP");
        if (File.Exists(memoryDump))
        {
            var info = new FileInfo(memoryDump);
            if (IsInRange(info.LastWriteTimeUtc, start.UtcDateTime, end.UtcDateTime))
                results.Add(new ExternalEvidenceFile("MemoryDump", memoryDump, info.LastWriteTime, info.Length, "KernelCrash", null));
        }
    }

    private static void ScanDumps(
        List<ExternalEvidenceFile> results,
        string kind,
        string directory,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in SafeEnumerateFiles(directory, "*.dmp", SearchOption.TopDirectoryOnly))
            TryAddDump(results, kind, file, start, end, subtype: null);

        // One guarded pass per subdirectory so a single access-denied folder
        // cannot discard the rest of the tree (LiveKernelReports nests by subtype).
        foreach (var subdirectory in SafeEnumerateDirectories(directory))
            foreach (var file in SafeEnumerateFiles(subdirectory.FullName, "*.dmp", SearchOption.AllDirectories))
                TryAddDump(results, kind, file, start, end, subdirectory.Name);
    }

    private static void TryAddDump(
        List<ExternalEvidenceFile> results,
        string kind,
        FileInfo file,
        DateTimeOffset start,
        DateTimeOffset end,
        string? subtype)
    {
        try
        {
            if (!IsInRange(file.LastWriteTimeUtc, start.UtcDateTime, end.UtcDateTime))
                return;

            results.Add(new ExternalEvidenceFile(
                kind,
                file.FullName,
                file.LastWriteTime,
                file.Length,
                kind == "LiveKernelReport" ? "LiveKernelEvent" : "KernelCrash",
                subtype));
        }
        catch
        {
            // File vanished or is locked: skip it.
        }
    }

    private static Dictionary<string, string> ParseWerReport(string path)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch
        {
            return fields;
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0 || value.Length > 128 || value.Any(char.IsControl))
                continue;

            fields.TryAdd(key, value);
        }

        return fields;
    }

    private static bool IsInRange(DateTime utcTimestamp, DateTime startUtc, DateTime endUtc)
        => utcTimestamp >= startUtc && utcTimestamp <= endUtc;

    private static long DirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateDirectories().ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<FileInfo> SafeEnumerateFiles(string path, string pattern, SearchOption option)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFiles(pattern, option).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
