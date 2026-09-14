# CrashScope Development Plan

## Product direction

CrashScope evolves from a Windows crash-forensics flight recorder into a **Windows System Diagnostic & Performance Suite**.

The product is built around three verbs:

```text
Monitor -> Optimize -> Diagnose
```

The diagnostic recorder remains the core. Hardware monitoring, system tuning, display/game tools, and future utilities should strengthen that core instead of replacing it.

## Non-negotiable principles

1. **The recorder must survive the UI.** CrashScope.Agent must continue collecting if the desktop app exits or crashes.
2. **Observation before optimization.** Prefer measuring and explaining a setting before changing it.
3. **Every change should be reversible.** System tuning operations should capture the previous value and expose rollback when practical.
4. **Every meaningful system change becomes evidence.** Power-plan, registry, driver, display, CPU-affinity, startup and related changes should be written to the CrashScope timeline.
5. **Local-first and privacy-conscious.** Keep the current privacy boundary unless a feature explicitly requires broader collection.
6. **Do not become an undifferentiated toolbox.** Optional entertainment/game utilities must stay modular and lower priority than diagnostics.

## Development phases

### Phase 0 - Validate the flight recorder

- Run the existing Agent on the target Windows/AMD machine.
- Confirm telemetry survives forced reboot/hard freeze scenarios as far as storage permits.
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

First implementation should read the existing `%LOCALAPPDATA%\\CrashScope` files directly.

Recommended stack for the first desktop version:

- .NET 8
- WPF
- MVVM
- shared `CrashScope.Core` class library

Exit criteria: the user can launch the desktop app, see recorder health, live/latest telemetry, list incidents, and open one incident.

### Phase 2 - Correlation and timeline

Build a shared timeline from:

- telemetry
- foreground events
- process start/stop events
- process snapshots
- Windows events
- system-change events

Add synchronized charts for GPU load, clocks, power, temperature, CPU load, and RAM.

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

### Phase 4 - Hardware suite, tray, overlay

Expand hardware monitoring to cover as available:

- CPU load / temperature / clock / voltage / power
- GPU load / core and memory clocks / temperature / hotspot / voltage / power / fan / VRAM
- memory usage
- disk and SMART health
- motherboard and BIOS inventory
- display information
- network adapter information

Add:

- Sensor Explorer
- system tray status
- mini monitor
- optional game overlay with `REC` indicator
- hardware report export

### Phase 5 - System optimization

Introduce system changes only after snapshot/rollback infrastructure exists.

Candidate modules:

- power plans
- process priority and CPU affinity
- P-core / E-core scheduling rules where applicable
- memory tools
- startup management
- storage cleanup / TRIM helpers
- selected network configuration
- selected Windows/game settings

Each action should follow:

```text
Detect -> Snapshot -> Explain -> Apply -> Verify -> Record -> Rollback
```

Avoid bulk "magic optimization" presets that change dozens of undocumented settings.

### Phase 6 - Graphics and display context

Add both diagnostic context and optional tools:

- driver inventory and driver-change timeline
- display topology
- resolution and refresh-rate changes
- HDR / VRR / relevant graphics settings
- virtual-display changes
- remote-session changes
- display-off / sleep / resume transitions
- optional color/filter tools

### Phase 7 - Game and toolbox modules

Possible modules:

- game launcher
- Steam library integration
- FPS / frame telemetry
- DLSS profile helper
- crosshair overlay
- resolution calculator
- third-party tool launcher / installation detection
- runtime repair helpers

Game-specific rapidly changing content should be isolated behind optional/community modules.

### Phase 8 - Productization

- Windows Service or equivalent privileged worker
- Named Pipe IPC between UI and privileged backend
- installer / uninstaller
- retention policy and ring buffer
- automatic update
- privacy-redacted export
- localization infrastructure
- stable release pipeline

### Phase 9 - Optional media and community features

Only after the diagnostic platform is stable:

- audio EQ
- media player
- community plugins
- non-core content integrations

## First local-development exercise

The recommended first task is the **Recorder Status Card**.

The desktop app should display:

```text
Recorder      Recording / Stale / Error
Session       elapsed time
Last Sample   age of latest persisted sample
Storage       healthy / write warning
```

This exercise intentionally touches path discovery, JSON parsing, MVVM binding, timers, error handling, files being concurrently written, and product-state modeling.

After that, implement the first GPU card and a short live trend chart.

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

No optimization, game, media, or destructive system-setting feature is required for this milestone.

## Reference projects

Projects such as NexBox may be studied for UX, page organization, hardware visualization, tray behavior, and general desktop-product ideas. Reimplement concepts independently unless license compatibility is deliberately accepted. Keep external reference code outside the CrashScope source tree while learning.
