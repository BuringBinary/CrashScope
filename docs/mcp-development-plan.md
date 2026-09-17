# Development Plan (MCP, 2026-09-17, updated same day)

Planning basis: full read-through of both projects (see `mcp-project-overview.md`) plus live probes of this host. Two workstreams.

## Decision record (user, 2026-09-17)

1. **NexBox is reference only.** The toolbox is built as an own product in a fresh codebase with independent implementation. No NexBox source, strings, or assets may be copied (GPL-3.0 clean-room discipline: study architecture, API shape, and UX behavior; reimplement). Reusing NexBox code would make the whole product GPL - not acceptable under the chosen direction.
2. **Feature priority** (recommended, user-approved): M1 hardware monitoring + in-game overlay -> M2 system optimization -> M3 display enhancement -> M4 game assist -> M5 audio/entertainment -> M6 tools/install/update.
3. **Sequencing: CrashScope first.** Finish Phase 0 and deploy on this machine (it IS the RX 9070 XT target); the freeze-wait period then funds Workstream B. A natural freeze is time-sensitive and random, so its capture window must open as early as possible.

## Ground truth (probed 2026-09-17)

- This host (DESKTOP-KM1JRPS) is the target AMD machine: RX 9070 XT + iGPU, drivers updated 2026-08-17, **GameViewer Virtual Display Adapter** installed (the exact virtual-display/remote class the evaluator weights heavily). CrashScope has never run here (no data dirs, no task, no service).
- No .NET SDK on this host; stale build outputs in the CrashScope root come from the previous `H:\1\workspace` machine (their CS7036 is already fixed in-tree).
- `NexBox-reference/` is a pristine clone of `github.com/MuLiuSaMa/NexBox` @ `a9293ab`, v9.5.3, 55 React pages, 6 locales, ~80 Rust modules, `monitor/` = .NET Framework 4.8 C# sensor helper wrapping LibreHardwareMonitorLib (same library CrashScope uses). No local Rust build artifacts.

# Workstream A - CrashScope field validation

Guiding principle (from `validation-plan.md`): do not add more telemetry until the first real incident shows a *specific* missing piece of context.

## A-Phase 0 - Field readiness (~1-2 days, start immediately)

