# Voidwright live perspective and structural raycast field test

Date: 2026-10-13

## Result

The Voidwright Agent MCP adapter successfully read a genuine Space Engineers
Agent Core snapshot from the isolated V2R3 fixture. Two virtual viewpoints
selected different Blue Utility Truck blocks; exact-cell inspection, an
occlusion case, a bounded miss, tick/run guards, and same-process snapshot
refresh were exercised. Independent slab intersection against the exported
cell envelopes agreed with the MCP's selected block and distance.

This validates snapshot-backed structural inspection, not a live physics
raycast, detailed block mesh visibility, or an in-game camera.

## Fixture and provenance

- Fixture: `test-suite-world-v2-r3-agent-core-20261013-a`
- Fixture ID: `b973b5dc085ff84ec466885dd992cb31`
- Root grid: `[Voidwright] Blue Utility Truck`, entity `106313676705820376`
- Agent Core: entity `94124964145704784`; MCP agent ID
  `voidwright-core:94124964145704784`
- Space Engineers build: `01_210_014`
- VRageCage server run: `9c558dcf115d37b5ed1284301fcfde1f`
- Exporter run ID: `f34bacbdd44b4efaa426f41c30793156`
- Final snapshot: tick `30400`, captured `2026-10-13T08:34:26.6907604Z`,
  SHA-256
  `62e7298eb8f310ca88a9b4b81663963ab25ac60f3efa8eda98b951a470b95472`
- Snapshot path on the game host:
  `.../instances/test-suite-world-v2-r3-agent-core-20261013-a/Saves/test_suite_world_v2_r3/Storage/90058407081852231.sbm_Voidwright/agent-core-snapshot-v1.json`
- Final terminal receipt:
  `Quarantine/9c558dcf115d37b5ed1284301fcfde1f/terminal-receipt.json`,
  SHA-256
  `6833db48bc22b0f2f881e8606dd8f52aeabec839040688ccd6fd260e60410816`

The exporter reported five physical grids and 88 observed blocks, with complete
coverage. The root truck contained 84 blocks. The snapshot schema carries an
exporter run ID but no canonical game-world UUID. MCP `worldScopeId` is a hash
of the configured snapshot-file path, not an authoritative world identity; the
fixture ID and exact source path were verified separately through VRageCage.

## MCP calls and observations

The adapter was launched as a local stdio MCP process against a byte-for-byte
copy of the fixture's explicit snapshot file. Actual JSON-RPC `tools/list` and
`tools/call` operations exercised:

- `voidwright_snapshot_status`, `list_voidwright_agents`, and
  `query_voidwright_body`: snapshot readable/fresh during the run; tick `23200`,
  exporter run `f34bac...`, 88 listed blocks, complete coverage.
- `create_voidwright_perspective`, `look_at_voidwright_perspective`, and
  `raycast_voidwright_perspective` from grid-relative offsets `(right, up,
  forward)`:
  - `(0, 7, -12)` mapped to world origin approximately
    `[-35.464, 64.519, 36.706]`; hit Offset Spotlight 2, entity
    `72087725661310819`, cell `[-2, 6, -1]`, face `+Y`, distance `12.339794 m`.
  - `(12, 7, 0)` mapped to world origin approximately
    `[-20.533, 56.490, 35.933]`; hit Offset Spotlight, entity
    `115597956163319752`, cell `[1, 3, 0]`, face `+X`, distance `12.470428 m`.
- `inspect_voidwright_cell` returned the same entity at each selected exact
  cell.
- Targeting cell `[-2, 6, -1]` aimed through its center but intersected adjacent
  envelope `119416580232819490` at `[-2, 6, -2]` first. This is expected
  nearest-envelope behavior; cell inspection is the exact occupancy query.
- Occlusion: aiming at block `121904629488997129`, cell `[-3, 4, -6]`, returned
  nearer block `72087725661310819` at `11.911786 m`. Independent geometry
  computed the same nearest block and distance.
- Explicit upward ray with a 3 m range returned a complete-coverage `miss` for
  the enrolled grid. This says nothing about terrain or other entities.
- A ray requiring tick `23199` while observing tick `23200` was rejected with
  `Snapshot tick does not match expectedSimulationTick`.
- Replacing the local test input with the genuine earlier snapshot (run
  `cea0f5e19b194b188dbf0f83f69aeb68`, same grid fingerprint) invalidated an
  existing perspective with `World scope or simulation run changed`.
- The same MCP process read a later snapshot without restarting: tick `23200`
  advanced to `23800` in the same exporter run with the same grid fingerprint.
  Moving the perspective after refresh changed its reported world position;
  the tested ray then returned a miss, not an arrival or visual observation.

The independent check used the raw exported transforms, cell bounds, and grid
size to calculate the nearest oriented cell-envelope intersection. The two
selected block IDs/cells and distances matched MCP results within `1e-8 m`.
This is independent validation of the exported envelope calculation, not
comparison with Space Engineers physics collision.

## Visual boundary

`capture_view` is not in the MCP tool list, and this fixture test did not produce
a HoloView frame. The existing virtual-camera HoloView proof remains separate
from this MCP workflow. `query_voidwright_body(includeWireframe=true)` did
return a 19,199-byte embedded SVG with top, side, and front profiles and the two
selected blocks colored green/red. It is a structural orthographic wireframe,
not a game-client image, LCD feed, or arbitrary-angle render. A rasterized copy
of that SVG is available at
`/tmp/voidwright-mcp-live-20261013/blue-utility-truck-structural-wireframe.png`.

## Gameplay readiness

The game-side exporter refreshes in world storage every 300 simulation ticks
(nominally five seconds at 60 ticks/s); snapshot reads during one MCP process
picked up newer files and preserved perspectives within the same run. The
adapter still requires access to the exact file path. For this test, the file
was copied from the game host to the workstation; this manual copy is not a
gameplay bridge. The workstation MCP tool catalog did not include Voidwright,
so Codex could not call these tools natively in this conversation. The game
host also lacks the .NET runtime needed for the documented framework-dependent
adapter launch.

No player connected during this field test. The instance was bound to private
LAN address `10.0.0.182`, then stopped cleanly; the service is now inactive.
The adapter currently has a lab-driving tool in its normal tool list, which is
not appropriate for a read-only cooperative play session. This task adds
`VOIDWRIGHT_READ_ONLY_ONLY=true` so a future configured MCP process exposes only
snapshot/body/perspective/inspection tools. Tests verify the driving tool is
absent in that mode.

## Tests and changed files

- `dotnet build AgentMcp/Voidwright.AgentMcp.csproj --configuration Release`:
  passed, 0 warnings/errors.
- `dotnet run --project tests/Voidwright.AgentMcpTests/Voidwright.AgentMcpTests.csproj --configuration Release`:
  passed, 11/11 checks, including the read-only tool surface.
- Task-specific repository changes: `AgentMcp/Program.cs`,
  `tests/Voidwright.AgentMcpTests/Program.cs`, this receipt, and the
  `VOIDWRIGHT_READ_ONLY_ONLY` setup note in `docs/agent-core-mcp.md`. Other
  modified/untracked files in both repositories predate this field test and
  were left untouched.

The temporary dedicated-server run ended with `clean_shutdown=true`,
`passed=true`, and an inactive service. No player was connected and no
autonomous control or block-edit operation was issued during this test.
