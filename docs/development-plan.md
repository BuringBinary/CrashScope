# CrashScope Development Plan

## Product direction

CrashScope evolves from a Windows crash-forensics flight recorder into a **Windows System Diagnostic & Performance Suite**.

The product is built around:

```text
Monitor -> Optimize -> Diagnose -> Extend
```

The diagnostic recorder remains the core. Hardware monitoring, system tuning, display/game tools, media, integrations, and future utilities are all part of the long-term scope, but they are introduced in stages so the project remains buildable and understandable.

## Full reference-scope commitment

The complete user-selected NexBox README feature set has been mapped into CrashScope.

Use these documents together:

- `docs/nexbox-feature-map.md` — exhaustive one-to-one checklist; nothing in the selected reference set is intentionally omitted.
- `docs/feature-matrix.md` — priority and module ownership.
- `ROADMAP.md` — version ordering.
- `docs/desktop-architecture.md` — technical boundaries for the first implementation.

"Expansion" or "content module" means **later implementation**, not exclusion from scope.

## Non-negotiable principles

1. **The recorder must survive the UI.** `CrashScope.Agent` must continue collecting if the desktop app exits or crashes.
2. **Observation before optimization.** Prefer measuring and explaining a setting before changing it.
3. **Every change should be reversible when practical.** System tuning operations should capture the previous value and expose rollback.
4. **Every meaningful system change becomes evidence.** Power-plan, registry, driver, display, CPU-affinity, startup, network and related changes should be written to the CrashScope timeline.
5. **Local-first and privacy-conscious.** Keep the current privacy boundary unless a feature explicitly requires broader collection.
6. **Full scope must stay modular.** Game content, media, announcements and other fast-changing integrations must not weaken the diagnostic collector.
7. **Reference ideas, do not blindly copy source.** Reimplement features in CrashScope architecture unless license compatibility is deliberately accepted.

## Development phases

### Phase 0 - Validate the flight recorder

- Run the existing Agent on the target Windows/AMD machine.
- Confirm telemetry survives forced reboot/hard-freeze scenarios as far as storage permits.
- Check timestamp accuracy and the distance between the final persisted sample and the failure.
- Confirm incident bundle creation and Windows Event Log capture.

Exit criteria: at least one real or controlled abnormal session can be inspected end-to-end.

### Phase 1 - Desktop foundation

Create a desktop application without changing the collection pipeline.

Initial pages:

- Dashboard
- Live Monitor
- Incidents
- Incident Detail
- Settings

First implementation reads the existing `%LOCALAPPDATA%\\CrashScope` files directly.

Recommended first desktop stack:

- .NET 8
- WPF
- MVVM
- shared `CrashScope.Core` class library

Exit criteria: launch the desktop app, see recorder health, latest telemetry, list incidents and open one incident.

### Phase 2 - Correlation and timeline

Build a shared timeline from:

- telemetry
- foreground events
- process start/stop events
- process snapshots
- Windows events
- system-change events

Add synchronized charts for GPU load, clocks, power, temperature, CPU load and RAM.

Exit criteria: an incident can be reconstructed visually around the last heartbeat.

### Phase 3 - Diagnostic engine

Start with deterministic rules, not opaque AI diagnosis.

Classify likely incident types:

- hard freeze
- BSOD
- GPU TDR / driver reset
- application crash
- power loss / forced power-off

Produce:

- evidence for
- evidence against
- alternative explanations
- confidence level

Exit criteria: every supported incident has a human-readable evidence report.

### Phase 4 - Complete hardware-monitoring foundation

Implement the mapped monitoring set:

- CPU load / temperatures / clocks / voltage / power / topology
- GPU load / temperatures / hotspot / fan / clocks / voltage / power / VRAM / driver
- multi-GPU
- physical and virtual memory
- disk / SMART / temperature / interfaces
- motherboard / BIOS / chipset
- display inventory
- network adapter / link / latency
- FPS / frame telemetry
- Sensor Explorer
- TXT / JSON hardware reports
- tray, mini monitor and hardware overlay

### Phase 5 - Explainable system optimization

Build snapshot/rollback infrastructure first, then implement the full mapped set:

- memory cleanup, schedule and thresholds
- page-file controls
- ACE / anti-cheat process tuning module
- process priority / CPU affinity / P-E-core rules
- game-process profiles
- HDD defrag / SSD TRIM helpers
- NVIDIA / AMD shader-cache cleanup
- power plans
- startup management
- DNS / TCP settings
- peripheral polling-related tooling where safely supported
- storage cleanup
- Windows performance/privacy/network/game/touch/app tweaks
- selected Defender settings
- Windows Update controls
- driver workflows
- runtime repair
- VT-x / virtualization controls where safely possible
- network speed/latency test

