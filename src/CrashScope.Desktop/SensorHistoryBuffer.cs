using CrashScope.Core.Models;
using System.Collections.ObjectModel;

namespace CrashScope.Desktop;

public sealed class SensorHistoryBuffer
{
    private readonly int _maxPoints;
    private readonly List<SensorSnapshot> _history = new();
    private readonly object _lock = new();

    public SensorHistoryBuffer(int maxPoints = 300)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);
        _maxPoints = maxPoints;
    }

    public void Push(IReadOnlyList<float?> sensorValues)
    {
        lock (_lock)
        {
            _history.Add(new SensorSnapshot(DateTimeOffset.Now, sensorValues.ToArray()));
            while (_history.Count > _maxPoints)
                _history.RemoveAt(0);
        }
    }

    public void Clear()
    {
        lock (_lock) _history.Clear();
    }

    public IReadOnlyList<SensorSnapshot> GetHistory()
    {
        lock (_lock)
        {
            return _history.ToList();
        }
    }

    public IReadOnlyList<(double X, double Y)> GetSeries(int sensorIndex, bool relativeTime = true)
    {
        lock (_lock)
        {
            if (sensorIndex < 0 || _history.Count == 0) return Array.Empty<(double, double)>();

            var baseTime = relativeTime ? _history[0].Timestamp : DateTimeOffset.MinValue;
            var result = new List<(double, double)>(_history.Count);

            foreach (var snapshot in _history)
            {
                if (sensorIndex < snapshot.Values.Count)
                {
                    var value = snapshot.Values[sensorIndex];
                    if (value.HasValue && float.IsFinite(value.Value))
                    {
                        result.Add((
                            (snapshot.Timestamp - baseTime).TotalSeconds,
                            value.Value));
                    }
                }
            }

            return result;
        }
    }
}

public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<float?> Values);