# CrashScope Desktop Architecture

## Goal

Add a modern desktop diagnostic console without weakening the reliability of the existing crash recorder.

The UI is a consumer of evidence, not the source of truth.

## Proposed solution layout

```text
src/
├─ CrashScope.Agent/
│  ├─ Monitoring/
│  ├─ Diagnostics/
│  ├─ Recording/
│  └─ Platform/
│
├─ CrashScope.Core/
│  ├─ Models/
│  ├─ Contracts/
│  ├─ Parsing/
│  ├─ Timeline/
│  └─ Analysis/
│
├─ CrashScope.Desktop/
│  ├─ Views/
│  ├─ ViewModels/
│  ├─ Controls/
│  ├─ Services/
│  └─ Themes/
│
└─ future/
   ├─ CrashScope.Service/
   └─ CrashScope.Modules.*
```

## Responsibilities

### CrashScope.Agent

Owns continuous collection and durable recording.

It must not depend on WPF, tray state, charts, or other desktop concerns.

Responsibilities:

- hardware sampling
- foreground / idle tracking
- process lifecycle tracking
- heartbeat
- incident detection
- Windows Event Log collection
- append-oriented persistence

### CrashScope.Core

Shared domain layer used by Agent and Desktop.

Candidate models:

```text
SessionInfo
TelemetrySample
SensorDescriptor
ForegroundEvent
ProcessEvent
WindowsEventEvidence
SystemChangeEvent
IncidentSummary
TimelineEvent
DiagnosisResult
EvidenceItem
```

Candidate services:

```text
JsonlReader
SessionRepository
IncidentRepository
TimelineBuilder
DiagnosisEngine
```

This library should avoid UI dependencies.

### CrashScope.Desktop

WPF/MVVM presentation layer.

Initial navigation:

```text
Dashboard
Live Monitor
Incidents
Incident Detail
Settings
```

Later navigation may add:

```text
Sensor Explorer
System Tools
Display / Graphics
Game Tools
Toolbox
```

## Phase-1 data flow

Keep the first version simple:

```text
CrashScope.Agent
      |
      v
%LOCALAPPDATA%\CrashScope
      |
      +-- state/
      +-- sessions/
      +-- incidents/
      |
      v
CrashScope.Core parsers
      |
      v
CrashScope.Desktop
```

Desktop should tolerate:

- files not existing yet
- partially written/current JSONL files
- malformed final lines after interruption
- current-session files changing while read
- stale heartbeat
- missing sensors on particular hardware

## Phase-2 real-time data flow

Once file-based UI works reliably, add IPC only where it improves UX.

Preferred direction:

```text
Desktop <---- Named Pipe ----> Service/Agent
```

Use IPC for live status and privileged operations; keep durable JSONL as the forensic source of truth.

## Privilege model

Do not permanently run the whole desktop UI elevated.

Long-term structure:

```text
CrashScope.Desktop
normal user
      |
      | validated request
      v
CrashScope.Service
privileged / SYSTEM
```

Privileged operations include selected system tuning, protected telemetry sources, and system configuration changes.

The privileged side should validate an allow-listed operation and typed parameters. Do not expose an unrestricted command shell through IPC.

## Desktop page design

### Dashboard

Primary question: **Is CrashScope recording correctly right now?**

Show:

- Recorder status
- last persisted sample age
- current session duration
- CPU / GPU / RAM summary
- recent incidents
- recent system changes

### Live Monitor

Diagnostic-first charts:

- GPU load
- GPU core clock
- GPU memory clock
- GPU power
- GPU temperature / hotspot
- CPU load / package temperature / power
- RAM usage

Charts should share timestamps where possible.

### Incidents

List abnormal sessions with:

- timestamp
- incident type
- final heartbeat
- evidence summary
- diagnosis confidence once available

### Incident Detail

Combine:

- synchronized telemetry charts
- merged event timeline
- Windows event evidence
- process / foreground context
- recent system changes
- diagnosis result

The timeline is a core product feature.

## Timeline architecture

`TimelineBuilder` normalizes heterogeneous evidence into a common model.

Example:

```text
Timestamp
Category
Severity
Title
Description
Source
Metadata
```

Inputs may include:

```text
telemetry.jsonl
foreground-events.jsonl
process-events.jsonl
process-snapshots.jsonl
windows-events.json
system-change-events.jsonl
```

The Desktop then sorts and renders normalized events instead of knowing every file format itself.

## System-change recorder

Any future CrashScope action that materially changes Windows behavior should emit a `SystemChangeEvent`.

Example conceptual payload:

```json
{
  "timestamp": "2026-09-14T08:20:00+08:00",
  "module": "Power",
  "action": "PowerPlanChanged",
  "before": "Balanced",
  "after": "HighPerformance",
  "source": "User",
  "reversible": true
}
```

Useful categories:

- Power
- ProcessScheduling
- Registry
- Display
- Driver
- Startup
- Network
- Storage
- GameProfile

System changes should become timeline evidence automatically.

## Optimization action contract

Every tunable feature should ideally expose:

```text
DetectCurrentState()
Explain()
CreateSnapshot()
Apply()
Verify()
Rollback()
```

The UI should expose at least:

```text
Current
Recommended
Risk
Reversible
Why
```

Do not present unsupported tweaks as universal performance improvements.

## Suggested first implementation order

1. Create `CrashScope.Core` class library.
2. Create `CrashScope.Desktop` WPF project.
3. Move/share only the models needed by the UI; avoid a large refactor at once.
4. Implement `CrashScopePathProvider`.
5. Implement safe readers for session state and JSONL.
6. Build Recorder Status Card.
7. Build CPU/GPU/RAM cards.
8. Add live tail reader and first chart.
9. Implement IncidentRepository.
10. Implement Incident List and Incident Detail shell.
11. Implement TimelineBuilder.
12. Only then begin richer modules.

## UI inspiration boundary

A reference application can guide:

- navigation layout
- cards
- sensor grouping
- tray behavior
- compact overlays
- theme ideas

CrashScope should preserve its own information architecture and visual identity. Do not make the UI a pixel-for-pixel clone of another project.
