# CrashScope Player Toolbox Integration

## Product direction

CrashScope evolves from a Windows crash flight recorder into a player-focused system toolbox whose differentiator is still **forensics and causality**.

The product should answer two kinds of questions in one place:

1. **What is my machine doing right now?** — hardware telemetry, FPS, process/game state, display/network/storage status, overlays.
2. **What changed before a problem happened?** — crash/freeze/TDR/BSOD timeline, Windows evidence, recent system/toolbox actions, root-cause ranking.

The key rule is that toolbox features must not destroy CrashScope's diagnostic value. Any operation that changes system state should be explicit, reversible where possible, and recorded into the incident timeline.

## Licensing boundary

NexBox is currently GPL-3.0. CrashScope currently has no repository-level license file.

Until CrashScope licensing is explicitly decided:

- do not copy NexBox source files into CrashScope;
- do not copy proprietary/bundled DLLs or assets from NexBox;
- use NexBox as a product/architecture reference only;
- implement equivalent capabilities independently against documented Windows/vendor APIs and appropriately licensed dependencies.

If CrashScope later adopts GPL-3.0-compatible distribution terms, direct source reuse can be evaluated separately with attribution and license compliance.

## Architecture principle

Separate **observation**, **analysis**, **presentation**, and **system mutation**.

```text
+--------------------------- CrashScope.App ----------------------------+
| Dashboard | Monitor | Incidents | Overlay | Toolbox | Games | Tools |
+-------------------------------|---------------------------------------+
                                |
                       local authenticated RPC
                         (Named Pipe preferred)
                                |
+-------------------------- CrashScope.Agent ---------------------------+
| Telemetry | Processes | Session | Event Log | Power/Display | ETW*  |
|                     append-only event stream                         |
+-------------------------------|---------------------------------------+
                                |
+----------------------- CrashScope.Analysis ---------------------------+
| Timeline | Correlation | Classification | Root-cause ranking | Export|
+-----------------------------------------------------------------------+

+----------------------- CrashScope.Actions ----------------------------+
| Power plan | process policy | cleanup | display tweaks | game tweaks |
| Every change -> actions.jsonl -> rollback metadata                    |
+-----------------------------------------------------------------------+
```

`CrashScope.Agent` remains the reliable recorder and should stay small, predictable, and mostly passive.

`CrashScope.Actions` is a separate boundary for mutations. If a user changes a power plan, process priority, display filter, shader cache, DNS setting, game profile, etc., CrashScope records the action with timestamp, old value, new value, caller, and rollback information. This makes optimization actions usable as forensic evidence instead of invisible variables.

## Repository target layout

```text
src/
  CrashScope.Agent/          # existing recorder, background process
  CrashScope.Contracts/      # shared DTOs / RPC contracts / event schema
  CrashScope.Analysis/       # timeline + correlation + diagnosis engine
  CrashScope.Actions/        # reversible system/game tuning operations
  CrashScope.App/            # desktop UI shell
  CrashScope.Overlay/        # optional FPS/hardware overlay process

docs/
  player-toolbox-integration.md
  architecture.md
  telemetry-schema.md
  validation-plan.md
```

The UI technology can be changed without rewriting the recorder because communication happens across an RPC boundary. A modern web-style shell (Tauri/React) and a native .NET UI (WinUI 3/Avalonia) are both compatible with this architecture.

## Module mapping

### 1. Hardware Monitor — P0

Build on the existing LibreHardwareMonitor collector.

Add:

- categorized CPU / GPU / RAM / motherboard / storage / network views;
- multi-GPU identity and per-device sensor groups;
- configurable sampling intervals;
- live charts and historical timeline;
- export JSON/CSV/TXT;
- sensor favorites;
- collector health and last-sample age.

Do not create a second hardware polling stack if the existing Agent can expose the same sensor stream.

### 2. Incident Center — P0 / differentiator

This remains the flagship feature.

Add:

- exact timeline around last heartbeat;
- markers for foreground/process changes;
- markers for toolbox actions;
- Windows Event / WER / LiveKernelReports correlation;
- hard-freeze / BSOD / TDR / app-crash / power-loss classification;
- evidence-for / evidence-against scoring;
- root-cause candidates and confidence;
- `.crashscope` export bundle.

