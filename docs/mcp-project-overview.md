# Project Overview - MCP Read-Through (2026-09-17)

Companion to `architecture.md` (v0.1 design). Produced by a remote agent reading the workspace via the ShunCode bridge; describes the tree as found, including build/tooling state.

> **Status update (later the same day):** this describes the tree on arrival, and the
> build/tooling section below is now superseded. See [`mcp-phase0-changes.md`](mcp-phase0-changes.md)
> for the executed Phase 0 result - in particular the "no .NET SDK" claim was wrong.

## Workspace layout

- `CrashScope/` - the project. Git repo, single commit `458c877` ("feat: v0.5-v0.8 ...").
- `NexBox-reference/` - vendored reference app: NexBox v9.5.3 (GPL-3.0, Tauri 2.10 + React 19 + Rust), a one-stop toolbox for PC gamers (hardware monitoring, system tuning, display enhancement, game helpers, media). It bundles the same `LibreHardwareMonitorLib.dll` CrashScope uses - useful reference for hardware-monitoring patterns and UI ideas.

## CrashScope in one paragraph

A Windows crash "black box": a user-mode recorder that keeps 1 Hz hardware telemetry plus foreground / process / GPU-engine / display / power / storage / per-process-VRAM / memory-pressure / DWM / ETW trails in append-only write-through JSONL. On next launch, if the previous session did not shut down cleanly, it gathers Windows event-log and external evidence (WER / LiveKernelReports / minidumps), rebuilds a unified timeline, scores six failure hypotheses (hard freeze, BSOD, TDR, WHEA, app crash, power loss) with weighted evidence-for/against items, and writes an incident bundle (`analysis.json` / `timeline.md` / `summary.md`). Status per README: v0.9 code complete, awaiting validation on the real AMD machine.

## Modules (61 C# files, ~10,400 LOC)

| Project | Role | Key files |
|---|---|---|
| `Core` | Monitoring + analysis library (UI-free) | `Analysis/HypothesisEvaluator.cs` (6-hypothesis weighted scoring, 673 lines), `Analysis/CrossIncidentAnalyzer.cs` (v0.7 pattern mining, driver correlation, MTBF/trends), `Analysis/RecommendationEngine.cs`, `Analysis/TimelineBuilder.cs`, `Analysis/ReportGenerator.cs`, `Monitoring/RecorderEngine.cs` (orchestrates all trackers + ETW sessions), 9 tracker classes, `Monitoring/EtwDxgKrnlMonitor.cs` + `EtwRealTimeMonitor.cs` (3 ETW providers: DxgKrnl / Kernel-Power / WHEA), `Diagnostics/CrashEvidenceCollector.cs` (event-log rules + tail copy), `Infrastructure/CrashSafeJsonlWriter.cs` (WriteThrough + FlushToDisk), `Models/Records.cs` (all JSONL records) |
| `Agent` | CLI / standalone recorder | `Program.cs` - inline 1 Hz top-level loop (legacy path) |
| `Service` | Windows Service host | `SessionRecorderWorker` -> `RecorderEngine` (data root under `C:\ProgramData\CrashScope`) |
| `Desktop` | WPF dashboard (tray, OxyPlot charts, incident viewer, timeline, settings; en-US / zh-CN locales, dark/light themes) | `AppService` hosts `RecorderEngine`; `Pages/` (Dashboard, Incidents, LiveMonitor, RecorderStatus, Settings, Timeline), `DiagnosticExportService` (privacy redaction), `RetentionService`, `TrayIconManager` |
| `Tests` | xunit, 9 test classes (~2,200 lines) | ETW JSON roundtrip, hypothesis scoring (incl. ETW signals), timeline building, cross-incident analyzer, export redaction, retention, session journal, sensor history buffer |

## Data flow

`SessionJournal` heartbeat / clean-shutdown marker -> next boot sees `CleanShutdown=false` -> `CrashEvidenceCollector.CollectForUncleanSession` (10-min pre-heartbeat window; provider->id rules in `SourceRules`; tails of all 15 session JSONL files) -> `IncidentAnalyzer.Analyze` (facts + GPU transitions + graphics context + 12-evidence `HypothesisEvaluator.Evaluate` + `TimelineBuilder`) -> incident bundle files.

