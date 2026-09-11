# Architecture

## Goal

CrashScope is a Windows crash-forensics recorder. Its job is not to replace HWiNFO, WinDbg, or Windows Event Viewer. Its job is to preserve and correlate the context immediately before a hard freeze, driver reset, BSOD, or forced reboot.

## v0.1 pipeline

```text
LibreHardwareMonitor ----> 1 Hz hardware telemetry ----\
                                                    \
Win32 foreground/idle ----> app context ---------------> per-session JSONL
                                                     /
Process polling ----------> start/stop + snapshots ---/

session heartbeat --------> last-session.json
                                   |
                         abnormal next boot?
                                   |
                                   v
Windows Event Log -------> incident evidence bundle
                                   |
                                   v
                        incidents/<timestamp>/
```

## Why append-only JSONL

- A complete line is independently parseable.
- Files survive process/system interruption better than large in-memory batches.
- `FileOptions.WriteThrough` plus `Flush(flushToDisk: true)` minimizes the window in which the latest sample exists only in cache.
- Dynamic sensor sets are easier to represent than a fixed CSV schema.

This does not guarantee preservation through every storage-controller or power-loss scenario. It is a best-effort flight recorder.

## Permission model

v0.1 uses user-mode APIs only. No custom kernel driver is required.

Normal user mode can usually provide process/foreground context and many event-log entries. Administrator mode is recommended because LibreHardwareMonitor and some event sources may expose additional data only when elevated.

For unattended use, the intended deployment is a Scheduled Task running at logon with `RunLevel Highest`. A Windows Service can replace this later if session-independent collection is required.

## Privacy boundary

The recorder intentionally stores process names and PIDs, but not command-line arguments, executable paths, window titles, typed text, network payloads, screenshots, or file contents.
