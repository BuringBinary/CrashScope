# A-Phase 0 Execution Log (MCP, 2026-09-17)

Companion to `mcp-development-plan.md` (Workstream A, Phase 0) and `mcp-code-review.md`.
Records what was actually done, what was already done by in-flight uncommitted work, and what
is deliberately left open. Scope agreed with the user: **H1-H5 + M1-M11**, commit the in-flight
work separately, **prepare but do not deploy**.

## Result

- Build: **5/5 projects green** in Debug and Release, **0 warnings, 0 errors**.
- Tests: **146 passed / 0 failed** (was 104 planned, 128 discovered, 0 runnable - the test
  project did not compile).
- `get_diagnostics`: 0 items.
- Artifact: `dist/win-x64/CrashScope.Agent.exe`, self-contained win-x64, 89 MB, 220 files,
  reports `0.9.0`. Smoke-tested with `--help` only.
- **Not deployed**: no `%LOCALAPPDATA%\CrashScope`, no `%ProgramData%\CrashScope`, no
  scheduled task, no service. Installation needs host-local confirmation and was explicitly
  out of scope.
- **No git commit was made** (user opted to commit themselves). Note that the repository has
  no `user.name` / `user.email` configured, so committing will fail until that is set.

## Starting state (measured, not assumed)

The working tree already contained substantial uncommitted work: 27 modified files plus new
trackers (`DwmStateTracker`, `GpuMemoryTracker`, `MemoryPressureTracker`), Desktop
`Locales/`, `Services/`, `Themes/`, five new test classes, and the three `docs/mcp-*.md`
files. Several `mcp-code-review.md` findings were therefore already fixed. Every finding was
re-verified against the working tree before acting on it.

The build was **red** on arrival, which blocked everything else:

| Project | State | Cause |
|---|---|---|
| Core | OK | - |
| Agent | OK | - |
| Service | **CS0246** `BackgroundService`, `ILogger<>` | plain `Microsoft.NET.Sdk`; only the Worker SDK injects the `Microsoft.Extensions.*` implicit usings the code relies on |
| Desktop | **CS0104** `Application` ambiguous | `UseWPF` + `UseWindowsForms` both on, and `ThemeService.cs` had no alias |
| Tests | **CS7036** x many | `Evaluate` / `Build` grew required parameters and only some call sites were updated |

## Finding-by-finding status

### Already fixed by the in-flight work (verified, not touched)

| ID | Finding | Verification |
|---|---|---|
| H1 | `MONITORINFOEX.cbSize` never set | `DisplayTopologyTracker.cs` sets `cbSize = Marshal.SizeOf<MONITORINFOEX>()` |
| H2 | Enumeration failure indistinguishable from "zero monitors" | `Enumerate()` returns `null` on failure; `Poll()` keeps the previous topology and also guards a sudden empty result |
| H3 | Fixed ETW session names collide between recorder instances | session names now carry `Environment.ProcessId` and the session directory name |
| H5 | WMI disk-model query per disk per capture | `LoadDiskModels()` runs once; `_modelsByDiskIndex` cache |
| M3 | Per-disk IO attribution broken | `DiskIndexRegex` + `ExtractInstanceIndex`; each counter consumed exactly once per capture |
| M9 | `publish.ps1` used `--self-contained false` | now `--self-contained true` |
| M10 (part) | `install-service.ps1` had no elevation check | Administrator check present and exits non-zero |
| tests | storage evidence, `GpuTransitionDetector`, `ProcessTracker`, `GraphicsContextBuilder`, `DisplayTopologyTracker` statics | five test classes present |

### Fixed in this pass

