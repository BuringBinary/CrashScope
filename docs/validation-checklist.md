# CrashScope Local Validation Checklist

This document defines the local validation flow after cloning the repository.

The development flow is:

1. Code changes land in a feature branch.
2. CI verifies compilation.
3. Local machine validation verifies real Windows behavior.

## Build

```powershell
dotnet restore

dotnet build .\src\CrashScope.Agent\CrashScope.Agent.csproj -c Release
dotnet build .\src\CrashScope.Actions\CrashScope.Actions.csproj -c Release
dotnet build .\src\CrashScope.App\CrashScope.App.csproj -c Release
```

Expected:

- Agent builds successfully.
- Actions builds successfully.
- App builds successfully.

## Runtime smoke test

Start Agent:

```powershell
CrashScope.Agent.exe
```

Verify:

- [ ] Agent starts without exception.
- [ ] Session heartbeat file is created.
- [ ] Telemetry JSONL starts growing.
- [ ] Sensor catalog is generated.

Start App:

```powershell
CrashScope.App.exe
```

Verify:

- [ ] Dashboard opens.
- [ ] Agent status shows Online.
- [ ] CPU/GPU/RAM sensors appear.
- [ ] Values refresh continuously.
- [ ] Incident Center opens.

## Incident validation

### Clean shutdown

Steps:

1. Start Agent.
2. Stop Agent normally.
3. Start Agent again.

Expected:

- Previous session is marked clean.
- No false crash incident is generated.

### Forced termination

Steps:

1. Start Agent.
2. Kill the Agent process.
3. Start Agent again.

Expected:

- Previous session is detected as incomplete.
- Incident bundle is generated.
- Timeline contains heartbeat/session information.

### Game foreground tracking

Steps:

1. Start Agent.
2. Launch a game or 3D application.
3. Switch foreground applications.

Expected:

- Foreground changes appear in timeline.

### Graphics driver event

Steps:

Trigger a controlled GPU recovery if test hardware supports it.

Expected:

- Display/TDR evidence is captured.
- Incident timeline places Windows events correctly.

### Power loss / unexpected reboot

Steps:

Use only safe test methods.

Expected:

- Kernel-Power/EventLog evidence is collected after reboot.

## Hardware matrix

Record test results:

| Hardware | Result |
|---|---|
| NVIDIA GPU | |
| AMD GPU | |
| Intel iGPU | |
| Multi-GPU | |

## Bug report format

When reporting failures include:

```
OS:
CPU:
GPU:
Driver:
CrashScope commit:

Steps:

Expected:

Actual:

Logs:
```

## Development rule

Do not add new features until the previous feature has:

- build validation;
- runtime validation;
- failure cases documented.