### 3. Monitor Overlay / OSD — P1

Create a lightweight overlay that reads telemetry from the Agent instead of polling hardware independently.

Initial metrics:

- FPS/frame time when available;
- GPU load/temp/hotspot/clock/power/VRAM;
- CPU load/temp/clock;
- RAM usage;
- ping/network throughput;
- recording/incident status.

Overlay failure must never stop the recorder.

### 4. System Optimization — P1

Implement as typed actions with preview + rollback + audit log.

Examples:

- process priority / affinity profiles;
- power plan selection;
- startup item management;
- shader cache cleanup;
- storage cleanup;
- SSD TRIM / HDD optimize launcher;
- network/DNS presets;
- page-file configuration;
- Windows gaming-related toggles.

Rules:

- show old and new value before applying where feasible;
- log every applied action;
- offer restore/default for persistent settings;
- do not silently disable Windows security features;
- do not manipulate anti-cheat processes/services as a default optimization path.

### 5. Display Tools — P1/P2

- per-monitor color/filter profiles;
- brightness/contrast/gamma controls where supported;
- ICC profile management;
- display topology history;
- display-off/resume events;
- optional crosshair overlay with clear game-policy warning.

Display topology changes should also feed Incident Center because they can correlate with graphics-driver failures.

### 6. Game Profiles — P2

Per-game configuration layer:

- launcher shortcuts;
- preferred power/process profile;
- overlay layout;
- display preset;
- DLSS/FSR/XeSS model/profile management where technically and legally appropriate;
- backup/restore before modifying game files;
- game-specific incident tags.

A game profile activation/deactivation is recorded to the timeline.

### 7. Third-party Tool Hub — P2

Detect and launch installed tools instead of bundling third-party binaries by default.

Examples: MSI Afterburner, GPU-Z, CPU-Z, Process Lasso, OBS, audio tools, uninstallers.

Store integration metadata separately from CrashScope core.

### 8. Entertainment / non-diagnostic features — P3

Music, wallpaper, quotes, announcements and similar features are optional plugin-level work. They should not delay monitoring, incident analysis, overlay, or optimization.

## Data model additions

Add a generic action/event stream alongside telemetry:

```json
{
  "timestamp": "2026-09-11T22:41:15.123+08:00",
  "source": "CrashScope.Actions",
  "category": "power-plan",
  "operation": "set",
  "target": "active-plan",
  "before": "Balanced",
  "after": "High Performance",
  "reversible": true
}
```

Recommended session files:

```text
sessions/<timestamp>/
  telemetry.jsonl
  foreground-events.jsonl
  process-events.jsonl
  process-snapshots.jsonl
  actions.jsonl
  display-events.jsonl
  power-events.jsonl
  network-events.jsonl
```

This is the main architectural advantage of combining the toolbox with CrashScope: **the application knows what the user changed and can correlate those changes with instability later.**

## Delivery phases

### Phase A — foundation

- keep existing v0.1 recorder working;
- add shared Contracts project;
- expose recorder state/telemetry through local RPC;
- add actions event schema;
- fix startup/clean-shutdown edge cases;
- add retention policy.

### Phase B — player dashboard

- desktop shell;
- hardware overview and charts;
- session/recorder status;
- incident browser;
- settings/profile storage.

### Phase C — overlay + safe toolbox

- OSD;
- power/process profiles;
- startup/storage/shader-cache tools;
- rollback + action audit trail.

### Phase D — graphics/game context

- per-process GPU engine counters;
- display topology and power events;
- remote-session events;
- game profiles;
- display filters/crosshair;
- driver inventory and change history.

### Phase E — diagnosis engine

- failure classification;
- evidence scoring;
- automated root-cause candidates;
- incident report generation;
- portable diagnostic export.

## Product identity

CrashScope should not become merely another collection of Windows tweak buttons. The integrated product proposition is:

> **A gaming PC toolbox that remembers what happened before something went wrong.**

Monitoring, tuning and game tools bring daily-use value. The recorder and incident engine make those features part of a coherent system rather than an unrelated toolbox collection.
