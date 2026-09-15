using CrashScope.Core.Diagnostics;
using CrashScope.Core.Infrastructure;
using CrashScope.Core.Models;
using CrashScope.Core.Monitoring;
using CrashScope.Core.Platform;
using System.Text.Json;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("CrashScope currently supports Windows only.");
    return 2;
}

var root = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CrashScope");
var stateDirectory = Path.Combine(root, "state");
var sessionsRoot = Path.Combine(root, "sessions");
var incidentsRoot = Path.Combine(root, "incidents");
Directory.CreateDirectory(stateDirectory);
Directory.CreateDirectory(sessionsRoot);
Directory.CreateDirectory(incidentsRoot);

var sessionDirectory = Path.Combine(sessionsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(sessionDirectory);

var journal = new SessionJournal(stateDirectory, sessionDirectory);

if (journal.PreviousState is { CleanShutdown: false } previous)
{
    var incidentDirectory = Path.Combine(
        incidentsRoot,
        $"{previous.LastHeartbeatAt:yyyyMMdd-HHmmss}-{previous.SessionId[..8]}");
    try
    {
        var analysis = CrashEvidenceCollector.CollectForUncleanSession(previous, incidentDirectory);
        Console.WriteLine($"Detected previous unclean session. Incident saved to: {incidentDirectory}");
        if (analysis.Hypotheses.FirstOrDefault(h => h.Score > 0) is { } leading)
            Console.WriteLine($"Preliminary ranking: {leading.Mode} (confidence {leading.Confidence:P0}). Details: summary.md");
    }
    catch (Exception ex)
    {
        // Incident collection must never prevent the new recording session from starting.
        Console.WriteLine($"Warning: failed to collect incident evidence: {ex.Message}");
    }
}

Console.WriteLine("CrashScope v0.3");
Console.WriteLine($"Session: {journal.Current.SessionId}");
Console.WriteLine($"Data: {sessionDirectory}");
Console.WriteLine($"Elevated: {journal.Current.Elevated}");
if (!journal.Current.Elevated)
    Console.WriteLine("Warning: running without Administrator privileges; some sensors/event details may be unavailable.");
Console.WriteLine("Press Ctrl+C to stop cleanly.");

using var telemetryWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "telemetry.jsonl"));
using var processEventWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "process-events.jsonl"));
using var processSnapshotWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "process-snapshots.jsonl"));
using var foregroundWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "foreground-events.jsonl"));
using var gpuEngineWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "gpu-engines.jsonl"));
using var displayWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "display-events.jsonl"));
using var powerWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "power-events.jsonl"));
using var remoteSessionWriter = new CrashSafeJsonlWriter(Path.Combine(sessionDirectory, "remote-session-events.jsonl"));
using var hardware = new HardwareMonitor();
File.WriteAllText(
    Path.Combine(sessionDirectory, "sensor-catalog.json"),
    JsonSerializer.Serialize(hardware.Catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
File.WriteAllText(
    Path.Combine(sessionDirectory, "driver-inventory.json"),
    JsonSerializer.Serialize(DriverInventory.Capture(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
var processTracker = new ProcessTracker();
var gpuEngineTracker = new GpuEngineTracker();
var displayTracker = new DisplayTopologyTracker();
var powerTracker = new PowerTracker();

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

ForegroundState? previousForeground = null;
var processPollCounter = 0;
var processSnapshotCounter = 0;
var heartbeatCounter = 0;
var displayCounter = 0;
var powerCounter = 0;
var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

try
{
    while (await timer.WaitForNextTickAsync(cancellation.Token))
    {
        var now = DateTimeOffset.Now;
        var foreground = WindowsActivity.CaptureForeground();
        var sensorValues = hardware.CaptureValues();
        telemetryWriter.Write(new TelemetrySnapshot(now, foreground, sensorValues));

        if (previousForeground is null ||
            previousForeground.ProcessId != foreground.ProcessId ||
            !string.Equals(previousForeground.ProcessName, foreground.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            foregroundWriter.Write(new ForegroundEvent(now, foreground.ProcessId, foreground.ProcessName, foreground.IdleSeconds));
            previousForeground = foreground;
        }

        if (++processPollCounter >= 2)
        {
            processPollCounter = 0;
            foreach (var processEvent in processTracker.Poll(now))
                processEventWriter.Write(processEvent);

            var engineSample = gpuEngineTracker.Capture(now);
            if (engineSample is not null)
                gpuEngineWriter.Write(engineSample);
        }

        if (++displayCounter >= 5)
        {
            displayCounter = 0;
            var displayChange = displayTracker.Poll(now);
            if (displayChange is not null)
                displayWriter.Write(displayChange);

            var remoteChange = powerTracker.PollRemoteSession(now);
            if (remoteChange is not null)
                remoteSessionWriter.Write(remoteChange);
        }

        if (++powerCounter >= 10)
        {
            powerCounter = 0;
            var schemeChange = powerTracker.PollScheme(now);
            if (schemeChange is not null)
                powerWriter.Write(schemeChange);
        }

        if (++processSnapshotCounter >= 30)
        {
            processSnapshotCounter = 0;
            processSnapshotWriter.Write(processTracker.CaptureSnapshot(now));
        }

        if (++heartbeatCounter >= 5)
        {
            heartbeatCounter = 0;
            journal.Heartbeat(now);
        }
    }
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C.
}
finally
{
    timer.Dispose();
    journal.MarkClean();
}

return 0;