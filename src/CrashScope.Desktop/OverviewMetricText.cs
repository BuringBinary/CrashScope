using System.Globalization;

namespace CrashScope.Desktop;

/// <summary>Missing/stopped/invalid data must never appear as a healthy zero.</summary>
public static class OverviewMetricText
{
    public static string Format(IReadOnlyList<float?>? values, int index, bool recording, string unit)
    {
        if (!recording || values is null || index < 0 || index >= values.Count
            || values[index] is not { } value || !float.IsFinite(value) || value < 0
            || (unit == "%" && value > 100)) return "—";
        return value.ToString(unit == "%" ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + unit;
    }
}
