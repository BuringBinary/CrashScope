# A-Phase 0 Follow-up (MCP, 2026-09-17)

Companion to `mcp-phase0-changes.md`. Records the small follow-up pass agreed
with the user after the Phase 0 baseline was verified still green (5/5 projects,
146/146 tests re-run before touching anything).

## Changes

### 1. U+FFFD mojibake in evidence strings (open item 1 from Phase 0)

`HypothesisEvaluator.cs` contained 25 `U+FFFD ?` sequences - the corrupted
remains of em dashes in evidence strings that end up in every incident's
`summary.md` / `timeline.md`. Replaced each `U+FFFD ?` with ` - ` (the form
recommended in `mcp-phase0-changes.md`), then collapsed the resulting double
spaces so every string reads `<clause> - <clause>`. Verified zero `U+FFFD`
remain anywhere in `src/` or `test/`. Only one file changed; no test
substrings matched the corrupted sequences, so all 146 tests pass unchanged.
Deliberately done as its own logical change so the commit stays trivial to
review.

### 2. L5 - stale comment in GraphicsContextBuilder

The comment claimed "A stop event after the final snapshot means the app was
running at the end", the opposite of the code, which marks an app as
running-at-end when its last event at or after the final snapshot is a
*start*. Replaced with an accurate description of the 30 s snapshot
granularity and the event-stream refinement, including the honest limitation
(a trailing "stop" keeps the snapshot answer).

### 3. L9 - magic ETW event ids annotated from the real manifest

`EtwDxgKrnlMonitor.IsInteresting` whitelisted numeric ids
(1-10, 250, 253, 400, 497, 500, 600, 700, 800) with no explanation. Rather
than guessing names, the provider manifest of the running OS was dumped
(`wevtutil gp Microsoft-Windows-DxgKrnl /ge:true /gm:true`) and the ids were
resolved through the manifest's event -> task -> name mapping:

| Id  | Task (this OS build) |
|-----|----------------------|
| 1   | HighPagingLevel |
| 2   | ExcessiveMemoryTransfer |
| 3   | (no task, level 0) |
| 250 | NodeMetadata |
| 253 | RuntimePowerManagement, message PowerPStateComponent |
| 400 | GpuPartitioning |
| 497 | SetBaseDesktopDuration |
| 500 | CancelPresentAtFlips |

Ids 4-10, 600, 700 and 800 are absent from this build's manifest; the comment
records that they remain whitelisted for other dxgkrnl builds. The annotation
lives at the switch site, so the list is auditable where it is used.

## Verification

- Build: 5/5 projects green (explicit per-project build; the repo has no .sln,
  so `dotnet build` in the root resolves nothing - use the per-project paths).
- Tests: 146/146 passed.

## Publishing

`dist/win-x64` republished via `scripts/publish.ps1` so the deploy baseline
includes the evidence-string fix (incident bundles are the product's
deliverable).

## Remaining deferred items

L1-L4, L6-L8, L10-L12 from `mcp-code-review.md` remain deferred by the agreed
scope. L5 and L9 are now done; L13/L14 were done in the main Phase 0 pass.
