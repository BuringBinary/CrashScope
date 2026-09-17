

using System.Runtime.InteropServices;

namespace CrashScope.Core.Actions;

public sealed record PowerPlanInfo(string Guid, string Name, bool IsActive);

/// <summary>Read/apply surface for Windows power plans, replaceable in tests.</summary>
public interface IPowerPlanService
{
    IReadOnlyList<PowerPlanInfo> GetPlans();

    /// <summary>Active scheme guid, or null when the API is unavailable.</summary>
    string? GetActive();

    bool SetActive(string guid);
}

/// <summary>
/// powrprof.dll implementation (user power schemes; no elevation required to
/// switch). Every failure degrades to null/false/empty rather than throwing, so
/// SystemActions can journal a clean failure record.
/// </summary>
public sealed class PowerPlanService : IPowerPlanService
{
    private const uint AccessScheme = 16;
    private const int ErrorSuccess = 0;
    private const int ErrorNoMoreItems = 259;

    public IReadOnlyList<PowerPlanInfo> GetPlans()
    {
        var result = new List<PowerPlanInfo>();
        try
        {
            var active = GetActive();
            for (uint index = 0; ; index++)
            {
                var guid = Native.EnumerateScheme(AccessScheme, index);
                if (guid is null)
                    break;
                var text = guid.Value.ToString();
                result.Add(new PowerPlanInfo(text, Native.ReadFriendlyName(guid.Value), active == text));
            }
        }
        catch
        {
            // Partial list beats an exception; callers treat empty as "unavailable".
        }

        return result;
    }

    public string? GetActive()
    {
        try
        {
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != ErrorSuccess)
                return null;
            try
            {
                return Marshal.PtrToStructure<Guid>(ptr).ToString();
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return null;
        }
    }

    public bool SetActive(string guid)
    {
        if (!Guid.TryParse(guid, out var scheme))
            return false;
        try
        {
            return Native.PowerSetActiveScheme(IntPtr.Zero, scheme) == ErrorSuccess;
        }
        catch
        {
            return false;
        }
    }

    private static class Native
    {
        [DllImport("powrprof.dll")]
        internal static extern int PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyKey);

        [DllImport("powrprof.dll")]
        internal static extern int PowerSetActiveScheme(IntPtr UserRootPowerKey, Guid SchemeKey);

        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
        private static extern int PowerReadFriendlyName(
            IntPtr RootPowerKey, Guid SchemeGuid, IntPtr SubGroupOfPowerSettingGuid,
            IntPtr PowerSettingGuid, IntPtr Buffer, ref uint BufferSize);

        [DllImport("powrprof.dll")]
        private static extern int PowerEnumerate(
            IntPtr RootPowerKey, IntPtr SchemeGuid, IntPtr SubGroupOfPowerSettingGuid,
            uint AccessIndex, uint Index, IntPtr Buffer, ref uint BufferSize);

        internal static Guid? EnumerateScheme(uint access, uint index)
        {
            var size = (uint)16;
            var buffer = Marshal.AllocHGlobal(16);
            try
            {
                var error = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, access, index, buffer, ref size);
                if (error != ErrorSuccess || size < 16)
                    return null; // includes ERROR_NO_MORE_ITEMS and transient errors
                return Marshal.PtrToStructure<Guid>(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        internal static string ReadFriendlyName(Guid guid)
        {
            try
            {
                var size = 0u;
                PowerReadFriendlyName(IntPtr.Zero, guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
                if (size == 0)
                    return string.Empty;

                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    return PowerReadFriendlyName(IntPtr.Zero, guid, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == ErrorSuccess
                        ? Marshal.PtrToStringUni(buffer) ?? string.Empty
                        : string.Empty;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