Each action should follow:

```text
Detect -> Snapshot -> Explain -> Apply -> Verify -> Record -> Rollback
```

### Phase 6 - Graphics and display

Diagnostic context:

- driver inventory and driver-change timeline
- display topology
- resolution / refresh changes
- HDR / VRR
- virtual displays
- remote sessions
- display-off / sleep / resume

Display tools:

- color temperature
- brightness / contrast / saturation
- RGB gamma
- ICC profiles
- multi-monitor configuration
- presets
- crosshair overlays
- PNG crosshair
- picker / presets / hotkeys
- floating and vertical monitoring panels

### Phase 7 - Game and toolbox suite

Implement the general game/toolbox layer:

- game launcher
- per-game profiles
- Steam library / installed games / launch
- FPS/frame telemetry integration
- DLSS preset helper
- auto clicker
- cosmetic GPU-name override + restore
- resolution/aspect helper
- third-party tool detection/launch
- VC++ / DirectX repair helpers

Reference third-party integrations include MSI Afterburner, CPU-Z, GPU-Z, Process Lasso, FxSound, Huorong, Geek Uninstaller, Optimizer, Mem Reduct, OBS Studio and Wallpaper Engine.

### Phase 8 - Productization

- Windows Service or equivalent privileged worker
- typed Named Pipe IPC
- Desktop remains non-elevated by default
- installer / uninstaller
- startup/minimize behavior
- retention policy and ring buffer
- automatic update + in-app update flow
- privacy-redacted export
- CI/release pipeline
- localization infrastructure
- Simplified Chinese / Traditional Chinese / English / French / Japanese / German

### Phase 9 - Product shell and appearance

- dark/light mode
- accent colors
- glass/acrylic-like effects where practical
- optional video background
- global hotkeys
- configurable tray behavior/menu
- splash screen
- announcement system
- important notifications
- daily-popularity widget
- random quote / 一言 widget
- sponsor/about page and QR support area
- module/plugin boundary

### Phase 10 - Delta Force content module

Keep fast-changing game-specific functionality isolated:

- gun-code browsing/categories/search/copy
- likes/submissions/sharing if backend support exists
- daily-password helper
- supported data-source integration
- Delta Force DLSS/profile integration
- external-platform shortcuts
- map / loot / spawn / extraction / boss helpers
- wallpaper helper
- explicit anti-cheat compatibility review for memory-reading features

### Phase 11 - Epic + audio/media suite

Epic:

- free-game cards
- countdown
- open claim/store page

Audio / media:

- 10-band EQ
- presets
- spectrum visualization
- reverb/effects where supported
- NetEase Cloud Music integration where terms/API permit
- Kugou Music integration where terms/API permit
- playlist browsing
- search
- playback controls
- lyrics / karaoke lyrics where available
- supported account login
- desktop lyrics
- mini player
- real-time spectrum

### Phase 12 - Reference-completion audit

Use `docs/nexbox-feature-map.md` as a completion checklist.

For every item, require one of:

- implemented and tested
- module scaffolded with tracked implementation work
- blocked by a documented technical/API/legal constraint

The audit should also verify that system-changing features record before/after state and rollback where practical.

## First local-development exercise

The recommended first task remains the **Recorder Status Card**.

The desktop app should display:

```text
Recorder      Recording / Stale / Error
Session       elapsed time
Last Sample   age of latest persisted sample
Storage       healthy / write warning
```

This exercise touches path discovery, JSON parsing, MVVM binding, timers, error handling, concurrent file reads, and product-state modeling.

After that:

```text
Recorder Status
  -> CPU/GPU/RAM cards
  -> Live chart
  -> Incident list
  -> Incident detail
  -> TimelineBuilder
```

Do not start with the 100-feature toolbox. Build the foundation that all later modules will reuse.

## Definition of v0.2 desktop MVP

The desktop MVP is complete when it has:

- Dashboard
- recorder status
- CPU / GPU / RAM summary cards
- Live Monitor with basic charts
- incident list
- incident detail page
- timeline markers for last heartbeat and reboot
- Windows event display

The full feature set is committed to the long-term scope, but optimization/game/media features are intentionally not required to finish this first milestone.

## Reference-project boundary

NexBox may be studied for UX, page organization, hardware visualization, tray behavior, system-tool ideas and general product patterns. Keep the reference repository separate from the CrashScope source tree while learning, and reimplement concepts independently unless license compatibility is deliberately accepted.
