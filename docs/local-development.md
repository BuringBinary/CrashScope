# Local Development Start

This guide is the intended starting point for developing the expanded CrashScope locally.

## 1. Keep CrashScope and the reference repository separate

Recommended workspace:

```text
workspace/
├─ CrashScope/
└─ NexBox-reference/
```

Clone:

```bash
git clone https://github.com/BuringBinary/CrashScope.git
git clone https://github.com/MuLiuSaMa/NexBox.git NexBox-reference
```

Switch CrashScope to the planning/development branch:

```bash
cd CrashScope
git fetch origin
git switch feature/desktop-foundation
git pull
```

Do not copy the whole reference repository into the CrashScope tree. Use it for learning and research; implement CrashScope features in CrashScope's own architecture.

## 2. Read these files first

Recommended order:

1. `README.md` — understand the existing recorder.
2. `docs/architecture.md` — understand the current v0.1 pipeline.
3. `docs/development-plan.md` — understand the build order.
4. `docs/desktop-architecture.md` — understand Desktop/Core boundaries.
5. `docs/feature-matrix.md` — understand priorities/modules.
6. `docs/nexbox-feature-map.md` — full long-term reference-feature checklist.
7. `ROADMAP.md` — versions and milestones.

## 3. Run the existing Agent before adding UI

Requirements:

- Windows 10/11
- .NET 8 SDK

Run:

```powershell
dotnet run --project .\src\CrashScope.Agent\CrashScope.Agent.csproj
```

Or use the existing admin helper when elevated sensor/event access is needed:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-admin.ps1
```

Confirm data appears under:

```text
%LOCALAPPDATA%\CrashScope\
```

Before changing architecture, inspect the existing session/incident files and understand what is already recorded.

## 4. First implementation milestone

Create the shared domain library and desktop shell:

```powershell
dotnet new classlib -n CrashScope.Core -o .\src\CrashScope.Core
dotnet new wpf -n CrashScope.Desktop -o .\src\CrashScope.Desktop
```

The first feature should be the **Recorder Status Card**, not a system optimizer.

Target UI:

```text
CrashScope

Recorder
● RECORDING

Session
00:43:21

Last Sample
0.4 sec ago

Storage
Healthy
```

Suggested learning tasks:

1. locate `%LOCALAPPDATA%\CrashScope`
2. read `state/last-session.json`
3. define a typed model
4. deserialize JSON safely
5. expose it through a ViewModel
6. bind it in WPF
7. refresh state on a timer
8. tolerate missing/stale/corrupt files
9. avoid breaking while the Agent is writing

## 5. Then build in this order

```text
Recorder Status Card
  -> CPU/GPU/RAM cards
  -> current telemetry reader
  -> first live chart
  -> Sensor model cleanup
  -> IncidentRepository
  -> Incident list
  -> Incident Detail
  -> TimelineBuilder
  -> synchronized incident charts
  -> Diagnosis rules
```

After the diagnostic foundation is stable:

```text
Tray/Overlay
  -> Hardware suite
  -> System-change recorder
  -> Power/process/memory tools
  -> Display/graphics tools
  -> Driver workflows
  -> Game tools
  -> Toolbox integrations
  -> Product shell
  -> Delta Force content module
  -> Epic/audio/media suite
```

## 6. How to learn from the reference project

Preferred loop:

```text
See the reference feature/UX
  -> define what data/behavior it needs
  -> design your own CrashScope interface
  -> implement a first version yourself
  -> compare with the reference approach
  -> improve your implementation
```

Avoid:

```text
Find source -> copy/paste -> make it compile
```

The first approach trains architecture, debugging, Windows APIs, C#, MVVM, persistence, and system reasoning.

## 7. Feature completion source of truth

For the long-term project, use:

```text
docs/nexbox-feature-map.md
```

Every selected reference feature is listed there. A feature is not considered "lost" just because its implementation belongs to a later version.

## 8. Branching while learning

You can develop from the planning branch or create a personal child branch:

```bash
git switch feature/desktop-foundation
git pull
git switch -c feature/local-desktop-work
```

Commit small, understandable steps, for example:

```text
feat(core): add CrashScope path provider
feat(desktop): add recorder status view model
feat(desktop): render recorder status card
feat(core): add safe telemetry JSONL reader
feat(desktop): add GPU summary card
```

Small commits make it much easier to debug, review, and learn.
