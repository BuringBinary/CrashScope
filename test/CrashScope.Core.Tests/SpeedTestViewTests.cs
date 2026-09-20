using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public class SpeedTestViewTests
{
    [Fact]
    public void FormatLatency_BasicAndInvalid()
    {
        Assert.Equal("23 ms", SpeedTestView.FormatLatency(23.4));
        Assert.Equal("--", SpeedTestView.FormatLatency(null));
        Assert.Equal("--", SpeedTestView.FormatLatency(double.NaN));
        Assert.Equal("--", SpeedTestView.FormatLatency(-1));
    }

    [Fact]
    public void Average_EmptyIsNull()
    {
        Assert.Null(SpeedTestView.Average([]));
        Assert.Equal(20.0, SpeedTestView.Average([10, 20, 30]));
    }

    [Fact]
    public void Jitter_NeedsTwoSamples()
    {
        Assert.Null(SpeedTestView.Jitter([]));
        Assert.Null(SpeedTestView.Jitter([10]));
        Assert.Equal(10.0, SpeedTestView.Jitter([10, 20, 30]));
        Assert.Equal(2.0, SpeedTestView.Jitter([10, 10, 14]));
    }

    [Fact]
    public void ToMbps_UsesDecimalMegabit()
        => Assert.Equal(10.0, SpeedTestView.ToMbps(1_250_000));

    [Fact]
    public void ToMbps_NullStaysNull()
        => Assert.Null(SpeedTestView.ToMbps(null));

    [Fact]
    public void FormatMbps_BasicAndInvalid()
    {
        Assert.Equal("93.9 Mbps", SpeedTestView.FormatMbps(93.86));
        Assert.Equal("--", SpeedTestView.FormatMbps(null));
        Assert.Equal("--", SpeedTestView.FormatMbps(double.PositiveInfinity));
        Assert.Equal("--", SpeedTestView.FormatMbps(-0.1));
    }

    [Theory]
    [InlineData(100, "高带宽：可支撑 4K 流媒体与大文件同步")]
    [InlineData(30, "中高带宽：可支撑 1440p 流媒体")]
    [InlineData(15, "中等带宽：可支撑 1080p 流媒体")]
    [InlineData(7, "基础带宽：网页与标清视频可用")]
    [InlineData(2, "低带宽：大文件与高清流媒体会缓慢")]
    public void Verdict_FollowsThresholds(double mbps, string expected)
        => Assert.Equal(expected, SpeedTestView.Verdict(mbps));

    [Fact]
    public void Verdict_IncompleteIsNotRated()
        => Assert.Equal("无法评估（下载测速未完成）", SpeedTestView.Verdict(null));
}
