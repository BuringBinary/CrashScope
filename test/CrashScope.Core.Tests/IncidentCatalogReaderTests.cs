using System.IO;
using System.Text.Json;
using CrashScope.Core.Models;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public class IncidentCatalogReaderTests
{
    private static IncidentAnalysis Analysis(int hour = 1)
    {
        var time = new DateTimeOffset(2026, 9, 21, hour, 0, 0, TimeSpan.Zero);
        var session = new SessionState("synthetic", time, time, false, "", false, "test");
        return new(session, null, 0, 0, null, Array.Empty<SensorTransition>(), Array.Empty<TimelineEntry>(),
            Array.Empty<FailureHypothesis>(), Array.Empty<string>(), GraphicsContext.Empty);
    }
    [Fact] public void MissingRootIsCompleteEmpty()
    {
        var reader = new IncidentCatalogReader(_ => throw new DirectoryNotFoundException());
        Assert.True(reader.Read("unused").IsCompleteEmpty);
    }
    [Fact] public void RootAccessDeniedIsNotEmptySuccess()
    {
        var result = new IncidentCatalogReader(_ => throw new UnauthorizedAccessException("fixture")).Read("unused");
        Assert.False(result.IsCompleteEmpty); Assert.Equal("rootUnavailable", Assert.Single(result.Issues).Code);
    }
    [Fact] public void RootIoFailureIsExplicit() => Assert.True(new IncidentCatalogReader(_ => throw new IOException("fixture")).Read("unused").HasIssues);
    [Fact] public void OneCorruptEntryDoesNotHideHealthyEntries()
    {
        var result = new IncidentCatalogReader(_ => new[] { "healthy", "broken" }, p => p == "broken" ? throw new JsonException() : Analysis()).Read("unused");
        Assert.Single(result.Items); Assert.Equal("invalidAnalysis", Assert.Single(result.Issues).Code); Assert.False(result.IsCompleteEmpty);
    }
    [Theory] [InlineData("missing", "missingAnalysis")] [InlineData("denied", "accessDenied")]
    [InlineData("json", "invalidAnalysis")] [InlineData("io", "readError")] [InlineData("evaluation", "evaluationError")]
    public void FailuresKeepTheirMeaning(string kind, string code)
    {
        Exception error = kind switch { "missing" => new FileNotFoundException(), "denied" => new UnauthorizedAccessException(), "json" => new JsonException(), "io" => new IOException(), _ => new InvalidOperationException() };
        var result = new IncidentCatalogReader(_ => new[] { "broken" }, _ => throw error).Read("unused");
        Assert.Empty(result.Items); Assert.Equal(code, Assert.Single(result.Issues).Code); Assert.False(result.IsCompleteEmpty);
    }
    [Fact] public void SortsAvailableEntriesNewestFirst()
    {
        var result = new IncidentCatalogReader(_ => new[] { "old", "new" }, p => Analysis(p == "new" ? 2 : 1)).Read("unused");
        Assert.Equal("new", result.Items[0].DirectoryName);
    }
    [Fact] public void MissingRequiredFieldsBecomeIssue()
    {
        var result = new IncidentCatalogReader(_ => new[] { "broken" }, _ => Analysis() with { PreviousSession = null! }).Read("unused");
        Assert.Equal("invalidAnalysis", Assert.Single(result.Issues).Code);
    }
    [Fact] public void RetryReplacesPreviousFailure()
    {
        var broken = true; var reader = new IncidentCatalogReader(_ => new[] { "entry" }, _ => broken ? throw new IOException() : Analysis());
        Assert.True(reader.Read("unused").HasIssues); broken = false;
        var result = reader.Read("unused"); Assert.False(result.HasIssues); Assert.Single(result.Items);
    }
    [Fact] public void DefaultReaderPreservesBadFileAndReportsMissingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "CrashScope-ReadTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bad")); Directory.CreateDirectory(Path.Combine(root, "missing"));
            var path = Path.Combine(root, "bad", "analysis.json"); File.WriteAllText(path, "not-json");
            var result = new IncidentCatalogReader().Read(root);
            Assert.Equal(2, result.Issues.Count); Assert.Equal("not-json", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public void SummaryDoesNotCallFailedLoadEmpty()
    {
        var result = new IncidentReadResult(Array.Empty<IncidentInfo>(), new[] { new IncidentReadIssue("bad", "readError", "fixture") });
        Assert.Equal("readUi.failed", result.Summary(k => k, "en-US"));
    }
    [Fact] public void DetailsAreBoundedWithoutHidingTotalCount()
    {
        var result = new IncidentReadResult(Array.Empty<IncidentInfo>(), Enumerable.Range(0, 30).Select(i => new IncidentReadIssue("id"+i, "readError", "fixture")).ToArray());
        Assert.Equal(30, result.Issues.Count); Assert.DoesNotContain("id20:", result.Details(k => k));
    }
}
