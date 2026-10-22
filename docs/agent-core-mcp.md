# Voidwright Agent Core: local MCP adapter

This adapter exposes Agent Core identity, grid/block observations, bounded
virtual perspectives, and the optional three-profile wireframe query through a
**local stdio MCP process**. A separately configured HoloView mailbox can also
provide one on-demand virtual color render from an existing perspective.
It also has one separately bounded, destructive-lab-only goal command; it does
not open a TCP/HTTP listener or expose raw wheel/thruster controls.

```text
Space Engineers server/mod
    └─ refreshes one bounded world-storage JSON snapshot every 300 simulation ticks
       └─ local MCP stdio process reads the explicitly configured file
          ├─ MCP client receives bounded metadata/pages, optional SVG, and images
          └─ optional one-shot HoloView software render for an existing perspective
          └─ optional fixed GPS goal delegates to VRageCage's disposable-lab bridge
```

The game-side publisher calls Keen's world-scoped file API and writes
`agent-core-snapshot-v1.json` under the active world's mod storage directory.
The separate process does not search save folders: the operator must configure
the exact generated file path using `VOIDWRIGHT_AGENT_SNAPSHOT_PATH`. This is
important on Steam/Proton installs with more than one compatibility prefix or
world. Do not point it at a save root or a directory.

The publisher refreshes every 300 simulation ticks (nominally five seconds at
60 ticks/s). Snapshot capture itself currently runs every 100 ticks. The file
is capped at 16 MiB, includes at most eight Agent Cores, and preserves the
underlying per-core limits of 64 physical grids and 4,096 block entries. Any
omitted agents or incomplete body coverage is explicitly marked; it must not be
interpreted as an empty or complete construct. The snapshot includes custom
block names and observed block/grid state from the opted-in Agent Core body. For
local debugging it also exports one anonymous player-character pose only when
exactly one non-bot player is connected. It omits display names, Steam IDs, and
identity IDs; if multiple human players are connected it reports ambiguity and
withholds every player position. It does not export GPS markers, inventory, or
control commands. The snapshot file is overwritten during play, but the last
file remains in the world's local mod storage after shutdown until the next run
refreshes it.

Because the file is retained in world storage, it remains available after the
game stops. `voidwright_snapshot_status` labels the last observation fresh for
30 seconds and stale afterward; stale data remains readable as last-known state.
If the game is in the middle of replacing the JSON file, an incomplete read
fails clearly and may be retried. The adapter does not claim that MCP delivery
means the game still runs or that the observation is current.

## MCP tools

`VOIDWRIGHT_TOOL_PROFILE=vehicle` selects the compact four-tool observation
workflow (five with explicitly enabled lab trials). It adds one-call discovery,
motion/attitude summaries and automatically framed named views. The default
`full` profile preserves the lower-level catalog. See
[higher-level vehicle tools](agent-vehicle-workflow.md) for contracts and limits.

The full profile exposes snapshot/body observation, vehicle summaries,
process-local perspectives, and the separately gated trial workflow:

- `voidwright_snapshot_status` reports explicit-path configuration, file size,
  parse/readability, captured timestamp, and freshness. It never returns the
  configured local path.
- `list_voidwright_agents` returns world-local Agent Core IDs, core pose axes,
  grid/block counts, coverage markers, capture tick, and structure fingerprint.
- `get_voidwright_character_observation` returns a fresh world-space character
  pose and simulation tick only for a sole connected non-bot player. It contains
  no account/display identifiers, refuses stale or mismatched run/tick requests,
  and withholds the pose if more than one human is connected. This is current
  positional debugging data, not a historical movement log.
- `observe_voidwright_vehicle` returns one-snapshot motion, local-gravity tilt,
  bounded equipment counts and current integrity. It defaults to fresh,
  same-tick telemetry, preserves canonical string IDs, and keeps incomplete
  coverage, zero-gravity reference and camera perception separate.
