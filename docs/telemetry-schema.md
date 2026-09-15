# Telemetry schema v0.1

## telemetry.jsonl — every 1 second

Sensor metadata is written once to `sensor-catalog.json`. Each telemetry line then contains only the timestamp, foreground/idle context, and an ordered numeric value array matching that catalog. This avoids repeating long sensor names every second.

Each line contains:

- timestamp
- foreground process PID/name
- user idle seconds
- ordered sensor values for selected LibreHardwareMonitor sensors
  - temperature
  - load
  - clock
  - power
  - voltage
  - fan
  - memory/data counters

## foreground-events.jsonl — on foreground process change

Stores only PID, process name, timestamp, and idle seconds. Window title is deliberately excluded in v0.1.

## process-events.jsonl — 2 second polling diff

Records process start/stop transitions. No executable path or command line is collected.

## process-snapshots.jsonl — every 30 seconds

Provides a low-frequency complete process-name/PID snapshot. This lets a later incident answer questions such as “Was a remote-desktop client, launcher, browser, overlay, or hardware-accelerated app still running before the freeze?”

## last-session.json

Heartbeat/clean-shutdown marker. If the next launch sees `cleanShutdown=false`, the previous session is considered *unclean*, not automatically “GPU crash”. The analyzer then collects additional Windows evidence.

## Incident bundle files (v0.2)

- `windows-events.json` — matching System + Application log events. Each entry can carry an `eventData` dictionary of key EventData properties (e.g. Kernel-Power 41 `BugcheckCode` / `PowerButtonTimestamp`). Binary/oversized values are dropped.
- `external-evidence.json` — WER reports (`ReportArchive`/`ReportQueue`), LiveKernelReports dumps, `C:\Windows\Minidump\*.dmp`, and `MEMORY.DMP` whose timestamps fall in the incident window.
- `sensor-catalog.json` — copied so the telemetry tail remains interpretable inside the bundle.
- `analysis.json` — machine-readable result: final-session facts, GPU transitions, merged timeline, and all failure hypotheses with evidence-for / evidence-against items, weights, scores, and confidence.
- `timeline.md` — human-readable ordered reconstruction around the final heartbeat (telemetry samples, foreground/process changes, GPU transitions, Windows and external evidence).
- `summary.md` — ranked hypotheses with confidence, GPU transition digest, and open questions. Correlation, not a verdict.
