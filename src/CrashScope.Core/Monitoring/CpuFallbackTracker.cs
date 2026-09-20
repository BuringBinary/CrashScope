using System.Diagnostics;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

/// <summary>Windows PDH fallback for total CPU load when hardware sensors expose no usable load.</summary>
public sealed class CpuFallbackTracker : IDisposable
{
    private readonly PerformanceCounter? _counter;
    private readonly Func<float?>? _capture;
    private bool _disposed;

    public bool IsAvailable { get; }
    public string HealthStatus { get; private set; }
    public string HealthDetail { get; private set; }

    public CpuFallbackTracker()
    {
        try
        {
            _counter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _counter.NextValue();
            IsAvailable = true;
            HealthStatus = "WarmingUp";
            HealthDetail = "Windows Processor(_Total) PDH counter initialized; waiting for the second sample.";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            HealthStatus = "Unavailable";
            HealthDetail = $"Windows total CPU counter unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    internal CpuFallbackTracker(Func<float?> capture)
    {
        _capture = capture;
        IsAvailable = true;
        HealthStatus = "WarmingUp";
        HealthDetail = "Injected CPU fallback awaiting sample.";
    }

    public CpuFallbackSample Capture(DateTimeOffset now)
    {
        if (_disposed || !IsAvailable)
            return new CpuFallbackSample(now, null, "WindowsPdhProcessorTotal", "Unavailable", HealthDetail);
        try
        {
            var raw = _capture is null ? _counter!.NextValue() : _capture();
            if (!raw.HasValue || !float.IsFinite(raw.Value))
            {
                HealthStatus = "WarmingUp";
                HealthDetail = "Windows total CPU counter has not produced a finite value yet.";
                return new CpuFallbackSample(now, null, "WindowsPdhProcessorTotal", HealthStatus);
            }
            var value = Math.Clamp(raw.Value, 0f, 100f);
            HealthStatus = "Collecting";
            HealthDetail = $"Windows Processor(_Total) PDH utilization={value:F1}%.";
            return new CpuFallbackSample(now, value, "WindowsPdhProcessorTotal", HealthStatus);
        }
        catch (Exception ex)
        {
            HealthStatus = "Degraded";
            HealthDetail = $"Windows total CPU counter read failed: {ex.GetType().Name}: {ex.Message}";
            return new CpuFallbackSample(now, null, "WindowsPdhProcessorTotal", HealthStatus, HealthDetail);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _counter?.Dispose();
    }
}
