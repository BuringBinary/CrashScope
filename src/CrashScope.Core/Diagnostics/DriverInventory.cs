using CrashScope.Core.Models;
using Microsoft.Win32;

namespace CrashScope.Core.Diagnostics;

public static class DriverInventory
{
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static IReadOnlyList<DriverInventoryEntry> Capture()
    {
        var results = new List<DriverInventoryEntry>();
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null)
                return results;

            foreach (var subKeyName in classKey.GetSubKeyNames()
                         .Where(name => name.Length == 4 && name.All(char.IsDigit))
                         .Order())
            {
                using var instanceKey = classKey.OpenSubKey(subKeyName);
                if (instanceKey is null)
                    continue;

                var description = instanceKey.GetValue("DriverDesc") as string;
                if (string.IsNullOrWhiteSpace(description))
                    continue;

                var matchingId = instanceKey.GetValue("MatchingDeviceId") as string;
                var version = instanceKey.GetValue("DriverVersion") as string;
                var date = ParseDriverDate(instanceKey.GetValue("DriverDate") as byte[]);

                results.Add(new DriverInventoryEntry(
                    description,
                    matchingId,
                    string.IsNullOrWhiteSpace(version) ? null : version,
                    date,
                    DetectVendor(description, matchingId)));
            }
        }
        catch
        {
            // Inventory is best-effort; the analysis continues without it.
        }

        return results;
    }

    private static DateTimeOffset? ParseDriverDate(byte[]? raw)
    {
        if (raw is null || raw.Length < 8)
            return null;

        try
        {
            var fileTime = BitConverter.ToInt64(raw, 0);
            return fileTime > 0 ? DateTimeOffset.FromFileTime(fileTime) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string DetectVendor(string? description, string? matchingDeviceId)
    {
        var haystack = $"{description} {matchingDeviceId}";
        if (haystack.Contains("ven_1002", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("amd", StringComparison.OrdinalIgnoreCase))
            return "AMD";

        if (haystack.Contains("ven_10de", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("geforce", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";

        if (haystack.Contains("ven_8086", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";

        return "Other";
    }
}
