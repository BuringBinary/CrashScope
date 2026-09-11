# Roadmap

## v0.1 — Flight recorder (current)

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

- [ ] exact timeline reconstruction around the last heartbeat
- [ ] distinguish hard freeze / BSOD / TDR / app crash / power loss
- [ ] evidence-for / evidence-against scoring
- [ ] detect GPU load/clock/power state transitions before failure
- [ ] parse Reliability Monitor / WER / LiveKernelReports metadata
- [ ] report generator with root-cause ranking and confidence

## v0.3 — Graphics-stack context

- [ ] per-process GPU Engine counters
- [ ] display topology and virtual-display changes
- [ ] remote-session connect/disconnect events
- [ ] sleep/display-off/resume/power-plan transitions
- [ ] detect common overlays, launchers, remote-desktop and hardware-accelerated apps
- [ ] AMD/NVIDIA/Intel driver inventory and driver-change timeline

## v0.4 — Productization

- [ ] Windows Service collector
- [ ] lightweight tray UI
- [ ] configurable retention/ring buffer
- [ ] incident viewer and charts
- [ ] one-click diagnostic export with privacy redaction
