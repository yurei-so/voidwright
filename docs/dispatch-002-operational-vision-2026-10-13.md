# Codex Dispatch 002: Operational Vision Receipt

Date: 2026-10-13  
Scope: read-only operational perception; no actuation, deployment, or commit

## Baseline reviewed

Reviewed the prior live-capture receipt at
[`vragecage/docs/voidwright-live-capture-2026-10-13.md`](../../vragecage/docs/voidwright-live-capture-2026-10-13.md)
and the continuation receipt at
[`overnight-dispatch-2026-10-13.md`](overnight-dispatch-2026-10-13.md).
Those receipts document three genuine nonblank HoloView color captures,
structural raycast/cell agreement for an exterior panel, and short bounded
capture bursts. The requested rate was 30 FPS, but the measured serial MCP
throughput was about 6 captures/second; this is not a video stream or sustained
performance claim.

## Capture tool availability

The current local Codex configuration still specifies the trusted SSH stdio
adapter, `VOIDWRIGHT_READ_ONLY_ONLY=true`, the exact disposable fixture
snapshot/HoloView locations, and ten allowed adapter tools including
`capture_view`. The direct adapter handshake described in the previous receipt
also advertised ten tools. The actual tool catalog injected into this current
Codex session contains only eight Voidwright tools:

- `create_voidwright_perspective`
- `inspect_voidwright_cell`
- `list_voidwright_agents`
- `look_at_voidwright_perspective`
- `move_voidwright_perspective`
- `query_voidwright_body`
- `raycast_voidwright_perspective`
- `voidwright_snapshot_status`

Therefore the adapter registration/configuration is not the observed failure;
the live conversation catalog is incomplete. This session cannot call
`capture_view` or inspect a returned image. The normal-session capture
acceptance criterion remains unmet. The precise unresolved boundary is app or
thread MCP catalog refresh: this agent has no exposed Codex app-server
`config/mcpServer/reload` or `mcpServerStatus/list` control. Codex App Server
documents those supported methods: reload reads MCP configuration and queues a
refresh for loaded threads, and status listing returns servers/tools/auth
state. Until the app exposes a reload control or the user opens a new thread
after refreshing/restarting the desktop MCP session, do not claim normal chat
capture access. The documented adapter tool is still read-only and its output
remains bounded to one image per call.

The tool `voidwright_snapshot_status` was called through the current normal
session and returned a readable but stale last-known snapshot:

- `capturedAtUtc`: `2026-10-13T11:25:29.1577399Z`
- `ageSeconds`: `11509` at query time
- `agentCount`: 1
- `fresh`: false

No capture was attempted through a substitute interface, and no stale image was
presented as current.

## New verified perception slice: damage-colored structural map

Extended the existing bounded top/side/front wireframe path with an optional
`visualizationMode="damage"` on `query_voidwright_body` when
`includeWireframe=true`.

- Green: currently observed, functional, zero `DamageRatio`.
- Amber: currently observed, functional, `DamageRatio > 0`.
- Red: currently observed, nonfunctional.
- Gray: integrity fields unavailable or outside the accepted finite [0, 1]
  domain.
- Incomplete coverage is explicitly described as unknown; omitted cells are
  not drawn or called damaged/destroyed.
- The SVG says this is current observed integrity only and does not infer
  historical destruction. MCP output includes observed-state counts.
- Ordered caller color selectors may override these colors for selected
  blocks.
- Paint, skin, and material appearance are not represented.

This is synthetic/MCP-fixture verification against exported snapshot fields,
not a newly acquired live-world damage observation.

## Other phases

| Capability | Status |
|---|---|
| CHASE camera | Existing grid-relative virtual perspective supports a fixed local offset; refreshed translation/rotation behavior has synthetic regression coverage. No live moving-grid follow test in this dispatch. |
| ORBIT | Not implemented. Existing move/look-at primitives can be called repeatedly, but no managed orbit/profile API or cadence guarantee. |
| COCKPIT | Not implemented as a named profile. A reference grid-relative offset can be used, but block-anchored cockpit pose is not resolved here. |
| FREE | Existing perspective storage is grid-relative; standalone world-space free camera is not established. |
| TARGET_LOCK | `look_at` aims at a world point, but stable block/grid target identity and continuous retargeting are not implemented. |
| Power/fuel telemetry | Not implemented in this slice; no values or totals claimed. |
| Paint-aware map | Not implemented; exported paint color/skin/material access was not verified. |
| Mechanical subgrids | Snapshots retain physical grid identities/transforms, but relationship types are not exported/classified. Connector-attached grids remain outside the proven assembly relation. |
| Damage map | Implemented for current observed `DamageRatio` and `IsFunctional`, with synthetic/MCP tests; not live-validated after this change. |
| Docking geometry | Not implemented; no connector pose/offset comparison is claimed. |
| Capture cadence | Prior bounded serial burst evidence only (~6 calls/s at a 30-FPS request). No sustained stream, queue, drop-rate, or 1/5/10-FPS benchmark in this dispatch. |

## Fixture and safety

Read-only `vragecage status` and `receipt` checks for
`test-suite-world-v2-r3-voidwright-unified-20261013-c` confirmed:

- Service: `inactive` / `dead`, result `success`, exit status 0, no restarts.
- Run `f419fad2474be11bb163e387fdb2a36a`: passed, clean shutdown, game ready,
  scripts loaded, zero fixture edits, no pending lab operation.
- Receipt confirms no connected humans.
- The snapshot is stale; no server start was needed or performed.
- No source world, production world, persistent save, network exposure, or
  Workshop source cache was modified.

The receipt includes environment warnings for `Unlimited Raycasting` and `GFA
- Repulsorlift Engines` in the world log; they did not make the terminal fixture
receipt fail, but they are not treated as successful mod behavior.

## Changed files

- `Data/Scripts/Voidwright/Core/AgentWireframeRenderer.cs`
- `Data/Scripts/Voidwright/Game/AgentWireframeSnapshotFactory.cs`
- `AgentMcp/AgentWireframeMapper.cs`
- `AgentMcp/AgentMcpTools.cs`
- `tests/Voidwright.AgentMcpTests/Program.cs`
- `docs/agent-core-mcp.md`
- This receipt

The checkout was already dirty with unrelated user work. No staging, commit,
push, cleanup, or reversal was performed.

## Verification

- `dotnet run --project tests/Voidwright.AgentMcpTests/Voidwright.AgentMcpTests.csproj -c Release`:
  18 checks passed, including qualitative damage colors, unknown integrity,
  incomplete coverage wording, MCP response counts, and rejection of an
  unsupported visualization mode.
- `dotnet build AgentMcp/Voidwright.AgentMcp.csproj -c Release`: passed, zero
  warnings/errors.
- `dotnet build tests/Voidwright.AgentMcpTests/Voidwright.AgentMcpTests.csproj -c Release`:
  passed, zero warnings/errors.
- `dotnet run --project tests/Voidwright.CoreTests/Voidwright.CoreTests.csproj -c Release`:
  29/29 checks passed.
- Previous receipts' HoloView tests remain the live-render evidence; they were
  not rerun because this patch changes only the Voidwright wireframe path.

## Recommended next experiment

First refresh the Codex MCP catalog using the app-supported reload/status path,
then verify `capture_view` is actually listed in a normal conversation and
request one bounded image from the already-stopped fixture only after starting
it under the existing safety checks. After normal image inspection works, use
a naturally moving grid to validate one live fixed-offset chase perspective.
Do not drive a vehicle to produce motion. Next telemetry slice should verify
one supported battery/tank measurement in the compatible game assemblies
before deciding whether to extend exporter schema.