- `query_voidwright_body` returns a page of up to 200 block rows and the grid
  transforms needed to interpret them. `offset`/`limit` bound ordinary data.
  Set `includeWireframe=true` to attach the top, side, and front SVG profiles.
  Optional ordered selectors highlight blocks with opaque `#RRGGBB` colors;
  selectors do not filter geometry. The server declines SVG outputs over
  500,000 characters rather than sending a potentially unwieldy result. The
  body response includes an integrity summary of currently observed damaged
  and nonfunctional blocks plus an explicit completeness marker. If coverage is
  incomplete, omitted state is unknown. It never calls an empty cell or omitted
  block "destroyed"; that requires a reliable historical comparison which is
  not implemented. The ratios are the game's current block snapshot values,
  not a damage history.
  Set `visualizationMode="damage"` with `includeWireframe=true` to color the
  same observed structural envelopes by current integrity: green for observed
  functional undamaged blocks, amber for observed functional blocks with
  `DamageRatio > 0`, red for observed nonfunctional blocks, and gray where a
  block's integrity fields are unavailable or invalid. The result includes
  counts for those observed classes. These are qualitative colors, not a
  fabricated damage percentage; incomplete snapshot coverage is called out
  and does not draw or classify omitted cells. Ordered caller selectors may
  still override a block's color for emphasis.
- `create_voidwright_perspective`, `move_voidwright_perspective`,
  `look_at_voidwright_perspective`, and `inspect_voidwright_cell` maintain a
  bounded virtual grid-relative observation frame and select exact snapshot
  cells. `create_voidwright_perspective.gridEntityId` is a canonical positive
  decimal string, such as `"106313676705820376"`; a JSON number can silently
  round a Space Engineers 64-bit entity ID in JavaScript clients. The optional
  raycast `targetGridId` uses the same string format. They do not create a game
  entity, move a camera block, or produce an
  image. The frame is bound to the snapshot's local world-scope path and run ID;
  old snapshots without a run ID cannot create a perspective. See
  [virtual-perspective-feasibility.md](virtual-perspective-feasibility.md).
  Its position and orientation are stored in the anchor grid's local basis and
  resolved against that grid's latest snapshot transform on each operation.
  This provides a basic transform-following viewpoint for profiles such as a
  fixed-offset chase camera; there is no interpolation/smoothing, orbit
  controller, or moving-target lock yet. A synthetic MCP regression test
  translates and rotates the grid between calls and verifies the viewpoint's
  world position and forward axis follow the refreshed transform. That test
  does not substitute for observing a moving vehicle in a live game.
- `capture_view(perspectiveId, fovDegrees=55, rangeMeters=64,
  timeoutSeconds=15)` requests exactly one 256x256 RGBA color frame from the
  current perspective through HoloView's existing software rasterizer. The
  renderer accepts the resolved world-space origin/forward/up directly; it
  does not need a physical camera or LCD. The MCP response contains an
  `image/png` block and a JSON receipt with frame hashes, actual reported pose,
  world-scope/run IDs, target grid, source grids, dimensions, geometry/visibility
  counts, request-to-capture and snapshot age. It rejects a stale snapshot,
  changed perspective/run, wrong world name, unsupported terrain, absent target
  grid, blank image, pose/FOV mismatch, old response, or oversized payload.
  `snapshot.simulationTick` and `rendererClock.updateCounter` are deliberately
  separate: HoloView's `PortalLcdSession` update counter has not been verified
  as the Space Engineers simulation tick, so the response states they are
  incomparable and `simultaneous=false`. The mailbox allows one in-flight
  capture and the tool is read-only with respect to game entities/blocks; its
  temporary request and frame files are cleaned up after each call. This is a
  single-shot operation, not a video stream or a continuous recorder.
