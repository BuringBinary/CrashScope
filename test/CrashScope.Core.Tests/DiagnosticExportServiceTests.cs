using System.IO;
using System.IO.Compression;
using CrashScope.Core.Models;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public class DiagnosticExportServiceTests
{
    private static IncidentInfo Incident(string incidentDir)
    {
        var time = new DateTimeOffset(2026, 9, 21, 1, 0, 0, TimeSpan.Zero);
        var session = new SessionState("synthetic", time, time, false, "", false, "test");
        var analysis = new IncidentAnalysis(session, null, 0, 0, null, Array.Empty<SensorTransition>(),
            Array.Empty<TimelineEntry>(), Array.Empty<FailureHypothesis>(), Array.Empty<string>(), GraphicsContext.Empty);
        return new IncidentInfo("synthetic-incident", incidentDir, time, "Unclassified", 0,
            Array.Empty<string>(), analysis);
    }

    [Fact]
    public async Task ExportHonoursTheChosenFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), "CrashScope-ExportTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var incidentDir = Path.Combine(root, "incident");
            Directory.CreateDirectory(incidentDir);
            File.WriteAllText(Path.Combine(incidentDir, "note.txt"), "fixture");
            // A name the old code could never produce: no CrashScope- prefix, custom words.
            var chosen = Path.Combine(root, "my custom export name.zip");

            var returned = await new DiagnosticExportService().ExportAsync(
                Incident(incidentDir), chosen, redactPersonalData: false);

            Assert.Equal(chosen, returned);
            Assert.True(File.Exists(chosen));
            Assert.Single(Directory.GetFiles(root, "*.zip"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExportReplacesAnExistingFileTheDialogApproved()
    {
        var root = Path.Combine(Path.GetTempPath(), "CrashScope-ExportTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var incidentDir = Path.Combine(root, "incident");
            Directory.CreateDirectory(incidentDir);
            var chosen = Path.Combine(root, "existing.zip");
            File.WriteAllText(chosen, "not a zip archive");

            await new DiagnosticExportService().ExportAsync(
                Incident(incidentDir), chosen, redactPersonalData: false);

            // The stale bytes are gone and the result is a readable archive.
            using var zip = ZipFile.OpenRead(chosen);
            Assert.NotEmpty(zip.Entries);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
