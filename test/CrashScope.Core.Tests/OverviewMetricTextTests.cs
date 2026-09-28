using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public class OverviewMetricTextTests
{
    [Fact] public void StoppedDoesNotShowStaleSample() => Assert.Equal("—", OverviewMetricText.Format(new float?[] { 72 }, 0, false, "%"));
    [Fact] public void MissingCollectionIsNotZero() => Assert.Equal("—", OverviewMetricText.Format(null, 0, true, "%"));
    [Theory]
    [InlineData(-1)] [InlineData(1)] [InlineData(100)]
    public void MissingIndexIsSafe(int index) => Assert.Equal("—", OverviewMetricText.Format(new float?[] { 50 }, index, true, "%"));
    [Theory]
    [InlineData(null)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1f)] [InlineData(101f)]
    public void InvalidPercentageIsNotHealthy(float? value) => Assert.Equal("—", OverviewMetricText.Format(new[] { value }, 0, true, "%"));
    [Theory] [InlineData(0f, "0 %")] [InlineData(100f, "100 %")]
    public void ValidEndpointsAreRealData(float value, string expected) => Assert.Equal(expected, OverviewMetricText.Format(new float?[] { value }, 0, true, "%"));
    [Fact] public void MemoryIsNotClampedToPercentage() => Assert.NotEqual("—", OverviewMetricText.Format(new float?[] { 128 }, 0, true, "GB"));
}