| ID | Change | Files |
|---|---|---|
| build | Service moved to `Microsoft.NET.Sdk.Worker`; `Microsoft.Extensions.Logging.EventLog` referenced explicitly | `CrashScope.Service.csproj` |
| build | `using Application = System.Windows.Application;` alias | `Services/ThemeService.cs` |
| build | Optional evidence parameters given `= null` defaults so existing call sites compile | `HypothesisEvaluator.cs`, `TimelineBuilder.cs` |
| bug | Duplicate EventLog sink: `AddEventLog()` was called twice, so every message was written under two providers | `Service/Program.cs` |
| H4 | Implemented the redaction the rules file claimed: new `ExportRedactor` with a per-export vocabulary giving consistent `[PROCESS_n]` tokens, `[USERPROFILE]\[USERNAME]`, `[HOSTNAME]`, `[IP_ADDRESS]`. Removed the dead window-title lookbehind regex and corrected the false "window titles" claim in `redaction-rules.txt` (CrashScope never records titles). | `ExportRedactor.cs` (new), `DiagnosticExportService.cs` |
| M1 | `volatile bool _stopping` checked at the top of `Tick()`; catch-all around the recording loop so one bad tick cannot fault the task and leak the thread; `CrashSafeJsonlWriter.Write` is a no-op after dispose and `Dispose` is idempotent | `RecorderEngine.cs`, `CrashSafeJsonlWriter.cs` |
| M2 | GPU facts now resolve against the **discrete** adapter. LHM labels both adapters `GpuAmd` here and enumerates the iGPU first, so a "first GPU wins" or "most sensors wins" rule picks the wrong one; ranking is explicit-Discrete, then has a Fan/Control sensor (an iGPU is cooled by the CPU cooler and never exposes one), then has a Power sensor, then unlabelled, then explicit-Integrated. `RecorderEngine.PrimaryGpuHardwareName` exposes the choice so hosts can print it. | `SensorIndices.cs`, `RecorderEngine.cs`, `Agent/Program.cs` |
| M4 | `DwmStateTracker` dead branches removed (both descriptive branches were unreachable: `_lastCompositionEnabled` was overwritten before being compared). The `IsHardwareRendering` copy is kept for JSONL schema stability but is now documented as derived, not measured, together with the honest limitation. | `DwmStateTracker.cs` |
| M5 | ETW in-memory hand-off queues bounded at 5000 with drop-oldest and a `DroppedEventCount` counter. The JSONL file remains the durable record, so a stalled tick (disk saturation - the crash scenario) no longer grows the recorder without limit. | `EtwDxgKrnlMonitor.cs`, `EtwRealTimeMonitor.cs` |
| M6 | `Agent/Program.cs` rewritten as a thin host over `RecorderEngine`: one recording path for CLI / Service / Desktop, so the CLI agent now starts all three ETW real-time sessions. Added `--data-root=`, `--help`, a `ProcessExit` shutdown hook, and Ctrl+C takeover (letting the process be killed would leave the journal unclean and fabricate an incident). | `Agent/Program.cs` |
| M7 | All five `Version` properties unified to `0.9.0`; the agent banner now reads the assembly version instead of a hard-coded "v0.8". Dead `_storageCounter` removed. Because `SessionJournal.AgentVersion` reads the assembly version, incident metadata was recording the wrong version. | all `.csproj`, `Agent/Program.cs`, `RecorderEngine.cs` |
| M8 | `RetentionService` moved to `CrashScope.Core.Infrastructure` and enforced from `RecorderEngine` on a daily tick, so Service-only and Agent-only deployments are bounded too. Deliberately **not** run at `Start()`: the live session directory must never be a deletion candidate, and the previous session's incident is collected right after `Start()`. | `Infrastructure/RetentionService.cs` (moved), `RecorderEngine.cs`, `MainWindow.xaml.cs`, `SettingsPage.xaml.cs`, `RetentionServiceTests.cs` |
| M10 | README deploy section rewritten: scheduled task (user session, `RunLevel Highest`) is the recommended mode, with a comparison table and an explicit statement of what Session 0 costs (`GetForegroundWindow` and `LASTINPUTINFO` are meaningless there). Also warns against running both modes at once - they share `state\last-session.json` and would corrupt each other's unclean-session detection. | `README.md` |
| M11 | `TimelineBuilder.AddTelemetryEntry` resolves CPU / GPU / RAM through `SensorIndices` against the session's sensor catalog instead of guessing from value shape. The old heuristic labelled the first value in [0,100] as "CPU" (GPU load qualifies) and the last value above 100 as "RAM" in MB (a temperature in Celsius qualifies). With no catalog it now emits nothing rather than guessing. | `TimelineBuilder.cs`, `SensorIndices.cs` |

### Fixed outside the original list (found while making the build green)

These were not in `mcp-code-review.md` and each one silently degraded evidence quality.

