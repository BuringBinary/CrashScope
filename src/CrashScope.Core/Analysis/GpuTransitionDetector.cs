using System.Globalization;
using CrashScope.Core.Models;

namespace CrashScope.Core.Analysis;

public static class GpuTransitionDetector
{
    private sealed record Threshold(float Absolute, float Relative);

    private static readonly Dictionary<string, Threshold> Thresholds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Load"] = new(25f, 0.30f),
        ["Clock"] = new(150f, 0.20f),
        ["Power"] = new(15f, 0.50f),
        ["Temperature"] = new(8f, 0f),
        ["SmallData"] = new(0.8f, 0.30f),
        ["Data"] = new(0.8f, 0.30f)
    };

    private static readonly Dictionary<string, string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Load"] = "%",
        ["Clock"] = "MHz",
        ["Power"] = "W",
        ["Temperature"] = "°C",
        ["SmallData"] = "GB",
        ["Data"] = "GB"
    };

    public static IReadOnlyList<SensorTransition> Detect(
        IReadOnlyList<SensorCatalogEntry> catalog,
        IReadOnlyList<TelemetrySnapshot> telemetry)
    {
        var transitions = new List<SensorTransition>();
        if (catalog.Count == 0 || telemetry.Count < 10)
            return transitions;

        foreach (var entry in catalog)
        {
            if (!entry.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Thresholds.TryGetValue(entry.SensorType, out var threshold))
                continue;
            if (entry.Id >= telemetry.Max(t => t.SensorValues.Count))
                continue;

            var series = telemetry
                .Select(sample => (sample.Timestamp, Value: sample.SensorValues[entry.Id]))
                .Where(point => point.Value.HasValue)
                .Select(point => (point.Timestamp, Value: point.Value!.Value))
                .ToList();
            if (series.Count < 10)
                continue;

            var steps = DetectSteps(entry, series, threshold);
            if (steps.Count > 0)
            {
                transitions.AddRange(steps);
            }
            else
            {
                var trend = DetectTrend(entry, series, threshold);
                if (trend is not null)
                    transitions.Add(trend);
            }
        }

        return transitions.OrderBy(t => t.Timestamp).ToList();
    }

    private static List<SensorTransition> DetectSteps(
        SensorCatalogEntry entry,
        IReadOnlyList<(DateTimeOffset Time, float Value)> series,
        Threshold threshold)
    {
        var result = new List<SensorTransition>();
        var i = 0;
        while (i < series.Count - 1)
        {
            var bestIndex = -1;
            var bestDelta = 0f;
            for (var j = i + 1; j < series.Count; j++)
            {
                if ((series[j].Time - series[i].Time).TotalSeconds > 5)
                    break;

                var delta = Math.Abs(series[j].Value - series[i].Value);
                if (delta > bestDelta)
                {
                    bestDelta = delta;
                    bestIndex = j;
                }
            }

            var limit = Math.Max(threshold.Absolute, threshold.Relative * Math.Abs(series[i].Value));
            if (bestIndex > i && bestDelta >= limit)
            {
                var from = series[i];
                var to = series[bestIndex];
                var elapsed = Math.Max(1.0, (to.Time - from.Time).TotalSeconds);
                var unit = Units.GetValueOrDefault(entry.SensorType, "");
                result.Add(new SensorTransition(
                    to.Time,
                    entry.HardwareName,
                    entry.SensorName,
                    entry.SensorType,
                    from.Value,
                    to.Value,
                    elapsed,
                    IsTrend: false,
                    Description: $"{entry.HardwareName} {entry.SensorName}: {Format(from.Value)}{unit} -> {Format(to.Value)}{unit} in {elapsed.ToString("0.#", CultureInfo.InvariantCulture)}s"));
                i = bestIndex;
            }
            else
            {
                i++;
            }
        }

        return result;
    }

    private static SensorTransition? DetectTrend(
        SensorCatalogEntry entry,
        IReadOnlyList<(DateTimeOffset Time, float Value)> series,
        Threshold threshold)
    {
        if ((series[^1].Time - series[0].Time).TotalSeconds < 60)
            return null;

        var windowSize = Math.Max(5, series.Count / 3);
        var headMean = (float)series.Take(windowSize).Average(point => point.Value);
        var tailMean = (float)series.Skip(series.Count - windowSize).Average(point => point.Value);
        var limit = Math.Max(threshold.Absolute, threshold.Relative * Math.Abs(headMean));
        if (Math.Abs(tailMean - headMean) < limit)
            return null;

        var elapsed = (series[^1].Time - series[0].Time).TotalSeconds;
        var unit = Units.GetValueOrDefault(entry.SensorType, "");
        return new SensorTransition(
            series[0].Time,
            entry.HardwareName,
            entry.SensorName,
            entry.SensorType,
            headMean,
            tailMean,
            elapsed,
            IsTrend: true,
            Description: $"{entry.HardwareName} {entry.SensorName}: gradual change from ~{Format(headMean)}{unit} to ~{Format(tailMean)}{unit} over {elapsed.ToString("0.#", CultureInfo.InvariantCulture)}s");
    }

    private static string Format(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