- `raycast_voidwright_perspective(perspectiveId, maxDistanceMeters, ...)`
  intersects a bounded ray with the enrolled body's exported grid-cell
  envelopes. Optional `directionWorld` is a three-number world vector; omitting
  it uses the perspective's current forward. Instead of a direction, callers
  may supply `targetWorldPosition` or `targetGridCell` (three grid coordinates),
  with optional `targetGridId` to choose the cell's grid and limit tested
  geometry. `expectedSimulationTick` can require an exact snapshot tick. The
  distance must be greater than zero and at most 250 m. The result reports the
  selected grid and block, hit cell, grid-envelope face and world normal,
  distance, world/run identity, tick, and perspective pose. It rejects stale
  snapshots, mixed agent/envelope ticks, changed runs, and mismatched expected
  ticks. A complete miss applies only to the selected enrolled grid geometry;
  terrain, non-enrolled entities, and block mesh surfaces are unsupported. An
  entity ID of zero is an unresolved block identity. The result is a structural
  snapshot query, not a live physics raycast or a renderable game pointer.
- `voidwright_drive_lab_target` asks the configured Voidwright controller to
  drive to one exact allowlisted GPS label (`Red Zone`, `Yellow Zone`, `Green
  Zone`, `Blue Zone`, `Safe Spawn Zone`, `Starting Area`, or `End Position`).
  `Observation Towers` is intentionally not a drive target; it may be used only
  as an observation viewpoint.
  Execution is allowed only when all `VOIDWRIGHT_LAB_*` values below pin one
  VRageCage destructive-lab fixture, one ordinary non-static grid, its Remote
  Control, and a front depth camera on that same grid. An Agent Core is not
  required for this temporary fixture binding. The adapter independently
  re-inspects the fixture and refuses a mismatched fixture, grid, block ID, or
  target before queuing the action.

  The compatibility entry point now delegates to VRageCage's reviewed,
  journaled workflow. Prefer `voidwright_lab_trial(gpsTarget, action="preview")`:
  preview queues/starts nothing and returns a request ID plus plan hash.
  `action="execute"` requires those exact values; `action="status"` reads the
  journal without replaying. Calls retain fixed 0.5 m/s, 3.1 m arrival radius,
  1,800 simulation ticks and 300-second observation bounds.

  Preflight binds fixture/world/config/package inputs and refuses a changed
  plan. The new plugin must declare the destructive-lab authority marker; a
  connected operator does not block bounded movement. Cleanup stops only the
  matching owned run with fresh zero-human exporter evidence; missing evidence
  or a changed run leaves the service running for operator review.
  Repeating the same completed request returns the recorded result. An
  interrupted journal does not automatically replay.

  Both entry points return `vragecage.lab-trial-result.v1`, including
  `arrival_verified`, `clean_shutdown`, `receipt_correlated`, and any pending
  command/run review requirement. The legacy v1 camel-case response is
  superseded. This is a bounded development controller, not raw actuator
  access or ordinary-save authority. Full contracts and verification limits
  are in [the vehicle workflow](agent-vehicle-workflow.md).

Block listings are deterministic by physical-grid entity ID and grid position.
Positions and axes are world-space values. The SVG uses the Agent Core's
right/up/forward frame and preserves physical-subgrid placement. Coverage and
capture metadata are repeated in the response to keep uncertainty attached to
the data.

## Build and configure

Build the mod as usual so the game has the snapshot publisher, then build the
stdio adapter on the same machine where the game-host process can read the
world-storage file:

```bash
dotnet build AgentMcp/Voidwright.AgentMcp.csproj --configuration Release
dotnet run --project tests/Voidwright.AgentMcpTests/Voidwright.AgentMcpTests.csproj
```

Set `VOIDWRIGHT_AGENT_SNAPSHOT_PATH` in the MCP client's server configuration
to the **exact** `agent-core-snapshot-v1.json` file in the active world's
Voidwright assembly storage directory. Example shape (replace both absolute
paths with local values):