1. **VRAM / memory-pressure evidence fired on any non-zero sample.** `AddVramEvidence` emitted
   weight-2 "high VRAM usage" and weight-2 "shared memory spillover" whenever a value was
   merely present, and `AddMemoryPressureEvidence` emitted weight-1 "commit charge peaked"
   plus weight-2 "extreme memory pressure" for any sample at all. An ordinary desktop session
   (2 GB VRAM, 8 GB free RAM, 62% commit) therefore produced HardFreeze evidence on every
   incident, inflating the score and flattening the ranking - the exact thing the weighted
   evaluator exists to avoid. Now threshold-based and tiered, with mutually exclusive VRAM
   tiers so one peak is not double-counted:

   | Signal | Threshold | Weight |
   |---|---|---|
   | dedicated VRAM peak | >= 10 GB | 3 ("VRAM exhaustion") |
   | dedicated VRAM peak | >= 7 GB | 2 ("approaching VRAM limit") |
   | dedicated+shared peak | >= 12 GB and > dedicated | 2 ("shared memory spillover") |
   | commit charge peak | >= 95% | 3 ("approaching commit limit") |
   | commit charge peak | >= 90% | 2 ("commit charge was high") |
   | available physical RAM | <= 256 MB | 3 ("critically low available RAM") |
   | available physical RAM | <= 512 MB | 2 ("extreme memory pressure") |

   The thresholds were reverse-engineered from the expectations already encoded in
   `HypothesisEvaluatorEtwTests` (six of which were failing).

2. **`MainWindow._forceClose` was read but never assigned** (CS0649). `OnClosing` therefore
   always cancelled and hid to tray, so the tray "Exit" item never reached `_tray.Dispose()`:
   the process exited via `App.OnExit` leaving a **ghost tray icon** behind, and `AppService`
   was disposed twice. Added `MainWindow.RequestExit()`, routed `TrayIconManager.OnExit`
   through it, and overrode `Application.OnSessionEnding` so Windows logoff/shutdown closes
   cleanly instead of minimising to tray. Alt+F4 and the title-bar close button still minimise,
   which is the intended behaviour.

3. **`DwmStateEvents_AppearAsDwmCategory` could never pass**: the timeline printed
   `Composition: disabled, HW rendering: no`, while the test asserts the description contains
   "software rendering". The timeline now says `DWM composition disabled - fell back to
   software rendering`, and no longer prints the derived `IsHardwareRendering` flag as if it
   were an independent measurement.

