# Code Review (MCP, 2026-09-17)

Scope: full read of `CrashScope` src + test (61 C# files, ~10,400 LOC), deployment scripts, CI, and the test inventory. Desktop page code-behind was reviewed at structure level (threading via grep) in addition to all service classes. Companion docs: `mcp-project-overview.md`, `mcp-development-plan.md`.

> **Status (2026-09-17, post-execution):** H1-H5 and M1-M11 are closed.
> [`mcp-phase0-changes.md`](mcp-phase0-changes.md) records, finding by finding, which were fixed
> in that pass and which had already been fixed by the in-flight uncommitted work. L1-L14 were
> deferred by the agreed scope, except **L13** (repo hygiene) and **L14** (CI), both now done.
> Counts quoted below are as found on arrival and are stale: the current tree is 66 source files
> (~8,300 LOC) plus ~2,700 LOC of tests in 17 classes, **146 tests**, all passing.
>
> **Update (2026-09-17, second pass):** L1, L2, L3, L7, L8, L10, L11 and L12 are closed.
> L4's duplicate firing is fixed (its English strings stay for the i18n pass). L6 is partially
> closed: the single-incident label and the always-"Worsening" 3-incidents trend are fixed,
> while the iGPU driver-correlation filter needs an `IsDiscrete` flag captured with the driver
> inventory - name-based filtering would reintroduce the guessing M11 removed. Tests 146 -> 148.

## Overall assessment

A solid, defensive, privacy-conscious codebase. The core design (append-only write-through JSONL, atomic session state, evidence-weighted hypothesis scoring with an honest `Unclassified`, per-feature graceful degradation) is well executed and the code is consistently clean (records, small classes, generous try/catch with `IsAvailable` flags).

The risk concentrates in five places:
1. **Real functional bugs that would corrupt evidence quality on exactly this machine** (dGPU + iGPU + virtual display) - H1/H2/M2/M3.
2. **Two divergent recording paths** (Agent inline loop without ETW vs `RecorderEngine`) - M6.
3. **A shutdown race in `RecorderEngine`** and an **ETW session-name collision** between concurrent recorder instances - M1/H3.
4. **The diagnostic export redacts less than its own rules file claims** - H4.
5. **A performance pitfall in `StorageTracker`** (WMI query per disk per capture) - H5.

Nothing here blocks a *first* field deployment badly, but H1-H5 should be fixed during Phase 0 because each one silently degrades exactly the evidence the project exists to capture.

## Findings

### HIGH

**H1. `MONITORINFOEX.cbSize` is never set** - `DisplayTopologyTracker.cs:114-115`
`var info = new MONITORINFOEX(); GetMonitorInfo(hMonitor, ref info)` is called with `cbSize == 0`. `GetMonitorInfo` requires `cbSize = sizeof(struct)`; on failure the whole per-monitor block is skipped, so `Enumerate()` would return an **empty** topology on most Windows versions. The display-topology stream - a top-weight evidence category (display change +2, virtual display +1) - may be silently producing nothing. Fix: `info.cbSize = Marshal.SizeOf<MONITORINFOEX>();` before the call; add a live-session smoke assertion during Test C.

**H2. Display enumeration failure is indistinguishable from "zero monitors"** - `DisplayTopologyTracker.cs:95-108`
`Enumerate()`'s catch returns an empty list; the comment says "report the empty topology change-free" but `Poll()` then sees `previous != current` and emits a `changed` event listing every monitor as removed. One transient enumeration failure injects high-weight fake evidence ("display reconfiguration shortly before the end"). Fix: return `null` on enumeration failure and keep the previous topology.

**H3. Fixed ETW session names collide between recorder instances** - `EtwDxgKrnlMonitor.cs:121-130`, `EtwRealTimeMonitor.cs:158-179`
Both monitors use constant session names (`CrashScope-GpuKernel`, `CrashScope-SystemRealtime`) and `CreateSession()` disposes any existing session with the same name. If two recorder instances run (Service + Desktop both started, or agent + service), the later start **kills the earlier instance's live ETW session** - silent loss of kernel-level evidence, exactly in the crash scenario. Fix: suffix session names with the session id / PID and never dispose sessions you did not create.

**H4. Diagnostic export redaction under-delivers vs its own rules file** - `DiagnosticExportService.cs:137-173`
`redaction-rules.txt` claims: process names -> `[PROCESS_N]`, usernames in paths -> `[USERNAME]`, machine names -> `[HOSTNAME]`. `RedactContent()` only implements `C:\Users\*`, IPv4, and a loose window-title regex. Process names (graphics apps, WER `AppName`, process-event tails) pass through unredacted, so the "privacy-safe" ZIP is less private than advertised. Additionally, window-title redaction is dead weight (CrashScope never records titles) and its lookbehind regex risks corrupting real text. Fix: implement the claimed rules (consistent per-process mapping) or rewrite the rules file to match reality; drop the title regex.

**H5. `StorageTracker.FindDiskModel` runs a WMI query per disk, per capture** - `StorageTracker.cs:134, 184-205`
`Capture()` is called every 5 s; for each LHM storage hardware it queries `Win32_DiskDrive` via WMI (~100-500 ms per round trip). This is the slowest operation in the tick path, running 12x/min per disk. Disk models do not change at runtime. Fix: resolve models once in the constructor and cache.

### MEDIUM

**M1. `RecorderEngine.Stop()` shutdown race** - `RecorderEngine.cs:329-374`
`Stop()` waits at most 3 s for the recording task, then disposes all writers, disposes hardware, and nulls trackers - while `Tick()` may still be mid-pass in a slow LHM/WMI/perf-counter cycle. `Tick` has no disposed-state guard and the task only catches `OperationCanceledException`, so a slow tick faults the task unobserved and leaks the thread. Fix: volatile `_stopping` flag checked at `Tick` start, a catch-all in the loop, and writers that tolerate post-dispose writes.

**M2. `GpuCoreLoad` picks the first "Core" Load sensor of the first GPU in catalog order** - `SensorIndices.cs:7-9`
On this host (RX 9070 XT + AMD iGPU) the iGPU can be `Gpu0`. Then `FinalGpuLoadPercent` (SessionFacts), the `HighGpuLoad` cross-incident pattern, and GPU-collapse evidence all refer to the **wrong adapter**. Fix: prefer the discrete adapter (name match, or the adapter exposing VRAM/Power sensors) and record which adapter the fact came from in the report.

**M3. StorageTracker per-disk IO attribution is effectively broken** - `StorageTracker.cs:141-168, 207-213`
`ExtractDiskIndex` concatenates *all* digits of the name - model strings with digits (`ST1000DM010-2EP102`) parse to a huge int or fail; the containment fallback then fails; every disk falls back to `AggregateMax` across all disks. Worse, when one disk matches and another doesn't, the aggregation pass re-consumes the matched disk's ratio counters within the same capture, so the second `NextValue()` returns ~0. Net effect: "per-disk" storage evidence is actually all-disk aggregate, sometimes zeroed. Fix: parse the `(Disk N)` index from the LHM name, precompute instance->disk mapping once, and consume each counter exactly once per capture.

**M4. `DwmStateTracker` conflates "composition enabled" with "hardware rendering"** - `DwmStateTracker.cs:15-16`
`isHardwareRendering = compositionEnabled` - the field is a copy, not a measurement. The v0.9 failure mode "DWM fell back to software rendering *while composition stays on*" is not detectable as implemented; the redundant field misleads readers of `dwm-state.jsonl`. Fix: either probe the real rendering mode or drop the fake field and document the limitation (the evaluator's compound condition reduces to `!compositionEnabled` anyway).

**M5. The documented 64 MB ETW ring is not implemented; the in-memory queue is unbounded** - `EtwDxgKrnlMonitor.cs:14, 114`
Events are durably written to file on the processing thread (good), and additionally enqueued into an unbounded `ConcurrentQueue` drained by the recorder tick every 3 s. If the tick stalls (disk/IO saturation - the crash scenario itself), the queue grows without bound, i.e. the flight recorder eats memory when it is most needed. Fix: bound the queue (drop-oldest); the file already provides durability.

**M6. Two divergent recording paths** - `Agent/Program.cs` vs `RecorderEngine.cs`
The CLI agent runs an inline loop with **no ETW sessions** and a different data root (`%LOCALAPPDATA%` vs `%ProgramData%` for the service); the banner says "v0.8". A field deployment via the CLI agent silently lacks all v0.5/v0.6 kernel evidence. Fix (already planned): make the Agent a thin host over `RecorderEngine`.

**M7. Version drift** - README v0.9; csproj `Version` 0.6.0 (Core/Agent/Desktop) / 0.4.0 (Service/Tests); agent banner "v0.8"; `SessionJournal.AgentVersion` reads the assembly version, so **incident metadata records the wrong version**. Unify to 0.9.0 everywhere.

**M8. Retention is desktop-only** - `MainWindow.xaml.cs:36,63-65`, `SettingsPage.xaml.cs:130`
`RetentionService.Enforce()` runs only when the Desktop app starts or the user touches Settings. A service-only deployment grows `sessions/` + ETW files unboundedly. Fix: move retention into Core and invoke it from `RecorderEngine.Start()` (plus a daily tick).

**M9. `publish.ps1` uses `--self-contained false`** while CI publishes self-contained; the README points field users at `publish.ps1`. The target machine would then need the .NET 8 Desktop runtime installed. Unify on self-contained.

**M10. `install-service.ps1` has no elevation check** and its deployment path (session 0) silently loses foreground-window and `LASTINPUTINFO` fidelity (`GetForegroundWindow` from a service session returns nothing). Self-elevate or detect, and document the session-0 limitation in the README deploy section.

**M11. `TimelineBuilder.AddTelemetryEntry` mislabels sensors** - `TimelineBuilder.cs:162-189`
"CPU:" is the first non-null value in [0,100] (GPU load qualifies too) and "RAM:" is the last value >100, divided as if MB. In a forensic timeline the labels can lie. Fix: use catalog indices (`SensorIndices`) instead of value-shape heuristics.

### LOW

- **L1.** `GpuEngineTracker` has no `IDisposable`; its `PerformanceCounter` set is never disposed (bounded by live GPU instances, but it matters for multi-day service runs).
- **L2.** `TrayIconManager.CreateIcon` never calls `DestroyIcon` on the `GetHicon()` handle (3 GDI handles per app launch) - `TrayIconManager.cs:159-161`.
- **L3.** `TrayIconManager.ShowUncleanSessionBalloon` re-subscribes `BalloonTipClicked` on every call (handler accumulation); `OnStatusChanged` touches the WinForms `NotifyIcon` from the recorder thread (works in practice, should be marshalled) - `TrayIconManager.cs:72-93`.
- **L4.** Desktop `GpuTransitionMonitor` is level-triggered: while a delta persists, a new "transition" fires every second and up to 50 near-duplicates accumulate in `RecentTransitions`; descriptions are hard-coded English (no localization) - `GpuTransitionMonitor.cs:57-103`.
- **L5.** `GraphicsContextBuilder.cs:83` comment ("A stop event after the final snapshot means the app was running at the end") contradicts the code that follows; the running-at-end logic is a 30 s-granularity heuristic - fine, but fix the comment.
- **L6.** `CrossIncidentAnalyzer` quirks: iGPU driver entries pollute driver correlation (every incident lists both adapters); a single incident is labeled "Stable" rather than "insufficient data"; with exactly 3 incidents the trend is always "Worsening" (first half has one point -> rate 0) - `CrossIncidentAnalyzer.cs:291-433`.
- **L7.** `CorrelateDrivers` resolves vendor/date by version string only, ignoring the adapter - two adapters sharing a version can swap metadata - `CrossIncidentAnalyzer.cs:321-322`.
- **L8.** `SessionJournal` persists `CleanShutdown=false` immediately in the constructor, so an instant crash before the first heartbeat yields a near-empty incident. Acceptable false positive, but consider a `neverStarted` marker the collector can skip - `SessionJournal.cs:20-29`.
- **L9.** `EtwDxgKrnlMonitor.IsInteresting` uses magic event ids (250/253/400/497/500/600/700/800) with no comments - unauditable - `EtwDxgKrnlMonitor.cs:135-140`.
- **L10.** `GpuTransitionDetector` labels `Data`/`SmallData` sensors as "GB" but LHM data-sensor units vary by hardware (some report MB) - verify per adapter - `GpuTransitionDetector.cs:24-28`.
- **L11.** `MemoryPressureTracker` returns a zeroed sample when `GlobalMemoryStatusEx` fails - indistinguishable from a real zero (acceptable; note it). `ullTotalPhys` is unused.
- **L12.** `ProcessTracker.Poll` emits "start" without a matching "stop" when a PID's name changes (PID reuse / rename) - minor semantic noise.
- **L13.** ~~Repo hygiene: 13 stale UTF-16 build/test logs + `TestResults/` at the CrashScope root; the test project commits `obj/` artifacts (dirty in git status); `.gitignore` is 76 bytes and incomplete.~~ **Done.** 21 artifacts moved out of the repository to `D:\workspace\workspace\.stale-artifacts-20260917\` (moved, not deleted); `.gitignore` rewritten (28 lines, adds `TestResults/`, `packages/`, `artifacts/`, `*.trx`, `*.coverage`, and the ad-hoc capture patterns). Verified no `obj/` or `bin/` paths are tracked - the `obj/` dirt was untracked working-tree output.
- **L14.** ~~CI builds only the Agent project; Desktop/Service/Tests are never compiled in CI and no test job exists.~~ **Done.** CI now restores, builds and publishes all five projects, runs `CrashScope.Core.Tests`, and uploads TRX on `always()`; triggers include `feature/local-desktop-work`. This gap is precisely why two projects were red on arrival.

## Test coverage (104 tests in 9 classes as planned; **128 in 16 classes as found**; **146 in 17 classes now**)

Strong: `CrossIncidentAnalyzerTests` (28), `HypothesisEvaluatorEtwTests` (22 - includes VRAM, memory-pressure, and DWM evidence, contrary to an earlier assumption in the plan doc), `TimelineBuilderEtwTests` (14), `ModelsJsonRoundtripTests` (10), `EtwModelJsonRoundtripTests` (7), `SessionJournalTests` (6), `RetentionServiceTests` (5), `DiagnosticExportRedactionTests` (5), `SensorHistoryBufferTests` (7).

Gaps worth closing in Phase 0:
- **Storage evidence path** (`AddStorageEvidence`, `StorageTracker` attribution) - untested (and broken per M3).
- `GpuTransitionDetector` - pure function, trivially testable, untested.
- `GraphicsContextBuilder` / `BuildAppPresence` - untested.
- `ReportGenerator` output shape - untested.
- `ProcessTracker` - untested (pure, easy).
- `DisplayTopologyTracker` statics (`SameTopology`, `Describe`, `IsVirtualDisplay`) - untested; the Win32 path at least needs a live smoke check given H1.

## What is done well (keep)

- `CrashSafeJsonlWriter`: lock + `WriteThrough` + `Flush(flushToDisk:true)`; readers tolerate torn final lines.
- `SessionJournal`: atomic tmp+rename persistence; elevation detection.
- `ExternalEvidenceScanner`: guarded enumeration, WER value truncation + control-char filtering - privacy-aware by construction.
- `HypothesisEvaluator`: opposing evidence, confidence mass-cap, honest `Unclassified`, per-dimension open questions.
- Per-feature graceful degradation everywhere (perf counters, ETW, WMI, Win32 all `try/catch` + `IsAvailable`).
- UI event marshalling via `Dispatcher.Invoke` on all pages.
- The privacy boundary is enforced at capture time (no command lines, window titles, paths, or content).

## Recommended fix order (fits Phase 0, est. 1.5-2.5 days)

1. H1 + H2 (display tracker, one small patch + statics tests)
2. H3 (ETW session naming)
3. H5 (WMI model caching) + M3 (attribution) - same file
4. H4 (redaction: implement or unclaim)
5. M1 (shutdown race) + M6/M7 (unify paths + versions, already planned)
6. M2, M4, M5, M8, M9-M11, then LOW items

