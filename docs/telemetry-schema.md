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