## Build / tooling state

> Everything in this section is **superseded** by
> [`mcp-phase0-changes.md`](mcp-phase0-changes.md). Kept for the record with corrections inline.

- Target framework `net8.0-windows` everywhere; main packages: LibreHardwareMonitorLib 0.9.6, TraceEvent 3.1.15, OxyPlot.Wpf 2.2.0, xunit 2.9.3.
- CI (`.github/workflows/build.yml`): on push/PR to `main` restores, builds, and publishes **only the Agent project** (win-x64 self-contained) to an artifact. Desktop / Service / Tests are not built by CI. **Fixed:** CI now restores, builds and publishes all five projects, runs `CrashScope.Core.Tests` and uploads TRX results, and triggers on `feature/local-desktop-work` as well.
- The root `build-output.txt` / `test-output.txt` (and 11 sibling txt files) are **stale artifacts from another machine** (`H:\1\workspace`), UTF-16LE + CRLF, and partly GBK-mojibake. The reported `CS7036` (missing `FinalDiskMetrics` argument in `Signals`) no longer exists in this tree: current `HypothesisEvaluator.ExtractSignals` passes all 33 named parameters (`FinalDiskMetrics: finalDiskMetrics`, line 218). **Fixed:** 21 such artifacts (19 logs + `TestResults/` from the repo root, 2 logs from the workspace root) were **moved out of the repository** - not deleted - to `D:\workspace\workspace\.stale-artifacts-20260917\`, and `.gitignore` now covers `TestResults/`, `packages/` and the ad-hoc capture patterns.
- This host (DESKTOP-KM1JRPS) has **no .NET SDK** installed (no `dotnet` on PATH, no `C:\Program Files\dotnet`), so local build/test is not possible here; CI or the previous H:-drive machine is the build environment. **Wrong - corrected.** The SDK *is* installed per-user at `C:\Users\BuringBinary\.dotnet\` (SDK 8.0.425, `Microsoft.WindowsDesktop.App` / `Microsoft.NETCore.App` 8.0.31); it is simply absent from `PATH`, so a bare shell reports it as missing. With `export PATH="$PATH:/c/Users/BuringBinary/.dotnet"` this host builds all five projects with 0 warnings, passes 146/146 tests, and publishes `dist/win-x64` locally - no CI round trip required. That is what unblocked the whole of Phase 0.

## Observations / open items

1. Two parallel recording paths: `Agent/Program.cs` (inline loop, **no ETW sessions**, banner prints "CrashScope v0.8") vs `RecorderEngine` (used by Service + Desktop, includes the three ETW real-time sessions). If the CLI agent is the field-deployment target, it currently misses v0.5/v0.6 kernel-level capture.
2. Version drift: README says v0.9; csproj `Version` is 0.6.0 (Core/Agent/Desktop) and 0.4.0 (Service/Tests); agent banner says v0.8.
3. `CrashScope.Core.Tests` references the WPF `Desktop` project (redaction/retention/buffer tests need it) - test project is coupled to the UI project.
4. Repo root is polluted with stale UTF-16 build/test logs plus `TestResults/` (13 txt files at root); candidates for cleanup / `.gitignore`.
5. `RecorderEngine._storageCounter` is assigned but never read (prior warning CS0414) - dead counter.
6. ETW start failures are swallowed by design (non-admin graceful degradation), only traced to `System.Diagnostics.Trace` - invisible in the Service EventLog output.

## NexBox-reference quick map

- Frontend: `src/` React 19 + Chakra UI + i18next + zustand-style stores; Vite + Tauri 2.10.
- Backend: `src-tauri/src/` ~80 Rust modules - hardware/sensor (LibreHardwareMonitorLib P/Invoke), NVAPI, power plans, storage clean/scan, shader/display cache, crosshair + overlays, game helpers (Delta Force, Steam, VAC, anti-cheat), music/SMTC, network tools, tray, updater-style utilities.
- Extras: `installer/` + `uninstaller/` (Tauri helper apps), `msix/` packaging scripts, `power-plans/`, `aq_registry/` + `aq_registry_restore/` (registry backup/restore), `R560-developer/` (AMD R560 driver SDK samples: docs + x86/amd64 binaries).
