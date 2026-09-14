# CrashScope Feature Matrix

This document turns the full reference-project capability set into CrashScope priorities and module ownership.

**Scope rule:** every feature listed in `docs/nexbox-feature-map.md` is part of the long-term CrashScope scope. Priority only controls implementation order; it does not mean a feature is dropped.

Legend:

- **Core** — directly strengthens CrashScope monitoring/diagnosis.
- **Next** — should follow the desktop/diagnostic foundation.
- **Expansion** — fully in scope, but built after the core suite is stable.
- **Content module** — fully in scope, isolated because data/APIs change quickly.

| Area | Feature | Priority | CrashScope module / angle |
|---|---|---|---|
| Hardware | CPU usage/temp/clock/voltage/power/topology | Core | Hardware + incident evidence |
| Hardware | GPU usage/temp/hotspot/fan/power/clocks/voltage/VRAM/driver | Core | Primary graphics evidence |
| Hardware | Multi-GPU | Next | Hardware monitoring |
| Hardware | RAM physical/virtual/work-set data | Core | Pressure context |
| Hardware | Disk capacity / SMART / temp / interface | Next | Storage diagnostics |
| Hardware | Motherboard / BIOS / chipset | Next | Machine profile |
| Hardware | Display resolution/refresh/model/vendor | Core | Graphics context |
| Hardware | FPS / frame telemetry | Next | Game-load correlation |
| Hardware | Network adapter/link/latency | Next | Session/game context |
| Monitoring UI | Dashboard | Core | Recorder + live system state |
| Monitoring UI | Live charts | Core | Shared diagnostic time axis |
| Monitoring UI | Sensor Explorer | Core | Raw sensor search/filter/grouping |
| Monitoring UI | TXT / JSON hardware report | Next | Export |
| Monitoring UI | Tray / mini monitor | Core | Background recorder UX |
| Monitoring UI | Game overlay / vertical overlay / floating bar | Next | Live metrics + `REC` state |
| Diagnostics | Incident browser | Core | Abnormal-session history |
| Diagnostics | Incident timeline | Core | Main differentiator |
| Diagnostics | Synchronized telemetry charts | Core | Pre-failure reconstruction |
| Diagnostics | Windows event viewer | Core | OS evidence |
| Diagnostics | Hard-freeze/BSOD/TDR/app-crash/power classification | Core | Diagnosis engine |
| Diagnostics | Evidence for/against + confidence | Core | Explainable diagnosis |
| Diagnostics | Privacy-redacted export | Core | Shareable evidence |
| System | Memory cleanup / threshold / schedule | Next | Explainable memory tools |
| System | Page-file / virtual-memory limits | Expansion | Snapshot + rollback |
| System | ACE / anti-cheat process tuning | Expansion | Opt-in compatibility module |
| System | CPU affinity / P-E core scheduling | Next | Process tuning + history |
| System | Game-process optimization | Next | Per-game profiles |
| System | HDD defrag / SSD TRIM | Next | Storage maintenance |
| System | NVIDIA / AMD shader-cache cleanup | Expansion | Preview + cleanup |
| System | Power plans / import / activate / restore | Next | Change timeline integration |
| System | Startup-item management | Next | Reversible startup changes |
| System | DNS presets / custom DNS / TCP tuning | Expansion | Typed network actions |
| System | Mouse / keyboard polling tooling | Expansion | Peripheral module |
| System | Temp/cache/log cleanup | Next | Safe cleanup preview |
| System | Windows performance/privacy/network/game/touch/app tuning | Expansion | Individual explainable tweaks |
| System | Defender-related tuning | Expansion | High-risk messaging required |
| System | Windows Update controls | Expansion | High-impact, reversible where possible |
| System | NVIDIA driver detection/download/install | Expansion | Driver workflow + history |
| System | AMD / Intel driver helpers | Expansion | Same driver architecture |
| System | Runtime repair (VC++ / DirectX) | Next | Game-runtime diagnostics |
| System | VT-x / virtualization state + controls | Expansion | Capability / reboot-aware workflow |
| System | Network speed / latency test | Expansion | Network diagnostics |
| Display | Color temperature/brightness/contrast/saturation | Expansion | Display tools |
| Display | RGB gamma / ICC profiles / per-monitor presets | Expansion | Display tools |
| Display | Crosshair styles / PNG / presets / picker | Expansion | Overlay module |
| Display | Hardware overlay configurable ordering/layouts | Next | Monitoring overlay |
| Display | Floating top status bar | Next | Compact monitor |
| Display | Vertical side-panel monitor | Next | Secondary-display UX |
| Graphics context | Display topology / virtual displays | Core | Incident context |
| Graphics context | Resolution / refresh timeline | Core | Incident context |
| Graphics context | HDR / VRR state | Next | Incident context |
| Graphics context | Remote-session changes | Core | Incident context |
| Graphics context | Sleep/display-off/resume | Core | Incident context |
| Drivers | Driver inventory | Core | Reproducibility |
| Drivers | Driver-change timeline | Core | Correlation |
| Game | Delta Force gun-code browsing/search/copy | Content module | Optional game-content module |
| Game | Delta Force likes/submission/share workflow | Content module | Requires backend/API |
| Game | Delta Force daily-password helper | Content module | Isolated data/memory integration |
| Game | DLSS model/preset manager | Expansion | Vendor/game tools |
| Game | Delta Force external shortcuts | Content module | Game content |
| Game | Delta Force map/loot/spawn/extraction/boss helpers | Content module | Game content |
| Game | Delta Force wallpaper helper | Content module | Game content |
| Game | Custom game launcher | Expansion | Game profiles |
| Game | Steam library / installed games / account awareness / launch | Expansion | Library integration |
| Game | Epic free-game cards/countdown/open claim page | Content module | External content integration |
| Game | 10-band EQ / presets / spectrum / reverb | Expansion | Audio module |
| Game | Auto clicker / rate / hotkey | Expansion | Automation utility |
| Game | Cosmetic GPU-name registry rewrite + restore | Expansion | Clearly marked entertainment tool |
| Game | Resolution/aspect converter | Expansion | Lightweight utility |
| Media | NetEase Cloud Music integration | Expansion | API/terms dependent |
| Media | Kugou Music integration | Expansion | API/terms dependent |
| Media | Playlist/search/playback | Expansion | Media player |
| Media | Lyrics / karaoke lyrics | Expansion | Media player |
| Media | Account login where supported | Expansion | Secure integration required |
| Media | Desktop lyrics | Expansion | Overlay window |
| Media | Mini player | Expansion | Compact mode |
| Media | Audio spectrum | Expansion | Visualization |
| Toolbox | MSI Afterburner detection/launch | Next | Third-party integration |
| Toolbox | CPU-Z detection/launch | Next | Third-party integration |
| Toolbox | GPU-Z detection/launch | Next | Third-party integration |
| Toolbox | Process Lasso detection/launch | Next | Third-party integration |
| Toolbox | FxSound detection/launch | Expansion | Third-party integration |
| Toolbox | Huorong detection/launch | Expansion | Third-party integration |
| Toolbox | Geek Uninstaller detection/launch | Expansion | Third-party integration |
| Toolbox | Optimizer detection/launch | Expansion | Third-party integration |
| Toolbox | Mem Reduct detection/launch | Expansion | Third-party integration |
| Toolbox | OBS Studio detection/launch | Expansion | Third-party integration |
| Toolbox | Wallpaper Engine detection/launch | Expansion | Third-party integration |
| Product | Dark/light mode | Next | Desktop shell |
| Product | Accent colors / glass effects / video background | Expansion | Appearance |
| Product | Global hotkeys | Next | Overlay/filter/crosshair control |
| Product | System tray / close behavior / tray menu | Core | Background UX |
| Product | Announcement system | Expansion | Optional network content |
| Product | Daily-popularity widget / random quote | Expansion | Optional home widgets |
| Product | Splash/loading screen | Expansion | Product polish |
| Product | Sponsor / QR support page | Expansion | About/support |
| Product | Automatic update / in-app install | Next | Stable release requirement |
| Product | zh-CN / zh-TW / en / fr / ja / de | Next | i18n infrastructure + translations |
| Product | Installer / uninstaller | Next | Productization |
| Product | Windows 10 22H2+ x64 baseline/docs | Next | Supported-platform contract |

## Integration rule

A toolbox feature becomes more valuable when it also improves diagnosis.

Example:

```text
Power Plan tool
  + detects current plan
  + snapshots previous plan
  + explains the change
  + applies and verifies
  + records timestamp/before/after
  + supports rollback
  + appears in Incident Timeline
```

The same rule should be applied to CPU scheduling, registry settings, display configuration, drivers, startup items, network settings, and game profiles.

## Navigation growth

### Desktop foundation

```text
Dashboard
Live Monitor
Incidents
Settings
```

### Diagnostic suite

```text
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

### System/performance suite

```text
System Tools
  Memory
  CPU / Process
  Power
  Storage
  Network
  Startup
  Windows
Display & Graphics
Game Tools
Toolbox
```

### Full extended suite

```text
Game Content
Media
Appearance
Announcements
Community / Plugins
```

## Safety and product-quality requirements

For each system-changing action, document and display as applicable:

- what it changes
- current value
- requested value
- expected benefit
- possible side effects
- privilege requirement
- restart/logoff requirement
- whether rollback is supported
- whether the change is recorded in the incident timeline

Prefer individual, understandable actions over large opaque presets.

## Exhaustive checklist

For the one-to-one checklist of all reference features, use:

- `docs/nexbox-feature-map.md`
