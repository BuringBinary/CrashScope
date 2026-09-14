# CrashScope Feature Matrix

This document maps the broader PC-toolbox direction into CrashScope priorities.

Legend:

- **Core** — strengthens the diagnostic identity directly.
- **Planned** — valuable product capability after the desktop foundation.
- **Optional** — useful but should remain modular.
- **Deferred** — low priority until the diagnostic platform is mature.

| Area | Feature | Priority | CrashScope angle |
|---|---|---|---|
| Hardware | CPU telemetry | Core | Incident evidence + live monitoring |
| Hardware | GPU telemetry | Core | Primary graphics-failure evidence |
| Hardware | RAM telemetry | Core | Context for pressure / exhaustion |
| Hardware | Disk / SMART | Planned | Health context and storage diagnostics |
| Hardware | Motherboard / BIOS inventory | Planned | Reproducibility and machine profile |
| Hardware | Display inventory | Core | Graphics-stack context |
| Hardware | FPS / frame telemetry | Planned | Correlate game load with incidents |
| Hardware | Network adapter / latency | Planned | Game/session context |
| UI | Dashboard | Core | Recorder health + system state |
| UI | Live Monitor | Core | Diagnostic-first real-time charts |
| UI | Sensor Explorer | Core | Raw sensor inspection and debugging |
| UI | Tray | Core | Recording status and quick access |
| UI | Overlay | Planned | In-game telemetry + REC state |
| Diagnostics | Incident browser | Core | Historical abnormal sessions |
| Diagnostics | Incident timeline | Core | Main differentiator |
| Diagnostics | Diagnosis rules | Core | Explain evidence and confidence |
| Diagnostics | Windows Event viewer | Core | Correlated OS evidence |
| Diagnostics | Export | Core | Shareable redacted evidence package |
| System | Power plans | Planned | Apply + record + rollback |
| System | CPU affinity / scheduling | Planned | Game/process tuning with change history |
| System | Memory tools | Planned | Conservative, explainable operations |
| System | Startup management | Planned | Reversible startup changes |
| System | Storage cleanup / TRIM | Planned | Maintenance tools with safeguards |
| System | DNS / selected network settings | Optional | Typed, reversible configuration |
| System | Windows Update controls | Optional | High-impact; require clear risk messaging |
| System | Runtime repair | Planned | VC++ / DirectX environment diagnostics |
| Drivers | Driver inventory | Core | Incident context |
| Drivers | Driver-change timeline | Core | Correlate failures with updates |
| Drivers | Driver download/install | Optional | Higher maintenance and privilege burden |
| Display | Topology / refresh / resolution | Core | Graphics-context evidence |
| Display | HDR / VRR state | Planned | Useful GPU/display context |
| Display | Virtual display detection | Core | Remote/overlay context |
| Display | Color filters / ICC helpers | Optional | Separate display-tools module |
| Game | Game launcher | Optional | Lightweight convenience module |
| Game | Steam library | Optional | Non-core integration |
| Game | DLSS profile helper | Optional | Vendor-specific game tool |
| Game | Crosshair overlay | Optional | Separate overlay feature |
| Game | Resolution helper | Optional | Low-cost utility |
| Game | Game-specific live content | Deferred | Prefer community/plugin boundary |
| Toolbox | Third-party tool detection/launcher | Planned | Useful integration without reimplementing everything |
| Toolbox | MSI Afterburner / GPU-Z / CPU-Z links | Planned | Complementary diagnostics |
| Media | Audio EQ | Deferred | Unrelated to core for early releases |
| Media | Music player | Deferred | Product expansion only after stability |
| Product | Theme / dark-light mode | Planned | Desktop polish |
| Product | Global hotkeys | Planned | Overlay/tray usability |
| Product | Automatic update | Planned | Required for stable releases |
| Product | Localization | Planned | Architecture early, translations later |
| Product | Announcement / social widgets | Deferred | Not needed for diagnostic value |

## Integration rule

A toolbox feature earns higher priority when it also improves diagnosis.

Example:

```text
Power Plan tool
  + records previous plan
  + records new plan
  + records timestamp
  + supports rollback
  + appears in Incident Timeline
```

This is preferred over an isolated "one-click optimization" button.

## Recommended navigation growth

### First desktop milestone

```text
Dashboard
Live Monitor
Incidents
Settings
```

### Diagnostic suite

```text
Dashboard
Monitoring
  Hardware
  Live Charts
  Sensors
Diagnostics
  Incidents
  Timeline
  Analysis
  Windows Events
```

### Extended suite

```text
System Tools
  Power
  Process / CPU
  Memory
  Storage
  Network
  Startup
Display & Graphics
Game Tools
Toolbox
```

Media/entertainment features should not occupy the main architecture until the diagnostic and system-tool layers are stable.

## Safety and product-quality requirements for system tools

For each system-changing action, document:

- what it changes
- current value
- requested value
- expected benefit
- possible side effects
- privilege requirement
- whether restart/logoff is required
- whether rollback is supported

Prefer individual understandable actions over large opaque presets.
