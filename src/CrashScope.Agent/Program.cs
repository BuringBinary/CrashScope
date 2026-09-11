using CrashScope.Agent.Diagnostics;
using CrashScope.Agent.Infrastructure;
using CrashScope.Agent.Models;
using CrashScope.Agent.Monitoring;
using CrashScope.Agent.Platform;
using CrashScope.Contracts;
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
    CrashEvidenceCollector.CollectForUncleanSession(previous, incidentDirectory);
    Console.WriteLine($"Detected previous unclean session. Incident saved to: {incidentDirectory}");
}

Console.WriteLine("CrashScope v0.1");
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
using var hardware = new HardwareMonitor();
File.WriteAllText(
    Path.Combine(sessionDirectory, "sensor-catalog.json"),
    JsonSerializer.Serialize(hardware.Catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
var processTracker = new ProcessTracker();

var sensorCatalog = hardware.Catalog
    .Select(sensor => new SensorCatalogDto(
        sensor.Id,
        sensor.HardwareType,
        sensor.HardwareName,
        sensor.SensorType,
        sensor.SensorName,
        sensor.Identifier))
    .ToArray();

TelemetryDto? latestTelemetry = null;
var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var rpcServer = new AgentRpcServer(
    statusProvider: () =>
    {
        var current = journal.Current;
        return new AgentStatusDto(
            current.AgentVersion,
            current.SessionId,
            current.StartedAt,
            current.LastHeartbeatAt,
            current.Elevated,
            current.SessionDirectory,
            Volatile.Read(ref latestTelemetry)?.Timestamp,
            sensorCatalog.Length);
    },
    telemetryProvider: () => Volatile.Read(ref latestTelemetry),
    sensorCatalogProvider: () => sensorCatalog,
    incidentProvider: () => IncidentCatalog.List(incidentsRoot));
var rpcTask = rpcServer.RunAsync(cancellation.Token);
Console.WriteLine($"RPC: \\.\\pipe\\{AgentRpcProtocol.PipeName}");

ForegroundState? previousForeground = null;
var processPollCounter = 0;
var processSnapshotCounter = 0;
var heartbeatCounter = 0;
var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

try
{
    while (await timer.WaitForNextTickAsync(cancellation.Token))
    {
        var now = DateTimeOffset.Now;
        var foreground = WindowsActivity.CaptureForeground();
        var sensorValues = hardware.CaptureValues();
        telemetryWriter.Write(new TelemetrySnapshot(now, foreground, sensorValues));

        Volatile.Write(ref latestTelemetry, new TelemetryDto(
            now,
            new ForegroundDto(foreground.ProcessId, foreground.ProcessName, foreground.IdleSeconds),
            sensorValues));

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
    cancellation.Cancel();
    timer.Dispose();
    journal.MarkClean();

    try
    {
        await rpcTask;
    }
    catch (OperationCanceledException)
    {
        // Expected during shutdown.
    }
}

return 0;
