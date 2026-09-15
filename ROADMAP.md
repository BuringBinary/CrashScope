# Roadmap

## v0.1 — Flight recorder

- [x] 1 Hz CPU/GPU/RAM telemetry through LibreHardwareMonitor
- [x] crash-oriented write-through JSONL logging
- [x] foreground process and user-idle tracking
- [x] process start/stop events
- [x] periodic process snapshots
- [x] session heartbeat and clean-shutdown marker
- [x] next-boot detection of an unclean session
- [x] capture key Windows System events into an incident bundle
- [x] extract telemetry/app tails around the suspected incident
- [ ] validate on the target AMD machine

## v0.2 — Correlation

- [x] exact timeline reconstruction around the last heartbeat
- [x] distinguish hard freeze / BSOD / TDR / app crash / power loss
- [x] evidence-for / evidence-against scoring
- [x] detect GPU load/clock/power state transitions before failure
- [x] parse Reliability Monitor / WER / LiveKernelReports metadata
- [x] report generator with root-cause ranking and confidence
- [ ] validate on the target AMD machine

## v0.3 — Graphics-stack context (current)

- [x] per-process GPU Engine counters
- [x] display topology and virtual-display changes
- [x] remote-session connect/disconnect events
- [x] sleep/display-off/resume/power-plan transitions
- [x] detect common overlays, launchers, remote-desktop and hardware-accelerated apps
- [x] AMD/NVIDIA/Intel driver inventory and driver-change timeline
- [ ] validate on the target AMD machine

## v0.4 — Productization

- [x] Windows Service collector
- [x] lightweight tray UI
- [x] configurable retention/ring buffer
- [x] incident viewer
- [x] charts (CPU/GPU/RAM history graphs)
- [x] one-click diagnostic export with privacy redaction