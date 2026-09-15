using System.Globalization;

namespace CrashScope.Desktop;

public sealed class GpuTransitionMonitor
{
    private readonly int _gpuLoadIdx;
    private readonly int _gpuClockIdx;
    private readonly int _gpuPowerIdx;
    private readonly string _gpuName;

    private readonly List<GpuTransition> _recentTransitions = new();
    private const int MaxTransitions = 50;
    private const int MinWindowSamples = 10;
    private const int WindowSamples = 60;

    private static readonly (string SensorType, float Absolute, float Relative)[] Thresholds =
    [
        ("Load", 25f, 0.30f),
        ("Clock", 150f, 0.20f),
        ("Power", 15f, 0.50f)
    ];

    private static readonly Dictionary<string, string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Load"] = "%",
        ["Clock"] = "MHz",
        ["Power"] = "W"
    };

    public event Action<GpuTransition>? TransitionDetected;

    public GpuTransitionMonitor(int gpuLoadIdx, int gpuClockIdx, int gpuPowerIdx, string gpuName)
    {
        _gpuLoadIdx = gpuLoadIdx;
        _gpuClockIdx = gpuClockIdx;
        _gpuPowerIdx = gpuPowerIdx;
        _gpuName = gpuName;
    }

    public IReadOnlyList<GpuTransition> RecentTransitions => _recentTransitions;

    public void Check(IReadOnlyList<SensorSnapshot> history)
    {
        if (history.Count < MinWindowSamples)
            return;

        var window = history.Count > WindowSamples
            ? history.Skip(history.Count - WindowSamples).ToList()
            : history.ToList();

        CheckSensor(window, _gpuLoadIdx, "Load", "GPU Core Load");
        CheckSensor(window, _gpuClockIdx, "Clock", "GPU Core Clock");
        CheckSensor(window, _gpuPowerIdx, "Power", "GPU Package Power");
    }

    private void CheckSensor(
        IReadOnlyList<SensorSnapshot> window,
        int sensorIdx,
        string sensorType,
        string sensorName)
    {
        if (sensorIdx < 0) return;

        var series = window
            .Select(s => (s.Timestamp, Value: sensorIdx < s.Values.Count ? s.Values[sensorIdx] : null))
            .Where(p => p.Value.HasValue)
            .Select(p => (p.Timestamp, Value: p.Value!.Value))
            .ToList();

        if (series.Count < MinWindowSamples) return;

        var threshold = Thresholds.First(t => t.SensorType == sensorType);
        var limit = Math.Max(threshold.Absolute, threshold.Relative * Math.Abs(series[^1].Value));

        var headMean = (float)series.Take(MinWindowSamples).Average(p => p.Value);
        var tailMean = (float)series.Skip(series.Count - MinWindowSamples).Average(p => p.Value);
        var delta = tailMean - headMean;

        if (Math.Abs(delta) < limit) return;

        var isDrop = delta < 0;
        var elapsed = (series[^1].Timestamp - series[0].Timestamp).TotalSeconds;
        var unit = Units.GetValueOrDefault(sensorType, "");
        var direction = isDrop ? "drop" : "surge";

        var transition = new GpuTransition(
            series[^1].Timestamp,
            sensorName,
            sensorType,
            headMean,
            tailMean,
            elapsed,
            IsTrend: true,
            IsDrop: isDrop,
            Description: $"{_gpuName} {sensorName}: {direction} from ~{Format(headMean)}{unit} to ~{Format(tailMean)}{unit} over {elapsed:0.#}s");

        _recentTransitions.Add(transition);
        while (_recentTransitions.Count > MaxTransitions)
            _recentTransitions.RemoveAt(0);

        TransitionDetected?.Invoke(transition);
    }

    private static string Format(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}

public sealed record GpuTransition(
    DateTimeOffset Timestamp,
    string SensorName,
    string SensorType,
    float FromValue,
    float ToValue,
    double ElapsedSeconds,
    bool IsTrend,
    bool IsDrop,
    string Description);