using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public class IncidentPresentationTests
{
    private static IncidentInfo Row(string id = "opaque-id", string mode = "Unclassified", double confidence = 0) =>
        new(id, "not-accessed", new DateTimeOffset(2026, 9, 18, 1, 16, 0, TimeSpan.Zero), mode, confidence, Array.Empty<string>(), null!);
    private static string T(string key) => key switch
    {
        "eventUi.recordTitle" => "异常会话记录",
        "eventUi.lastRecord" => "最后记录：{0}",
        "eventUi.timeDetail" => "Last heartbeat, not failure time. {0}; original {1}",
        "eventUi.support" => "Support {0}, not probability",
        _ => key
    };

    [Fact] public void TitleDoesNotInferCauseFromDirectory() => Assert.Equal("异常会话记录", IncidentPresentation.Create(Row("recovered-gpu-crash"), "zh-CN", T).Title);
    [Fact] public void KeepsOriginalIdentity() { var row = Row(); var view = IncidentPresentation.Create(row, "zh-CN", T); Assert.Equal(row.DirectoryName, view.Id); Assert.Same(row, view.Incident); }
    [Theory]
    [InlineData("HardFreeze")] [InlineData("BlueScreen")] [InlineData("DriverTimeoutRecovery")]
    [InlineData("HardwareError")] [InlineData("AppCrash")] [InlineData("PowerLoss")] [InlineData("Unclassified")]
    public void MapsKnownModes(string mode) => Assert.Equal("eventUi.modes." + mode, IncidentPresentation.Mode(mode, T));
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("NewFutureMode")]
    public void UnknownModesHaveSafeFallback(string? mode) => Assert.Equal("eventUi.modes.Unknown", IncidentPresentation.Mode(mode, T));
    [Fact] public void TimeUsesExplicitZoneAndExplainsHeartbeat()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test8", TimeSpan.FromHours(8), "Test8", "Test8");
        var display = IncidentPresentation.Create(Row(), "zh-CN", T, zone);
        Assert.Equal("最后记录：2026年9月18日 09:16", display.TimeLabel);
        Assert.Contains("+08:00", display.TimeDetail); Assert.Contains("not failure time", display.TimeDetail);
    }
    [Fact] public void EnglishTimeUsesAppLocale() => Assert.Contains("Sep 18, 2026", IncidentPresentation.Create(Row(), "en-US", T, TimeZoneInfo.Utc).TimeLabel);
    [Theory] [InlineData(0d)] [InlineData(-1d)] [InlineData(1.2d)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidOrZeroSupportIsNotZeroRisk(double value) => Assert.Equal("eventUi.insufficientSupport", IncidentPresentation.Support(value, "en-US", T));
    [Fact] public void PositiveSupportIsNotProbability() => Assert.Contains("not probability", IncidentPresentation.Support(.75, "en-US", T));
    [Fact] public void ResolvesRequestedRecordNotFirst() { var a = Row("a"); var b = Row("b"); Assert.Same(b, IncidentPresentation.Resolve(new[] { a, b }, "b")); }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("missing")] [InlineData("../analysis.json")]
    public void MissingOrPathTextDoesNotFallBack(string? id) => Assert.Null(IncidentPresentation.Resolve(new[] { Row() }, id));
    [Fact] public void DuplicateIdsAreAmbiguous() => Assert.Null(IncidentPresentation.Resolve(new[] { Row("same"), Row("same") }, "same"));
    [Theory] [InlineData(0, false, false)] [InlineData(1, false, true)] [InlineData(2, false, false)] [InlineData(1, true, false)]
    public void ExportRequiresOneSelectionAndNotBusy(int count, bool busy, bool expected) => Assert.Equal(expected, IncidentPresentation.CanExport(count, busy));
}
