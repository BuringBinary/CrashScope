using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CrashScope.Core.Monitoring;

internal sealed record GpuCounterInstance(
    int ProcessId,
    string? AdapterLuid,
    int? PhysicalAdapterIndex,
    int? EngineIndex,
    string? EngineType);

internal static partial class GpuCounterInstanceParser
{
    public static bool TryParse(string instance, out GpuCounterInstance result)
    {
        result = default!;
        var pidMatch = PidPattern().Match(instance);
        if (!pidMatch.Success || !int.TryParse(pidMatch.Groups[1].Value, out var pid))
            return false;

        string? luid = null;
        var luidMatch = LuidPattern().Match(instance);
        if (luidMatch.Success
            && uint.TryParse(luidMatch.Groups[1].Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
            && uint.TryParse(luidMatch.Groups[2].Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low))
            luid = FormatLuid(high, low);

        int? physical = TryGroupInt(PhysicalPattern().Match(instance));
        int? engine = TryGroupInt(EnginePattern().Match(instance));
        var typeMatch = EngineTypePattern().Match(instance);
        var engineType = typeMatch.Success ? typeMatch.Groups[1].Value : null;
        result = new GpuCounterInstance(pid, luid, physical, engine, engineType);
        return true;
    }

    public static string FormatLuid(uint high, uint low) => $"0x{high:X8}_0x{low:X8}";

    private static int? TryGroupInt(Match match)
        => match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;

    [GeneratedRegex(@"^pid_(\d+)(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PidPattern();
    [GeneratedRegex(@"_luid_(0x[0-9a-f]+)_(0x[0-9a-f]+)(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LuidPattern();
    [GeneratedRegex(@"_phys_(\d+)(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PhysicalPattern();
    [GeneratedRegex(@"_eng_(\d+)(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex EnginePattern();
    [GeneratedRegex(@"_engtype_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EngineTypePattern();
}

internal sealed class GpuAdapterIdentityResolver
{
    private readonly IReadOnlyDictionary<string, string> _names;

    public GpuAdapterIdentityResolver() : this(CaptureNames()) { }
    internal GpuAdapterIdentityResolver(IReadOnlyDictionary<string, string> names) => _names = names;
    public string? Resolve(string? luid) => luid is not null && _names.TryGetValue(luid, out var name) ? name : null;

    private static IReadOnlyDictionary<string, string> CaptureNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
            if (root is null) return result;
            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(subKeyName);
                var description = key?.GetValue("Description") as string;
                if (string.IsNullOrWhiteSpace(description) || !TryReadLuid(key?.GetValue("AdapterLuid"), out var luid))
                    continue;
                result[luid] = description;
            }
        }
        catch { }
        return result;
    }

    private static bool TryReadLuid(object? value, out string luid)
    {
        ulong raw;
        switch (value)
        {
            case long signed: raw = unchecked((ulong)signed); break;
            case ulong unsigned: raw = unsigned; break;
            case int signed32: raw = unchecked((uint)signed32); break;
            case uint unsigned32: raw = unsigned32; break;
            case byte[] bytes when bytes.Length >= 8: raw = BitConverter.ToUInt64(bytes, 0); break;
            default: luid = ""; return false;
        }
        luid = GpuCounterInstanceParser.FormatLuid((uint)(raw >> 32), (uint)raw);
        return true;
    }
}
