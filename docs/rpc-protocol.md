# Agent RPC Protocol (v1)

The local control plane between the recorder (`CrashScope.Agent`, or any host of
`RecorderEngine`) and UI clients. Introduced with the toolbox integration
architecture in `player-toolbox-integration.md` (branch
`design/player-toolbox-integration`); this document is authoritative for the wire.

## Transport

- Windows named pipe, byte mode, in/out. Default name:
  `crashscope-agent-v<pid>` (PID suffix so Agent + Service never collide).
- `PipeOptions.CurrentUserOnly`: only processes of the same Windows user can
  connect. The handshake additionally enforces the protocol version.
- Discovery: the server writes `<state-root>/state/rpc-endpoint.json`
  (`{ pipeName, processId, protocolVersion, agentVersion, startedAt }`) on start
  and deletes it on stop. Clients read that file instead of guessing the PID.
- Framing: 4-byte little-endian length + that many bytes of UTF-8 JSON.
  Frames larger than 8 MB are rejected.

## Envelope

```json
{ "v": 1, "id": "hex32-or-null", "type": "verb-name", "ts": "2026-09-17T12:00:00+08:00", "payload": { } }
```

Requests carry an `id`; responses echo it; server-pushed events have a null `id`.
Property names are camelCase (System.Text.Json web defaults).

## Handshake

The first message must be `hello` with `{"clientName": "...", "protocolVersion": 1}`.
Anything else is answered with an `error` (`handshake-required`) and the connection
closes. A version mismatch is answered with `protocol-version` and closes. On
success the server replies `welcome`:

```json
{ "protocolVersion": 1, "agentVersion": "0.9.0", "recording": false, "sessionId": "..." }
```

## Requests (client -> server)

| Verb | Response type | Payload |
|---|---|---|
| `get-status` | `status` | `{ recording, sampleCount, sessionId, startedAt, uptimeSeconds, agentVersion, primaryGpu, recorderState, recorderError, lastSampleAt, recoveryError }` |
| `get-sensor-catalog` | `catalog` | `{ entries: [{ id, hardwareType, hardwareName, sensorType, sensorName, identifier }] }` |
| `get-latest-telemetry` | `telemetry` | `{ timestamp, values: [float or null] }` - `values` is empty before the first sample |
| `start-recording` | `ok` / `error` | - |
| `stop-recording` | `ok` / `error` | - |
| `get-incidents` | `incidents` | `{ incidents: [{ id, timestamp, leadingMode, confidence, hypothesisCount, hasSummary, hasTimeline }] }`, newest first |
| `get-incident-detail` | `incident-detail` | request `{ id }` (a directory name, never a path); response `{ id, summary, timeline }` with the markdown texts (either may be null) |
| `get-power-plans` | `power-plans` | `{ plans: [{ guid, name, isActive }] }` |
| `get-processes` | `processes` | `{ processes: [{ pid, name, priority }] }` |
| `get-actions` | `actions` | `{ actions: [actionRecord] }`, newest first (audit journal) |
| `set-power-plan` | `ok` / `error` | request `{ planGuid, reason }` - `reason` is mandatory |
| `set-process-priority` | `ok` / `error` | request `{ pid, priority, reason }`; priorities Idle/BelowNormal/Normal/AboveNormal/High only (no RealTime) |
| `rollback-action` | `ok` / `error` | request `{ actionId, reason }`; appends a rollback row, never edits the journal |

Unknown verbs get `error` with code `unknown-type`; requests always get exactly
one response with the same `id`.

## Events (server -> client, `id == null`)

| Type | Payload | Notes |
|---|---|---|
| `status-changed` | same as `status` | pushed on every recorder state change |
| `telemetry-update` | same as `telemetry` | pushed at the recording cadence (1 Hz); per-client queue is bounded (drop-oldest), a stalled UI falls behind instead of stalling the recorder |

## Error codes

`handshake-required`, `protocol-version`, `already-handshaken`, `unknown-type`,
`start-failed`, `stop-failed`, `bad-incident-id`, `incident-not-found`,
`actions-unavailable`, `reason-required`, `unknown-plan`, `already-active`,
`unknown-priority`, `process-not-found`, `set-failed`, `rollback-target-not-found`,
`rollback-failed`.

## The actions plane

Every mutation is journalled append-only to `<state-dir>/actions.jsonl` (write-through)
with timestamp, caller (the hello client name), reason, old and new values, and
rollback metadata - so toolbox-side changes become forensic evidence instead of
invisible variables. Mutations without a non-blank reason are rejected
(`reason-required`). Start the agent with `--no-actions` to serve the read verbs
but refuse all mutations (`actions-unavailable`). `CrashScope.Agent probe
--exercise` self-tests the plane end to end (power plan set + rollback, process
priority on the probe process itself) and restores everything it changes.

## Implementation

- Contracts (DTOs, envelope, constants): `src/CrashScope.Contracts/`
- Server: `src/CrashScope.Core/Ipc/AgentRpcServer.cs` (hosted by the Agent;
  reusable by the Service and Desktop hosts)
- Client: `src/CrashScope.Core/Ipc/AgentRpcClient.cs` (used by tests and the
  upcoming UI shell)
- Agent flags: `--no-rpc` disables the surface; `--pipe-name=<name>` overrides
  the endpoint name.

## Probe

`CrashScope.Agent probe [--pipe-name=<n>] [--state-dir=<path>] [--duration=<s>]`
is a ready-made inspection client: resolves the endpoint (explicit name or the
endpoint file), handshakes, prints status, a sensor-catalog summary and the live
telemetry count, then exits 0. Exit 3 means the agent was unreachable or the
endpoint file is missing; `probe --help` documents the flags. The probe logic
lives in `AgentRpcProbe` (Core.Ipc) so tests and future tools reuse it without
a process boundary.

## Reliability lifecycle additions (2026-09-17)

Protocol v1 gains optional status fields; old status payloads still deserialize:

- `recorderState`: `Stopped`, `Starting`, `Recording`, `Stopping`, or `Faulted`.
- `recorderError`: the last acquisition/cleanup failure, if any. This is not evidence of a machine crash.
- `lastSampleAt`: UTC timestamp of the latest successfully written telemetry sample, or null before sampling.
- `recoveryError`: recovery was deferred; pending evidence remains on disk for a later launch.

`recording` is true only in `Recording`. Stop publishes `Stopping` immediately and acknowledges only after
its worker exits and cleanup finishes. It does not mark a known faulty session clean. A request timeout
is not proof that stopping finished; a blocked native collector retains resources and data-root ownership.
A faulted recorder must be stopped before it can be restarted. Every restart uses a distinct evidence directory.
The latest-telemetry cache is invalidated when stopping or starting a fresh session.

Data-root ownership is held by the recorder and shared as a protocol with recovery/retention, not by the
RPC listener itself. CurrentUserOnly authorization and the actions plane have not been redesigned in this pass.
The full change/validation record is `docs/mcp-reliability-convergence.md`.
