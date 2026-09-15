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

Expected: the next launch detects the previous unclean session and creates an `incidents/` directory containing telemetry/app tails, event-log evidence, `summary.md`, `timeline.md`, and `analysis.json`.

Because this simulated termination kills only the agent (the system keeps running), the expected v0.2 analysis outcome is: no Kernel-Power 41 / 6008 / kernel dump evidence, all system-death hypotheses score negative, and `summary.md` shows **Unclassified** with an open question explaining that the agent itself may have been terminated. This validates that the scorer does not cry wolf on evidence-free incidents.

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

In addition to the raw evidence, the v0.2 report should provide:

1. A ranked list of failure hypotheses with evidence-for / evidence-against items and confidence values.
2. A `timeline.md` reconstruction around the final heartbeat showing GPU transitions, app/process changes, and Windows events in one ordered stream.
3. GPU load/clock/power transitions detected in the final minutes (sudden steps or gradual trends).
4. An honest **Unclassified** verdict if the evidence does not discriminate — not a forced conclusion.

## Go/no-go for v0.2

Proceed to ETW/per-process GPU/display-power instrumentation only when the first real incident shows a specific missing piece of context. This prevents collecting everything just because Windows exposes it.
