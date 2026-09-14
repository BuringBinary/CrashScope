# Roadmap

CrashScope is growing from a crash-forensics flight recorder into a **Windows System Diagnostic & Performance Suite** built around:

```text
Monitor -> Optimize -> Diagnose
```

The recorder and incident-evidence pipeline remain the core. Extended tools should feed useful context back into diagnosis instead of becoming disconnected utilities.

See also:

- `docs/development-plan.md`
- `docs/desktop-architecture.md`
- `docs/feature-matrix.md`

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

## v0.2 — Desktop foundation + live monitor

- [ ] add `CrashScope.Core` shared domain/parsing library
- [ ] add `CrashScope.Desktop` WPF/MVVM application
- [ ] recorder health/status card
- [ ] CPU / GPU / RAM dashboard cards
- [ ] read/tail current session telemetry safely
- [ ] live diagnostic charts
- [ ] incident browser
- [ ] incident-detail shell
- [ ] dark/light theme foundation

## v0.3 — Correlation + incident diagnosis

- [ ] exact timeline reconstruction around the last heartbeat
- [ ] merge telemetry, foreground, process and Windows-event evidence
- [ ] synchronized incident charts
- [ ] distinguish hard freeze / BSOD / TDR / app crash / power loss
- [ ] evidence-for / evidence-against scoring
- [ ] detect GPU load/clock/power state transitions before failure
- [ ] parse Reliability Monitor / WER / LiveKernelReports metadata
- [ ] report generator with root-cause ranking and confidence
- [ ] privacy-aware diagnostic export

## v0.4 — Hardware suite + tray + overlay

- [ ] Sensor Explorer with search/filter/grouping
- [ ] expanded CPU/GPU sensor coverage
- [ ] disk / SMART health
- [ ] motherboard / BIOS inventory
- [ ] display inventory
- [ ] network adapter inventory
- [ ] lightweight tray UI
- [ ] mini hardware monitor
- [ ] optional in-game overlay with `REC` state
- [ ] configurable retention/ring buffer

## v0.5 — Explainable system optimization

All system-changing operations should follow:

```text
Detect -> Snapshot -> Explain -> Apply -> Verify -> Record -> Rollback
```

- [ ] `system-change-events.jsonl`
- [ ] power-plan management
- [ ] process priority / CPU affinity tools
- [ ] optional P-core / E-core scheduling rules where applicable
- [ ] conservative memory tools
- [ ] startup-item management
- [ ] storage cleanup / TRIM helpers
- [ ] selected network/DNS configuration
- [ ] risk/reversibility explanation in UI
- [ ] surface recent configuration changes in incident timeline

## v0.6 — Graphics-stack + display context

- [ ] per-process GPU Engine counters
- [ ] display topology and virtual-display changes
- [ ] resolution / refresh-rate change timeline
- [ ] HDR / VRR and relevant graphics-state inventory
- [ ] remote-session connect/disconnect events
- [ ] sleep/display-off/resume transitions
- [ ] detect common overlays, launchers, remote-desktop and hardware-accelerated apps
- [ ] AMD/NVIDIA/Intel driver inventory
- [ ] driver-change timeline
- [ ] optional display/filter tools

## v0.7 — Game tools + toolbox

- [ ] game launcher
- [ ] FPS/frame telemetry
- [ ] optional DLSS profile helper
- [ ] optional crosshair overlay
- [ ] resolution helper
- [ ] third-party diagnostic/tool detection and launcher
- [ ] runtime repair helpers
- [ ] keep rapidly changing game-specific content behind optional/community modules

## v0.8 — Productization

- [ ] Windows Service / privileged worker
- [ ] typed Named Pipe IPC
- [ ] keep Desktop UI non-elevated by default
- [ ] installer / uninstaller
- [ ] automatic update
- [ ] robust retention controls
- [ ] localization infrastructure
- [ ] stable CI/release pipeline

## v0.9 — Extensibility

- [ ] module/plugin boundary
- [ ] community-tool integration model
- [ ] optional vendor/game modules
- [ ] optional media/audio experiments only after diagnostic stability

## v1.0 — Windows Diagnostic & Performance Suite

Target product identity:

- reliable background flight recorder
- modern live hardware monitor
- incident timeline and evidence-based diagnosis
- reversible, explainable system tools
- graphics/game context
- exportable diagnostic evidence

CrashScope should answer not only **"what is the PC doing now?"** but also **"what changed, what happened before the failure, and what evidence supports the diagnosis?"**
