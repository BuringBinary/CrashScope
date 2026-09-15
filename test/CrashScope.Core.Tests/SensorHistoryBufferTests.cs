using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class SensorHistoryBufferTests
{
    [Fact]
    public void Constructor_DefaultMaxPoints_Is300()
    {
        var buffer = new SensorHistoryBuffer();
        var history = buffer.GetHistory();
        Assert.Empty(history);
    }

    [Fact]
    public void Push_AddsSnapshot()
    {
        var buffer = new SensorHistoryBuffer();
        buffer.Push([1.0f, 2.0f, 3.0f]);

        var history = buffer.GetHistory();
        Assert.Single(history);
        Assert.Equal(3, history[0].Values.Count);
        Assert.Equal(1.0f, history[0].Values[0]);
        Assert.Null(history[0].Values[3]); // out of range: F0 for null
    }

    [Fact]
    public void Push_RespectsMaxPoints()
    {
        var buffer = new SensorHistoryBuffer(maxPoints: 3);

        for (var i = 0; i < 10; i++)
            buffer.Push([i]);

        var history = buffer.GetHistory();
        Assert.Equal(3, history.Count);
        Assert.Equal(7f, history[0].Values[0]);
        Assert.Equal(9f, history[2].Values[0]);
    }

    [Fact]
    public void GetSeries_EmptyHistory_ReturnsEmpty()
    {
        var buffer = new SensorHistoryBuffer();
        var series = buffer.GetSeries(0);

        Assert.Empty(series);
    }

    [Fact]
    public void GetSeries_BasicValues_ReturnsCorrectPoints()
    {
        var buffer = new SensorHistoryBuffer();

        buffer.Push([10f, 20f]);
        buffer.Push([15f, 25f]);
        buffer.Push([20f, 30f]);

        var series0 = buffer.GetSeries(0);
        Assert.Equal(3, series0.Count);
        Assert.Equal(0, series0[0].X); // relative time starts at 0
        Assert.Equal(10, series0[0].Y);

        var series1 = buffer.GetSeries(1);
        Assert.Equal(3, series1.Count);
        Assert.Equal(20, series1[0].Y);
    }

    [Fact]
    public void GetSeries_SkipsNullValues()
    {
        var buffer = new SensorHistoryBuffer();

        buffer.Push([null]);
        buffer.Push([5f]);
        buffer.Push([null]);

        var series = buffer.GetSeries(0);
        Assert.Single(series);
        Assert.Equal(5, series[0].Y);
    }

    [Fact]
    public void GetSeries_RisingTrend_PointsAreOrdered()
    {
        var buffer = new SensorHistoryBuffer();

        for (var i = 0; i < 10; i++)
            buffer.Push([i * 10f]);

        var series = buffer.GetSeries(0);

        for (var i = 1; i < series.Count; i++)
        {
            Assert.True(series[i].X >= series[i - 1].X);
            Assert.True(series[i].Y > series[i - 1].Y);
        }
    }
}