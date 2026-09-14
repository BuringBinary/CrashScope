# NexBox Reference Feature Map

This document maps **all user-selected NexBox README capabilities** into the long-term CrashScope product scope.

The goal is not to copy NexBox source code. NexBox is a reference for product ideas, UX, module boundaries, and implementation research. CrashScope should reimplement the capabilities with its own architecture and preserve its diagnostic identity.

## Product rule

Every feature below is **in scope** for the long-term CrashScope project. Priority and version may differ, but no item in this map is intentionally dropped.

CrashScope keeps the product model:

```text
Monitor -> Optimize -> Diagnose -> Extend
```

Where possible, system-changing features must also feed evidence into CrashScope's forensic timeline.

---

## 1. Hardware monitoring

Target module: `CrashScope.Modules.Hardware`

### CPU

- [ ] total CPU usage
- [ ] per-core usage
- [ ] temperatures
- [ ] frequencies
- [ ] voltages
- [ ] power
- [ ] core topology
- [ ] P-core / E-core topology when applicable

### GPU

- [ ] GPU usage
- [ ] temperature
- [ ] hotspot temperature
- [ ] fan speed
- [ ] power
- [ ] core clock
- [ ] memory clock
- [ ] voltage
- [ ] VRAM usage
- [ ] driver version
- [ ] multi-GPU support

### Memory

- [ ] physical memory usage
- [ ] virtual memory / page-file usage
- [ ] working-set information

### Storage

- [ ] partition capacity / usage
- [ ] SMART data
- [ ] disk health assessment
- [ ] disk temperature
- [ ] interface / media type

### Motherboard / firmware

- [ ] motherboard model
- [ ] BIOS / UEFI version
- [ ] chipset information where available

### Display

- [ ] resolution
- [ ] refresh rate
- [ ] model
- [ ] manufacturer
- [ ] display topology

### FPS / frame telemetry

- [ ] FPS capture
- [ ] frame telemetry for incident correlation where practical
- [ ] in-game overlay display

### Network

- [ ] adapter type
- [ ] adapter identification
- [ ] link speed
- [ ] latency / game latency measurement
- [ ] MAC-address display only where privacy settings permit

### Monitoring UX

- [ ] live hardware dashboard
- [ ] compact trend charts
- [ ] Sensor Explorer
- [ ] grouping by hardware type
- [ ] sensor search / filtering
- [ ] real-time refresh
- [ ] hardware report export to TXT
- [ ] hardware report export to JSON

CrashScope extension: hardware telemetry is also incident evidence and feeds the synchronized Incident Timeline.

---

## 2. System optimization

Target module: `CrashScope.Modules.Optimization`

All optimization actions should follow:

```text
Detect -> Snapshot -> Explain -> Apply -> Verify -> Record -> Rollback
```

### Memory cleanup

- [ ] physical-memory cleanup helpers
- [ ] working-set cleanup helpers
- [ ] scheduled cleanup
- [ ] threshold-triggered cleanup

### Virtual-memory / page-file controls

- [ ] inspect current page-file configuration
- [ ] presets
- [ ] custom limits
- [ ] rollback / restore previous configuration

### ACE / anti-cheat process optimization

- [ ] detect supported anti-cheat processes
- [ ] optional process priority tuning
- [ ] optional CPU affinity tuning
- [ ] background detection
- [ ] explicit compatibility/risk warnings

### CPU scheduling

- [ ] process CPU-affinity editor
- [ ] P-core / E-core assignment where supported
- [ ] persistent scheduling rules
- [ ] one-click restore to prior/default state

### Game-process optimization

- [ ] process priority tuning
- [ ] process affinity tuning
- [ ] per-game optimization profiles
- [ ] restore original settings

### Disk maintenance

- [ ] HDD defragmentation launcher/helper
- [ ] SSD TRIM helper
- [ ] storage-health checks before risky actions

### Shader-cache cleanup

- [ ] NVIDIA shader-cache cleanup
- [ ] AMD shader-cache cleanup
- [ ] explain what will be removed

### Power management

- [ ] detect current power plan
- [ ] built-in performance-oriented plans
- [ ] import power plans
- [ ] activate plan
- [ ] restore prior plan
- [ ] record power-plan transitions in `system-change-events.jsonl`

### Startup management

- [ ] registry startup entries
- [ ] startup-folder entries
- [ ] enable/disable startup items
- [ ] CrashScope start-minimized / startup behavior

### Network optimizer

- [ ] DNS presets
- [ ] custom DNS
- [ ] 114 DNS preset
- [ ] AliDNS preset
- [ ] Cloudflare DNS preset
- [ ] Tencent DNS preset
- [ ] selected TCP tuning
- [ ] rollback to prior network configuration

### Peripheral optimization

- [ ] mouse USB polling-rate related tooling where safely supported
- [ ] keyboard USB polling-rate related tooling where safely supported
- [ ] device capability detection and warnings

