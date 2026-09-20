using CrashScope.Core.Models;
using Xunit;

namespace CrashScope.Core.Tests;

public class FloatingBarViewTests
{
    [Fact]
    public void NetSpeed_NormalDelta()
        => Assert.Equal(500.0, FloatingBarView.NextBytesPerSecond(2000, 1000, 2.0));

    [Fact]
    public void NetSpeed_FirstSample_ReturnsNull()
        => Assert.Null(FloatingBarView.NextBytesPerSecond(1000, null, 2.0));

    [Fact]
    public void NetSpeed_CounterReset_ReturnsZero_NotNegative()
        => Assert.Equal(0.0, FloatingBarView.NextBytesPerSecond(1000, 2000, 2.0));

    [Fact]
    public void NetSpeed_NonPositiveInterval_ReturnsNull()
        => Assert.Null(FloatingBarView.NextBytesPerSecond(2000, 1000, 0));

    [Fact]
    public void FormatSpeed_AdaptiveUnits()
    {
        Assert.Equal("--", FloatingBarView.FormatSpeed(null));
        Assert.Equal("0 B/s", FloatingBarView.FormatSpeed(0));
        Assert.Equal("1023 B/s", FloatingBarView.FormatSpeed(1023.4));
        Assert.Equal("1.50 KB/s", FloatingBarView.FormatSpeed(1536));
        Assert.Equal("1.25 MB/s", FloatingBarView.FormatSpeed(1310720));
        Assert.Equal("1.50 GB/s", FloatingBarView.FormatSpeed(1610612736));
        Assert.Equal("--", FloatingBarView.FormatSpeed(-5));
    }

    [Fact]
    public void Build_FullLine()
    {
        var line = FloatingBarView.Build(12.4f, 5f, 44f, 8.2f, 23.8f, 1310720, 307200);
        Assert.Equal("CPU 12%  ·  GPU 5% 44°C  ·  RAM 8.2/32.0 GB  ·  ↓ 1.25 MB/s  ↑ 300.00 KB/s", line);
    }

    [Fact]
    public void Build_AllMissing_ShowsDashes()
        => Assert.Equal("CPU --  ·  GPU --  ·  RAM --  ·  ↓ --  ↑ --",
            FloatingBarView.Build(null, null, null, null, null, null, null));

    [Fact]
    public void Build_GpuSegmentsAppearIndependently()
    {
        Assert.Contains("GPU 5%", FloatingBarView.Build(null, 5f, null, null, null, null, null));
        Assert.Contains("GPU -- 44°C", FloatingBarView.Build(null, null, 44f, null, null, null, null));
    }

    [Fact]
    public void Build_NaNValues_AreMissing()
    {
        var line = FloatingBarView.Build(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, double.NaN, double.NaN);
        Assert.Equal("CPU --  ·  GPU --  ·  RAM --  ·  ↓ --  ↑ --", line);
    }
}
