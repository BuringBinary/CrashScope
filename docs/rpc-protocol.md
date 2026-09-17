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
| `get-status` | `status` | `{ recording, sampleCount, sessionId, startedAt, uptimeSeconds, agentVersion, primaryGpu }` |
| `get-sensor-catalog` | `catalog` | `{ entries: [{ id, hardwareType, hardwareName, sensorType, sensorName, identifier }] }` |
| `get-latest-telemetry` | `telemetry` | `{ timestamp, values: [float or null] }` - `values` is empty before the first sample |
| `start-recording` | `ok` / `error` | - |
| `stop-recording` | `ok` / `error` | - |

Unknown verbs get `error` with code `unknown-type`; requests always get exactly
one response with the same `id`.

## Events (server -> client, `id == null`)

| Type | Payload | Notes |
|---|---|---|
| `status-changed` | same as `status` | pushed on every recorder state change |
| `telemetry-update` | same as `telemetry` | pushed at the recording cadence (1 Hz); per-client queue is bounded (drop-oldest), a stalled UI falls behind instead of stalling the recorder |

## Error codes

`handshake-required`, `protocol-version`, `already-handshaken`, `unknown-type`,
`start-failed`, `stop-failed`.

## Implementation

- Contracts (DTOs, envelope, constants): `src/CrashScope.Contracts/`
- Server: `src/CrashScope.Core/Ipc/AgentRpcServer.cs` (hosted by the Agent;
  reusable by the Service and Desktop hosts)
- Client: `src/CrashScope.Core/Ipc/AgentRpcClient.cs` (used by tests and the
  upcoming UI shell)
- Agent flags: `--no-rpc` disables the surface; `--pipe-name=<name>` overrides
  the endpoint name.