1. **Close the Agent ETW gap (highest priority).** `Agent/Program.cs` runs an inline loop without the three ETW real-time sessions (DxgKrnl / Kernel-Power / WHEA) that `RecorderEngine` starts. Fix: make the Agent a thin host over `RecorderEngine`, one recording path for CLI/Service/Desktop.
2. **Deployment mode decision.** Recommended: scheduled task at logon, RunLevel Highest, elevated agent (user session -> keeps foreground-window fidelity, matching the "user leaves the machine" scenario). Service variant if unattended robustness wins.
3. **Build environment.** Install .NET 8 SDK on this host so `dotnet build`/`test` run locally (this also unblocks Workstream B's C# sensor helper).
4. **Extend CI** to build all five projects and run `CrashScope.Core.Tests` on `windows-latest` (currently Agent-only, zero tests).
5. **Repo hygiene.** Remove 13 stale UTF-16 logs + `TestResults/` from root (keep off-tree copies if wanted), `.gitignore` them, unify versions to 0.9.0 (README v0.9 vs csproj 0.6.0/0.4.0 vs banner v0.8), drop dead `_storageCounter`.
6. **Test the untested evidence paths** (per `mcp-code-review.md`: the *storage* evidence path is untested - `AddStorageEvidence` + `StorageTracker` attribution; VRAM/memory-pressure/DWM are covered by `HypothesisEvaluatorEtwTests`). Also add tests for `GpuTransitionDetector`, `ProcessTracker`, `GraphicsContextBuilder`, and `DisplayTopologyTracker` statics.
7. **Baseline:** green build + all tests, committed as the deploy baseline.

## A-Phase 1 - Field validation on this machine (the milestone)

1. Publish self-contained win-x64, install per chosen mode, run elevated. Verify session dir, `sensor-catalog.json` (GPU + storage sensors present), and all three `etw-*.jsonl` actively written.
2. **Test A**: 60s run, app switching, clean stop; restart -> expect *no* incident.
3. **Test B**: `taskkill /F` the agent, restart -> expect an incident with **Unclassified** and a well-formed bundle.
4. **Test C**: game -> launcher foreground -> remote software left running -> idle; verify traces show the transitions.
5. **Test D**: keep running through normal use (games, GameViewer sessions, idle) until the first *natural* hard freeze. After forced reboot: **preserve the incident before touching drivers/BIOS**, then answer the README's 10 questions.
6. Deliverable: `docs/validation-report-<date>.md` with the 10 answers and a go/no-go list of specific missing evidence.

## A-Phase 2 - Evidence-driven iteration

- Implement only the specific gaps the first incident reveals (small patches + tests). Hardening candidates: multi-day retention, disk-full behavior, telemetry-lag self-monitoring, ETW ring sizing, ETW start failures into the Service event log (currently Trace-only), privacy-redaction audit of the export ZIP.
- After 2-3 incidents: real cross-incident analysis - the 2026-08-17 driver update is a natural before/after boundary for driver correlation; failure-mode clustering; first MTBF estimate.

## A-Phase 3 - v0.10 productization (after Phase 2 go/no-go)

- Evidence-driven backlog only: one-click installer (MSIX pattern exists in `NexBox-reference/msix/` for study), service management from the desktop UI, incident annotations tied to driver updates, deeper telemetry where gaps were proven.

# Workstream B - Own PC gaming toolbox product

## B-Definition (week 1 deliverable)

- Name/brand (check availability before branding), license (own choice, NOT GPL), repo scaffold.
- Positioning: one-stop toolbox for PC gamers - the NexBox MVP feature subset, local-first, zero telemetry. Differentiator candidate: the CrashScope black box as an integrated module (decision deferred until A-Phase 1 proves incident value).
- MVP scope: M1 + safe-subset of M2 + tray + i18n (zh-CN / en).

## B-Architecture (recommended)

- **Frontend**: Tauri 2 + React 19 + TypeScript + Vite. Same class as the reference product; mature Windows overlay / global-hotkey / tray ecosystem; low idle footprint.
- **Backend**: Rust core for deep Windows integration (registry, power plans, services, storage, network, hotkeys, overlays).
- **Sensor subsystem**: a **C# helper process** wrapping LibreHardwareMonitorLib (the proven pattern of the reference product's `monitor/`, and the same library CrashScope uses). Ship as .NET 8 self-contained. This one helper serves both products - extract the LHM wrapper (catalog + capture) into a shared C# project with CrashScope.Core's Monitoring layer.
- **Data**: local-first; settings in local JSON/SQLite; no cloud, no telemetry.

## B-Milestones

- **M0 (W1)** - product definition + repo scaffold + CI (lint / tsc build / cargo check, Windows runner for packaging) + theme tokens (dark/light) + i18n skeleton (zh-CN/en) + shared sensor-helper repo.
- **M1 (W2-W4)** - hardware monitoring: CPU (usage/temp/freq/voltage/power/core topology), GPU (multi-GPU: usage/temp/fan/power/freq/VRAM/driver), RAM (physical+working set), disk (usage/SMART/interface), motherboard, display (res/refresh/model), FPS capture, network (adapter/link/latency). UI: dashboard with mini trend graphs + raw sensor window (grouped, searchable, live). **In-game overlay** (FPS + hardware info, draggable, global hotkey). Tray. Data: shared C# sensor helper.
- **M2 (W5-W7)** - system optimization, safe subset first: memory cleanup (+interval/threshold auto), startup manager, power-plan import with one-click restore, storage cleanup, shader-cache cleanup (AMD + NVIDIA), DNS presets + speed test. Deferred with per-item revert/audit: the 50+ bulk registry tweaks; deferred with supply-chain review: driver download/install; flagged: anti-cheat (ACE) tuning - compatibility/ban risk.
- **M3 (W6-W8, can overlap M2)** - display enhancement: color filter (temp/brightness/contrast/saturation, RGB gamma, per-monitor, presets, ICC), crosshair overlay (6 styles + custom PNG + pro presets + picker), floating info panels.
- **M4 (W8-W10)** - game assist: game launcher (custom entries), Steam library management, audio EQ (10-band + presets + spectrum), auto clicker, GPU name display, resolution converter. **Excluded by default** (ToS/ban risk, revisit only with explicit user decision): game-memory-reading features (e.g. daily-password readers), unofficial music-platform APIs.
- **M5 (W10-W12, only if M1-M4 stable)** - audio/entertainment: music player, desktop lyrics, spectrum visualization.
- **M6 (W11-W13)** - tool integration (detect/launch MSI Afterburner, GPU-Z, CPU-Z, Mem Reduct, OBS, ...), themes (frosted glass, video wallpaper), auto-update, MSIX + installer, docs/screenshots, i18n completion (6 languages if in scope).

## B-Shared foundation with CrashScope

- One sensor codebase: LHM wrapper shared between CrashScope.Core Monitoring and the toolbox helper (avoid two divergent hardware stacks).
- Integration option (post-A-Phase 1): CrashScope black box as a toolbox module - incidents page + health dashboard + auto-recording toggle, reusing the UI-free Core library.

## B-Guardrails and risks

- **Clean-room discipline** (hard rule): no copying of NexBox source/strings/assets; reference = behavior/API/UX patterns only. Any copied code contaminates the product with GPL.
- Game ToS: per-feature risk note for every M4 item; default to the safe side.
- Overlay/hotkey must coexist with games and anti-cheat - this host (RX 9070 XT, GameViewer virtual display, active gaming usage) is the test bench.
- Effort: M1+M2 alone span frontend + Rust + C#; keep MVP narrow, ship M1 visibly before widening scope.
- Scheduling: on a single machine with an active fault, any natural freeze preempts everything - incident capture is always top priority.

# Combined timeline

| Week | CrashScope (A) | Toolbox (B) |
|---|---|---|
| W0 | Phase 0: ETW gap fix, SDK, CI, hygiene, tests, deploy | - |
| W1 | Tests A/B/C pass; running in field | M0: definition + scaffold + CI + shared sensor helper |
| W2-W4 | waiting for natural freeze (passive; inspect sessions) | M1: hardware monitoring + overlay + tray |
| W5-W7 | freeze -> preserve incident -> 10-question report | M2 (safe subset) + M3 overlap |
| W8+ | Phase 2 evidence-driven iteration; cross-incident from 2-3 incidents | M4, then M5/M6 per stability |

## Quality gates

- A: CI green (all 5 projects + tests) before push; local `dotnet test` before deploy; no driver/BIOS changes until the first incident is preserved.
- B: CI green (lint + build + cargo check); overlay tested in real game sessions on this host; per-feature risk note in the plan doc.
- Both: small commits on branches; `docs/mcp-*.md` for agent-produced analyses.