### Storage cleanup

- [ ] temporary-file scanning
- [ ] cache scanning
- [ ] log scanning
- [ ] preview before delete
- [ ] safe cleanup

### System optimizer

The broader Windows tuning module should cover the reference categories:

- [ ] performance
- [ ] privacy
- [ ] network
- [ ] gaming
- [ ] touch/input
- [ ] apps
- [ ] selected Windows Defender-related settings with strong risk messaging

No opaque "50 tweaks" button: each tweak should be independently inspectable and reversible when practical.

### Windows Update management

- [ ] inspect Windows Update state
- [ ] pause updates
- [ ] automatic-update controls where supported
- [ ] driver-update controls where supported
- [ ] restore prior state

### NVIDIA driver management

- [ ] installed-driver detection
- [ ] available-version check
- [ ] download helper
- [ ] installation workflow
- [ ] driver-change timeline entry

The architecture should also allow AMD and Intel driver helpers later.

### Disk-health detection

- [ ] SMART information
- [ ] health evaluation
- [ ] temperature monitoring

### Runtime repair

- [ ] VC++ runtime detection/repair workflow
- [ ] DirectX runtime detection/repair workflow
- [ ] common game-runtime diagnostics

### VT-x / virtualization

- [ ] virtualization-state detection
- [ ] firmware/OS capability explanation
- [ ] supported enable/disable workflow where safely possible
- [ ] reboot requirement messaging

### Network-speed test

- [ ] bandwidth test
- [ ] latency test
- [ ] historical test results optional

---

## 3. Display enhancement

Target module: `CrashScope.Modules.Display`

### Display filters

- [ ] color temperature
- [ ] brightness
- [ ] contrast
- [ ] saturation
- [ ] RGB gamma controls
- [ ] ICC profile management
- [ ] per-monitor configuration
- [ ] custom filter presets

### Crosshair overlay

- [ ] cross style
- [ ] dot style
- [ ] circle style
- [ ] additional built-in styles
- [ ] custom PNG crosshair
- [ ] player-style presets where legally/technically appropriate
- [ ] color picker
- [ ] configurable global hotkey

### Hardware overlay panel

- [ ] FPS
- [ ] CPU metrics
- [ ] GPU metrics
- [ ] memory metrics
- [ ] game latency
- [ ] configurable item ordering
- [ ] drag/reorder
- [ ] compact/island-style layout
- [ ] vertical layout
- [ ] default layout
- [ ] `REC` recording indicator

### Floating navigation / status bar

- [ ] always-on-top compact bar
- [ ] CPU usage
- [ ] GPU usage
- [ ] memory usage
- [ ] configurable position

### Vertical overlay panel

- [ ] separate side-panel window
- [ ] secondary-monitor friendly mode

CrashScope extension: display configuration changes should be captured as diagnostic context.

---

## 4. Game tools

Target module: `CrashScope.Modules.Game`

### Delta Force area

The reference project includes a dedicated Delta Force area. CrashScope will keep these capabilities behind an optional/game-content module so the core app is not coupled to fast-changing game content.

- [ ] gun-code browsing platform
- [ ] category browsing
- [ ] keyword search
- [ ] one-click code copy
- [ ] likes / interactions if a backend exists
- [ ] submission/sharing workflow if a backend exists
- [ ] daily-password helper
- [ ] supported data-source integration for daily password
- [ ] DLSS model/preset management
- [ ] quality-level mapping
- [ ] external platform shortcuts
- [ ] official map-tool shortcuts/integration
- [ ] loot / spawn / extraction / boss coordinate helpers where data is available
- [ ] official wallpaper browser/download helper

Any game-memory-reading feature must be isolated, opt-in, and reviewed for anti-cheat compatibility before implementation.

### Game launcher

- [ ] add custom games
- [ ] game metadata
- [ ] one-click launch
- [ ] per-game CrashScope profile

### Steam management

- [ ] library browsing
- [ ] installed-game detection
- [ ] multi-account awareness where safely supported
- [ ] direct game launch

### Epic free games

- [ ] free-game cards
- [ ] offer countdown
- [ ] open store/claim page

### Audio equalizer

- [ ] 10-band EQ
- [ ] presets
- [ ] spectrum visualization
- [ ] reverb/effect options where supported

### Auto clicker

- [ ] configurable click rate
- [ ] configurable hotkey
- [ ] explicit enable/disable indicator

### Displayed GPU-name rewrite / entertainment feature

- [ ] inspect current display name
- [ ] optional registry-based display-name override where supported
- [ ] one-click restore
- [ ] mark clearly as cosmetic

### Resolution converter

- [ ] resolution conversion helper
- [ ] aspect-ratio calculations
- [ ] common presets

---

## 5. Media / entertainment

Target module: `CrashScope.Modules.Media`

### Music player

