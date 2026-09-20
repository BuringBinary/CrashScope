namespace CrashScope.Core.Monitoring;

/// <summary>Safety-sensitive recorder options. Expensive GPU kernel ETW is opt-in.</summary>
public sealed record RecorderOptions(
    bool EnableDxgKrnlEtw = false,
    bool EnableSystemEtw = true,
    float CommitWarningPercent = 90f,
    float CommitThrottlePercent = 95f,
    float CommitCriticalPercent = 98f)
{
    internal void Validate()
    {
        if (!float.IsFinite(CommitWarningPercent) || !float.IsFinite(CommitThrottlePercent)
            || !float.IsFinite(CommitCriticalPercent) || CommitWarningPercent < 0
            || CommitWarningPercent >= CommitThrottlePercent || CommitThrottlePercent >= CommitCriticalPercent
            || CommitCriticalPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(CommitWarningPercent),
                "Commit thresholds must satisfy 0 <= warning < throttle < critical <= 100.");
    }
}

internal enum CommitPressureLevel { Normal, Warning, Throttled, Critical }

internal static class CommitPressurePolicy
{
    public static CommitPressureLevel Classify(float percent, RecorderOptions options)
    {
        if (!float.IsFinite(percent) || percent < 0) return CommitPressureLevel.Normal;
        if (percent >= options.CommitCriticalPercent) return CommitPressureLevel.Critical;
        if (percent >= options.CommitThrottlePercent) return CommitPressureLevel.Throttled;
        if (percent >= options.CommitWarningPercent) return CommitPressureLevel.Warning;
        return CommitPressureLevel.Normal;
    }
}
