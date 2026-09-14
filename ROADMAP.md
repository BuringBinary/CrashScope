# Roadmap

CrashScope is growing from a crash-forensics flight recorder into a **Windows System Diagnostic & Performance Suite** built around:

```text
Monitor -> Optimize -> Diagnose -> Extend
```

The recorder and incident-evidence pipeline remain the core. Extended tools should feed useful context back into diagnosis where possible instead of becoming disconnected utilities.

## Scope commitment

The complete user-selected NexBox reference capability set is now part of the long-term CrashScope scope. Not every feature belongs in the first release, but every mapped feature has a planned home and implementation stage.

See also:

- `docs/development-plan.md`
- `docs/desktop-architecture.md`
- `docs/feature-matrix.md`
- `docs/nexbox-feature-map.md` — exhaustive one-to-one reference feature checklist

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
- [ ] settings shell

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

## v0.4 — Full hardware monitoring suite + tray + overlay

- [ ] expanded CPU usage/temp/clock/voltage/power/topology
- [ ] expanded GPU load/temp/hotspot/fan/power/clocks/voltage/VRAM/driver
- [ ] multi-GPU support
- [ ] physical/virtual memory and working-set data
- [ ] disk partitions / capacity / SMART / temperature / interface
- [ ] motherboard / BIOS / chipset inventory
- [ ] display resolution / refresh / model / manufacturer
- [ ] network adapter / link speed / latency
- [ ] FPS / frame telemetry
- [ ] Sensor Explorer with search/filter/grouping
- [ ] hardware report export to TXT / JSON
- [ ] lightweight tray UI
- [ ] mini hardware monitor
- [ ] in-game hardware overlay with `REC` state
- [ ] vertical overlay layout
- [ ] compact/island-style overlay layout
- [ ] always-on-top floating status bar
- [ ] configurable retention/ring buffer

## v0.5 — Explainable system optimization

All system-changing operations should follow:

```text
Detect -> Snapshot -> Explain -> Apply -> Verify -> Record -> Rollback
```

- [ ] `system-change-events.jsonl`
- [ ] memory cleanup / schedule / threshold trigger
- [ ] page-file / virtual-memory controls and restore
- [ ] ACE / anti-cheat process detection and optional tuning
- [ ] process priority / CPU affinity tools
- [ ] P-core / E-core scheduling rules where applicable
- [ ] per-game process optimization profiles
- [ ] HDD defrag helper / SSD TRIM helper
- [ ] NVIDIA / AMD shader-cache cleanup
- [ ] power-plan import / activation / restore
- [ ] startup-item management
- [ ] DNS presets / custom DNS / selected TCP configuration
- [ ] mouse / keyboard polling-related tooling where safely supported
- [ ] temp/cache/log cleanup with preview
- [ ] Windows performance/privacy/network/game/touch/app tweaks
- [ ] selected Defender-related settings with explicit risk messaging
- [ ] Windows Update controls
- [ ] disk-health checks
- [ ] VC++ / DirectX runtime repair workflows
- [ ] virtualization / VT-x state detection and supported controls
- [ ] network speed / latency test
- [ ] surface all relevant changes in Incident Timeline

## v0.6 — Graphics stack + display enhancement

### Diagnostic context

- [ ] per-process GPU Engine counters
- [ ] display topology and virtual-display changes
- [ ] resolution / refresh-rate change timeline
- [ ] HDR / VRR and relevant graphics-state inventory
- [ ] remote-session connect/disconnect events
- [ ] sleep/display-off/resume transitions
- [ ] common overlay/launcher/remote-desktop/hardware-accelerated app detection
- [ ] AMD/NVIDIA/Intel driver inventory
- [ ] driver-change timeline

### Display tools

- [ ] color temperature
- [ ] brightness / contrast / saturation
- [ ] RGB gamma controls
- [ ] ICC profile management
- [ ] per-monitor configuration
- [ ] filter presets
- [ ] crosshair built-in styles
- [ ] custom PNG crosshair
- [ ] crosshair color picker / presets
- [ ] display/filter/crosshair global hotkeys

## v0.7 — Drivers + game tools + toolbox

### Driver workflows

- [ ] NVIDIA installed-version detection
- [ ] available-version check
- [ ] driver download helper
- [ ] installation workflow
- [ ] AMD/Intel driver helper architecture

### Game tools