- [ ] NetEase Cloud Music integration where API/terms permit
- [ ] Kugou Music integration where API/terms permit
- [ ] playlist browsing
- [ ] song search
- [ ] playback controls
- [ ] lyrics
- [ ] karaoke-style progressive lyrics where source data permits
- [ ] account login integration where supported and compliant

### Desktop lyrics

- [ ] always-on-top lyrics window
- [ ] configurable font
- [ ] configurable color
- [ ] configurable size
- [ ] configurable position

### Mini music player

- [ ] compact playback mode

### Audio spectrum

- [ ] real-time spectrum visualization

---

## 6. Third-party tool integration

Target module: `CrashScope.Modules.Toolbox`

Provide installation detection, paths, launch actions, and optionally official download links for commonly used tools.

Reference targets:

- [ ] MSI Afterburner
- [ ] CPU-Z
- [ ] GPU-Z
- [ ] Process Lasso
- [ ] FxSound
- [ ] Huorong Security
- [ ] Geek Uninstaller
- [ ] Optimizer
- [ ] Mem Reduct
- [ ] OBS Studio
- [ ] Wallpaper Engine

CrashScope should prefer integration/detection over reimplementing mature specialist tools unnecessarily.

---

## 7. Product / shell features

Target: Desktop shell and shared product infrastructure.

### Theme customization

- [ ] dark mode
- [ ] light mode
- [ ] custom accent color
- [ ] acrylic/glass-like effects where practical
- [ ] video wallpaper/background option

### Global hotkeys

- [ ] crosshair hotkey
- [ ] overlay hotkey
- [ ] display-filter hotkey
- [ ] configurable bindings

### System tray

- [ ] minimize to tray
- [ ] configurable close behavior
- [ ] independent tray menu configuration
- [ ] recorder health indicator
- [ ] latest incident shortcut

### Announcement system

- [ ] in-app announcements
- [ ] important notification dialog
- [ ] network-free fallback behavior

### Daily popularity / quote widget

- [ ] optional daily-popularity widget
- [ ] optional random quote / "一言" widget

### Startup screen

- [ ] custom splash/loading screen

### Sponsor support

- [ ] sponsor/about page
- [ ] optional QR-code support area

### Automatic update

- [ ] startup update check
- [ ] in-app download
- [ ] in-app install/update flow
- [ ] release-channel support later

### Localization

- [ ] Simplified Chinese
- [ ] Traditional Chinese
- [ ] English
- [ ] French
- [ ] Japanese
- [ ] German
- [ ] architecture should allow more locales later

---

## 8. Installation and system requirements

Target: installer/productization.

Reference baseline:

- [ ] Windows 10 22H2 or newer baseline unless CrashScope testing supports older versions
- [ ] 64-bit support baseline
- [ ] documented memory recommendation
- [ ] documented disk-space requirement
- [ ] installer
- [ ] uninstaller
- [ ] startup option
- [ ] administrator/privilege explanation

CrashScope-specific requirement: recorder reliability, storage retention, and incident data size must be documented separately from the UI installation footprint.

---

## 9. Engineering practices to adopt as reference goals

These are not end-user features, but they are part of the reference project's useful engineering shape.

- [ ] clear UI/backend separation
- [ ] reusable UI components
- [ ] centralized state management where useful
- [ ] localization resources
- [ ] formatting/linting rules
- [ ] CI build validation
- [ ] release packaging
- [ ] installer/uninstaller separation where useful
- [ ] modular feature directories

CrashScope does **not** need to copy NexBox's React/Tauri/Rust stack. The current first-choice path remains .NET 8 + WPF/MVVM + C# because it matches the existing Agent and Windows-only product direction.

---

# CrashScope-only differentiators

The following capabilities are not merely reference-project parity; they define CrashScope's identity:

- [ ] append-oriented flight recorder
- [ ] session heartbeat and abnormal-session detection
- [ ] incident evidence bundle
- [ ] unified Incident Timeline
- [ ] synchronized pre-failure telemetry charts
- [ ] evidence-for / evidence-against diagnosis
- [ ] confidence-scored incident classification
- [ ] `system-change-events.jsonl`
- [ ] driver-change timeline
- [ ] display-topology timeline
- [ ] power/sleep/display-off timeline
- [ ] rollback-aware optimization history
- [ ] privacy-redacted diagnostic export

The combination should make CrashScope able to answer:

> What is the PC doing now?
>
> What did I change?
>
> What happened before the failure?
>
> What evidence supports the likely cause?

---

# Implementation ordering

All mapped features are in scope, but implementation should be incremental:

```text
Foundation
  -> Desktop
  -> Hardware monitoring
  -> Incident timeline/diagnosis
  -> Tray/overlay
  -> System optimization
  -> Display/graphics
  -> Game tools
  -> Toolbox
  -> Product shell
  -> Media/community extensions
```

This ordering is intended to keep the project buildable and educational while preserving the full long-term feature set.