```json
{
  "mcpServers": {
    "voidwright-agent": {
      "command": "dotnet",
      "args": ["/absolute/path/to/voidwright/AgentMcp/bin/Release/net8.0/Voidwright.AgentMcp.dll"],
      "env": {
        "VOIDWRIGHT_AGENT_SNAPSHOT_PATH": "/absolute/path/to/active-world/Storage/Voidwright/agent-core-snapshot-v1.json"
      }
    }
  }
}
```

For human gameplay sessions where the agent must inspect only, set
`VOIDWRIGHT_READ_ONLY_ONLY=true`. This removes both lab-driving tools from MCP
discovery entirely; snapshot, body, anonymous single-player character,
perspective, cell, structural-raycast, and (if configured) virtual capture
queries remain available. It does not launch a game server, publish an MCP
listener, or make the snapshot fresh by itself. The adapter must still run on a
host that can read the exact active-world snapshot file (or be launched over a
trusted local stdio bridge such as SSH).

`capture_view` is disabled unless both values below identify the exact
HoloView world-storage directory and world name of the same disposable
fixture. The HoloView mod must contain the live mailbox script and be loaded in
that fixture. This path is local-only; it does not expose an HTTP/TCP endpoint.

```text
VOIDWRIGHT_HOLOVIEW_STORAGE_PATH=/absolute/path/to/exact/world/Storage/<holoview-mod-storage>
VOIDWRIGHT_HOLOVIEW_EXPECTED_WORLD_NAME=test_suite_world_v2_r3
```

The mailbox uses one atomically published request file and uniquely named
response files in that HoloView storage folder. Calls are serialized and
bounded to one in-flight request. Do not point it at a Workshop directory, a
world root, a production save, or another fixture's storage. Restart the MCP
client after changing its executable or environment so tool discovery and
configuration are refreshed.

The lab tools refuse calls unless each of these additional variables is
configured for the exact disposable fixture and vehicle. The VRageCage CLI and
the MCP process must run on the same game-host machine because the adapter uses
the CLI's local mode.

```text
VOIDWRIGHT_LAB_CLI_PATH=/absolute/path/to/vragecage
VOIDWRIGHT_LAB_INSTANCE=<exact-configured-lab-instance>
VOIDWRIGHT_LAB_FIXTURE_ID=<32-character-fixture-id>
VOIDWRIGHT_LAB_GRID_NAME=<exact-ordinary-vehicle-grid-name>
VOIDWRIGHT_LAB_CONTROLLER_ID=<Remote-Control-entity-id>
VOIDWRIGHT_LAB_DEPTH_CAMERA_ID=<same-grid-front-camera-entity-id>
VOIDWRIGHT_LAB_SNAPSHOT_RELATIVE_PATH=Storage/<exact-exporter-storage>/agent-core-snapshot-v1.json
```

Do not configure a source save, a non-disposable instance, or a production
world here. The adapter treats the supplied IDs as pins and cross-checks them
against current fixture inspection; they are not a general-purpose vehicle
discovery or authorization mechanism.

The exact mod assembly storage folder name is determined by the built assembly;
select the real generated file rather than assuming the example's `Voidwright`
directory name. Stdio is a process boundary and a local transport, not an
authorization mechanism for a process launched from an untrusted MCP client.
Only configure this server in a client the operator trusts.

## Verification boundary

The adapter tests run a real MCP stdio client/server subprocess against a
synthetic snapshot and fake VRageCage/HoloView responders. They verify discovery, anonymous
character-pose provenance, multi-player ambiguity and stale-context rejection,
paged body data, selected colors, the SVG resource, reviewed trial identity,
named view reuse and the compact four/five-tool catalogs.
They also exercise malformed/partial JSON, file-size guards, capture
provenance reconciliation, blank/mismatched-frame rejection, PNG encoding, and
fake MCP image delivery. These are synthetic tests, not proof of live game
rendering. A disposable-world run must still verify that the installed HoloView
script compiles, responds to a perspective-derived request, and emits a
nonblank frame. Any live actuation test must use only the explicitly pinned
disposable VRageCage fixture and preserve its terminal receipt.
