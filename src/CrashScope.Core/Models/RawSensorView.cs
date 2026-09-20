namespace CrashScope.Core.Models;

/// <summary>A display-ready row for the raw sensor views.</summary>
public sealed record RawSensorRow(
    int Id,
    string Group,
    string Sensor,
    string SensorType,
    string ValueText,
    bool HasValue);

/// <summary>
/// Pure presentation logic shared by the hardware overview embedded list and the
/// raw sensor window. Values are indexed by catalog entry Id — never by position
/// in a filtered list — so a filter can never attach another sensor's value to a row.
/// </summary>
public static class RawSensorView
{
    public static IReadOnlyList<RawSensorRow> Build(
        IReadOnlyList<SensorCatalogEntry> catalog,
        IReadOnlyList<float?>? values,
        string? searchText = null,
        string? hardwareType = null)
    {
        List<RawSensorRow> rows = new();
        if (catalog.Count == 0) return rows;

        foreach (var entry in catalog)
        {
            if (!MatchesSearch(entry, searchText)) continue;
            if (!MatchesType(entry, hardwareType)) continue;

            float? value = null;
            if (values is not null && entry.Id >= 0 && entry.Id < values.Count)
                value = values[entry.Id];

            var hasDisplayableValue = value.HasValue && IsFinite(value.Value);

            rows.Add(new RawSensorRow(
                entry.Id,
                entry.HardwareName,
                entry.SensorName,
                entry.SensorType,
                FormatValue(entry.SensorType, value),
                hasDisplayableValue));
        }

        return rows;
    }

    public static IReadOnlyList<string> ListHardwareTypes(IReadOnlyList<SensorCatalogEntry> catalog)
    {
        SortedSet<string> types = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
            types.Add(entry.HardwareType);
        return types.ToList();
    }

    /// <summary>Display-only unit hints derived from the sensor type; unknown types stay unit-free.</summary>
    public static string FormatValue(string sensorType, float? value)
    {
        if (!value.HasValue || !IsFinite(value.Value)) return "--";
        return $"{value.Value:F1}{UnitFor(sensorType)}";
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool MatchesSearch(SensorCatalogEntry entry, string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return true;
        return entry.HardwareType.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || entry.HardwareName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || entry.SensorName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || entry.SensorType.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesType(SensorCatalogEntry entry, string? hardwareType)
        => string.IsNullOrWhiteSpace(hardwareType)
           || hardwareType == "All"
           || string.Equals(entry.HardwareType, hardwareType, StringComparison.OrdinalIgnoreCase);

    private static string UnitFor(string sensorType) => sensorType switch
    {
        "Temperature" => " °C",
        "Load" or "Control" => " %",
        "Power" => " W",
        "Clock" => " MHz",
        "Voltage" => " V",
        "Fan" => " RPM",
        "Data" => " GB",
        "SmallData" => " MB",
        _ => ""
    };
}
