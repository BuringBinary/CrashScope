# v0.1 Validation Plan

The first milestone is not a GUI. It is proving that the recorder preserves useful evidence across an unclean termination and, eventually, a real machine freeze.

## Test A — normal shutdown marker

1. Start the agent as Administrator.
2. Let it run for 60 seconds.
3. Switch between several apps.
4. Stop with Ctrl+C.
5. Re-run the agent.

Expected: no new incident is created for the previous session because `cleanShutdown=true`.

## Test B — simulated unclean agent termination

This tests the blackbox workflow without crashing Windows.

1. Start the agent and let it run for at least 60 seconds.
2. In another elevated terminal, forcibly terminate only the agent:

```powershell
taskkill /F /IM CrashScope.Agent.exe
```

If running through `dotnet run`, terminate the corresponding application process from Task Manager instead.

3. Start the agent again.

Expected: the next launch detects the previous unclean session and creates an `incidents/` directory containing telemetry/app tails, event-log evidence, and `summary.md`.

## Test C — target usage sequence

Reproduce the context that motivated the project without intentionally forcing a crash:

```text
Delta Force running
    -> exit game
    -> WeGame becomes foreground
    -> UU remote remains running
    -> stop user input / leave machine idle
```

Verify afterwards that the logs show:

- game process stop time;
- WeGame foreground transition;
- UU/remote process still present in periodic process snapshots;
- increasing user idle seconds;
- GPU load/clock/power/temperature transition after game exit.

## Test D — first natural hard freeze

Do not deliberately create unstable voltages/clocks just to trigger this test. Run the recorder during normal use until the existing fault naturally happens.

After forced reboot, preserve the generated incident directory before changing drivers or BIOS settings.

Questions the first real incident must answer:

1. How close is the final telemetry sample to the observed freeze?
2. Did GPU load, clock, power, voltage, temperature, or VRAM-related sensors change abruptly first?
3. Which foreground app was active?
4. Which launcher/remote/overlay/browser processes were still alive?
5. How long had the user been idle?
6. Did Windows record Display/TDR, WHEA, Kernel-Power 41, 6008, or BugCheck/WER evidence?

## Go/no-go for v0.2

Proceed to ETW/per-process GPU/display-power instrumentation only when the first real incident shows a specific missing piece of context. This prevents collecting everything just because Windows exposes it.