4. **Test typo**: `GraphicsContextBuilderTests` used the process name `toodesk` (two o's) while
   `GraphicsAppCatalog` keys `todesk`, which is what the real ToDesk client is named. The test
   failed with a confusing `Sequence contains no matching element`.

5. **README data-structure listing was wrong**: it documented `etw-power.jsonl` and
   `etw-power-tail.jsonl`, but the recorder writes `etw-kernel-power.jsonl` and the collector
   tails `etw-kernel-power-tail.jsonl`. The code path was self-consistent; only the document
   was wrong.

## Repository hygiene

- 21 stale artifacts **moved out of the repository, not deleted** (they are untracked or were
  tracked, and the user is doing the committing): `D:\workspace\workspace\.stale-artifacts-20260917\`.
  19 UTF-16LE build/test logs plus `TestResults/` from the CrashScope root, and
  `build-output.txt` / `test-output.txt` from the workspace root. Four of the CrashScope logs
  were git-tracked, so they show as deletions in `git status`.
- `.gitignore` extended: `TestResults/`, `*.trx`, `*.coverage`, `artifacts/`, `packages/`, and
  the ad-hoc `build-*.txt` / `test_*.txt` / `all_output.txt` capture patterns that caused the
  pollution in the first place.
- `packages/` is 132 MB of untracked legacy packages.config-style output. It is **not** a
  restore source (restore resolves from `%USERPROFILE%\.nuget\packages`), so it is ignored
  rather than deleted. Deleting it would make an offline restore impossible for anyone who
  was relying on it; that call is left to the user.
- CI (`.github/workflows/build.yml`) now restores, builds and publishes all five projects,
  runs `CrashScope.Core.Tests`, and uploads the TRX results on `always()`. Triggers extended
  to `feature/local-desktop-work` so this branch is actually validated. Previously CI built
  the Agent only and never compiled Desktop / Service / Tests - which is exactly why two of
  them were red on arrival.

## Tests added

| File | Tests | Covers |
|---|---|---|
| `SensorIndicesTests.cs` | 9 | discrete-adapter preference (incl. the real `GpuAmd` + iGPU-first catalog shape), explicit `GpuDiscrete` labelling, deterministic fallback, integrated-only machines, physical RAM vs commit charge, null/empty catalog |
| `TimelineTelemetryTests.cs` | 6 | catalog-driven CPU/GPU/RAM labels, GPU load no longer mislabelled as CPU, a temperature no longer mislabelled as RAM, no-catalog emits nothing, RAM-used-only, out-of-range catalog index |
| `CrashSafeJsonlWriterTests.cs` | 3 | append semantics, post-dispose write is a no-op instead of throwing, dispose is idempotent |

128 -> **146** tests, all passing.

Not covered by automated tests, and why:

- **M5 queue bound** - `MaxBufferedEvents` is a `const` and the enqueue path is only reachable
  from a live `TraceEventSession`, so it cannot be driven without ETW. Would need the cap
  injected or a fake transport; deferred rather than contorting production code for it.
- **M1 shutdown race** - inherently timing dependent; the deterministic parts (post-dispose
  write tolerance, idempotent dispose) are tested, the race itself is not.
- **H1 Win32 path** - `GetMonitorInfo` needs a real interactive desktop. The pure statics are
  covered by `DisplayTopologyTrackerTests`; the live path belongs to Phase 1 Test C.

## Ground-truth corrections to earlier docs

`mcp-project-overview.md` states that this host has no .NET SDK and that local build/test is
impossible. **That is no longer true** (and may never have been): the SDK is installed
per-user at `C:\Users\BuringBinary\.dotnet\` - SDK 8.0.425 with
`Microsoft.WindowsDesktop.App` / `Microsoft.NETCore.App` 8.0.31. It is simply not on `PATH`,
so `dotnet` appears missing to a bare shell. Use:

```bash
export PATH="$PATH:/c/Users/BuringBinary/.dotnet"
```

This is what unblocked the whole of Phase 0: the green build + test baseline was produced
locally, and `dist/win-x64` was published locally, with no CI round trip. The overview's
"61 C# files / ~10,400 LOC" and the review's "104 tests in 9 classes" counts are also stale;
the current figures are 66 source files (~8,300 lines) plus 2,700 lines of tests in 17
classes, 146 tests.

Host confirmed as the Phase 1 target: `DESKTOP-KM1JRPS`, AMD Radeon RX 9070 XT
(driver 32.0.31041.1004) + AMD Radeon iGPU (32.0.31045.5002) + GameViewer Virtual Display
Adapter. `SensorIndices.PrimaryGpuHardwareName` was verified against the live catalog to
resolve to `AMD Radeon RX 9070 XT`, with a live read of CPU 21.9% / GPU 13.0% /
RAM 11.5 GB used of 23.1 GB.

## Open items

1. **U+FFFD mojibake in evidence strings - needs a decision.** `HypothesisEvaluator.cs`
   contains 25 U+FFFD replacement characters where em dashes used to be (for example
   `"Event 6008 \uFFFD?Windows recorded an unexpected shutdown"`). They are present in the
   committed HEAD as well as the working tree, so this is an old encoding accident rather than
   recent damage, and the files are otherwise valid UTF-8. These strings are written into
   `summary.md` and `timeline.md`, so the incident bundle - the project's whole deliverable -
   contains garbage characters. Left alone deliberately: a blanket fix is easy
   (`\uFFFD?` -> ` - `) but touches 25 evidence strings that tests match on substrings of, and
   it is cosmetic rather than functional. Recommend doing it as its own small commit.
   The rest of the non-ASCII in the tree is intentional (Chinese UI strings, box drawing,
   `->` and `>=` in test section headers) and was verified byte-exact.
2. **L1-L14 deferred** by the agreed scope. Still accurate in `mcp-code-review.md` except
   L13 (repo hygiene, done) and L14 (CI, done). L5's stale comment in
   `GraphicsContextBuilder` and L9's unannotated magic ETW event ids are the cheapest wins.
3. **`_storageCounter` removal** means the storage capture interval is driven by
   `_displayCounter` (every 5 s). That is pre-existing behaviour, unchanged here, but worth
   knowing: storage and display share one cadence.
4. **Phase 1 is unblocked.** The remaining A-Phase 0 item is the commit itself (the user is
   doing it; the repository needs a git identity first). After that: install the scheduled
   task from `dist/win-x64`, then Tests A-D per `mcp-development-plan.md`. Test A in
   particular now has a real chance of passing, because the Ctrl+C / `ProcessExit` path marks
   the journal clean and the Desktop exit path no longer leaves the recorder running.
