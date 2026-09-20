namespace CrashScope.Core.Models;

/// <summary>Pure formatting and statistics for the speed test page; no I/O here.</summary>
public static class SpeedTestView
{
    public static string FormatLatency(double? milliseconds)
        => milliseconds.HasValue && IsFinite(milliseconds.Value) && milliseconds.Value >= 0
            ? $"{milliseconds.Value:F0} ms"
            : "--";

    public static double? Average(IReadOnlyList<double> samples)
        => samples.Count == 0 ? null : samples.Average();

    /// <summary>Mean absolute difference between consecutive samples; needs at least two.</summary>
    public static double? Jitter(IReadOnlyList<double> samples)
    {
        if (samples.Count < 2) return null;
        double sum = 0;
        for (var i = 1; i < samples.Count; i++)
            sum += Math.Abs(samples[i] - samples[i - 1]);
        return sum / (samples.Count - 1);
    }

    /// <summary>Bytes per second → megabits per second (decimal 1 Mbps = 1,000,000 bit/s).</summary>
    public static double? ToMbps(double? bytesPerSecond)
        => bytesPerSecond.HasValue ? bytesPerSecond.Value * 8 / 1_000_000d : null;

    public static string FormatMbps(double? mbps)
        => mbps.HasValue && IsFinite(mbps.Value) && mbps.Value >= 0
            ? $"{mbps.Value:F1} Mbps"
            : "--";

    /// <summary>Informational capacity label derived from measured download only.</summary>
    public static string Verdict(double? downMbps)
    {
        if (!downMbps.HasValue || !IsFinite(downMbps.Value) || downMbps.Value < 0)
            return "无法评估（下载测速未完成）";
        return downMbps.Value switch
        {
            >= 50 => "高带宽：可支撑 4K 流媒体与大文件同步",
            >= 25 => "中高带宽：可支撑 1440p 流媒体",
            >= 10 => "中等带宽：可支撑 1080p 流媒体",
            >= 5 => "基础带宽：网页与标清视频可用",
            _ => "低带宽：大文件与高清流媒体会缓慢"
        };
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