- [ ] custom game launcher
- [ ] per-game CrashScope profiles
- [ ] Steam library browsing
- [ ] installed Steam game detection
- [ ] Steam account awareness where safely supported
- [ ] direct game launch
- [ ] DLSS model/preset helper
- [ ] resolution/aspect-ratio calculator
- [ ] configurable auto clicker
- [ ] cosmetic GPU-name override + restore

### Third-party toolbox

- [ ] MSI Afterburner integration
- [ ] CPU-Z integration
- [ ] GPU-Z integration
- [ ] Process Lasso integration
- [ ] FxSound integration
- [ ] Huorong integration
- [ ] Geek Uninstaller integration
- [ ] Optimizer integration
- [ ] Mem Reduct integration
- [ ] OBS Studio integration
- [ ] Wallpaper Engine integration

## v0.8 — Productization

- [ ] Windows Service / privileged worker
- [ ] typed Named Pipe IPC
- [ ] keep Desktop UI non-elevated by default
- [ ] installer / uninstaller
- [ ] Windows 10 22H2+ x64 support baseline validation
- [ ] startup/minimize behavior
- [ ] automatic update
- [ ] in-app update download/install
- [ ] robust retention controls
- [ ] stable CI/release pipeline
- [ ] localization infrastructure
- [ ] Simplified Chinese
- [ ] Traditional Chinese
- [ ] English
- [ ] French
- [ ] Japanese
- [ ] German

## v0.9 — Shell / appearance / extensibility

- [ ] custom accent color
- [ ] acrylic/glass-like visual effects
- [ ] optional video background
- [ ] configurable global hotkeys
- [ ] configurable tray menu and close behavior
- [ ] splash/loading screen
- [ ] announcement system
- [ ] important-notification dialogs
- [ ] optional daily-popularity widget
- [ ] optional random quote / 一言 widget
- [ ] sponsor/about page + QR support area
- [ ] module/plugin boundary
- [ ] community integration model

## v1.0 — Windows Diagnostic & Performance Suite

Target stable core identity:

- reliable background flight recorder
- modern live hardware monitor
- incident timeline and evidence-based diagnosis
- reversible, explainable system tools
- graphics/display context
- game-performance context
- tray / overlay UX
- exportable diagnostic evidence
- installer/update/localization foundation

CrashScope should answer not only **"what is the PC doing now?"** but also **"what changed, what happened before the failure, and what evidence supports the diagnosis?"**

## v1.1 — Delta Force content module

This module is intentionally isolated from the core because game data, APIs and anti-cheat compatibility can change quickly.

- [ ] gun-code platform / categories / search / copy
- [ ] likes / submissions / sharing if a backend exists
- [ ] daily-password helper
- [ ] supported daily-password data source
- [ ] Delta Force DLSS/profile integration where appropriate
- [ ] external-platform shortcuts
- [ ] official map-tool integration/shortcuts
- [ ] loot / spawn / extraction / boss helpers where data is available
- [ ] official wallpaper helper
- [ ] anti-cheat compatibility review for any memory-reading functionality

## v1.2 — Epic + audio/media suite

### Epic

- [ ] free-game cards
- [ ] offer countdown
- [ ] open claim/store page

### Audio / EQ

- [ ] 10-band equalizer
- [ ] audio presets
- [ ] spectrum visualization
- [ ] reverb/effect options where supported

### Music

- [ ] NetEase Cloud Music integration where terms/API permit
- [ ] Kugou Music integration where terms/API permit
- [ ] playlist browsing
- [ ] song search
- [ ] playback controls
- [ ] lyrics
- [ ] karaoke-style lyrics where data permits
- [ ] supported account login
- [ ] desktop lyrics
- [ ] mini player
- [ ] audio spectrum

## v1.3 — Reference-feature completion pass

Use `docs/nexbox-feature-map.md` as the acceptance checklist.

- [ ] verify every mapped reference feature has an implementation, module placeholder, or explicit technical blocker
- [ ] verify every system-changing feature records before/after state when practical
- [ ] verify rollback for reversible features
- [ ] verify high-impact features include warnings and privilege requirements
- [ ] verify fast-changing external-content features are isolated from the diagnostic core
- [ ] verify CrashScope-specific diagnostic features remain first-class and are not buried by toolbox features

At this point, the complete mapped reference feature set should have a concrete implementation path inside CrashScope.
