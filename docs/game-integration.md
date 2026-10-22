# Space Engineers integration plan

## Stage 0 — installed planner substrate (current)

The session component loads, reports its version boundary, and does not take
control. LAND/AIR planners are independently exercised by deterministic tests.

## Agent Core enrollment and body snapshot (schema v1)

The visible **Voidwright Agent Core** (small and large variants) is the
enrollment anchor. Placing it identifies one body with the world-local key
`voidwright-core:<entity-id>` (scoped to the current world) and discovers the root grid's **physical** grid
group. Mechanical subgrids are part of the body; connector/logical neighbors
are not silently enrolled. The core block and root grid are both identified in
the snapshot.

The read-only schema-v1 snapshot contains capture tick, core world position and
right/up/forward axes; for each discovered physical grid it records identity,
name, grid-size class and cell size, static state, world pose, linear velocity,
natural gravity, and the block count. Each bounded block entry records entity ID
when available, definition type/subtype, terminal/display name, grid coordinate
and occupied bounds, local cardinal orientation, build/damage ratios, and
functional/working/enabled state. It does
not copy Custom Data or inventory contents. Limits are 64 physical grids and
4,096 block entries; completeness is explicitly marked when either limit is
exceeded. The terminal's Custom Info is an inspection view capped at 80 block
rows.

The mod currently surfaces this snapshot in the game's Custom Info and retains
the latest typed snapshot in its game-side registry. It can now produce an
optional three-profile wireframe from that retained snapshot; see
[Agent Core wireframe query](agent-core-wireframe.md). A local stdio MCP
adapter exposes the retained snapshot through bounded body queries and the
optional wireframe; see [Agent Core MCP](agent-core-mcp.md). That observation
path does not enroll or actuate a body by itself.
The Agent Core is identity and grid discovery only. The persisted `Navigation
armed` operator-intent setting remains separate, and neither placement nor
enrollment grants actuation.

## Stage 1 — observation-only vehicle probe (current)

Bind to an explicitly opted-in Remote Control whose name contains `[Voidwright]`.
Derive a conservative world-aligned vehicle envelope from the physical grid
group, count blocks and propulsion evidence, and classify it as LAND, AIR,
HYBRID, or UNKNOWN. Diagnostics are emitted only when the observed signature
changes. No steering or autopilot mutation occurs.

## Stage 2A — goal ingestion and free-space route visualization (current)

Read the first configured Remote Control waypoint without modifying it, project
it into the mobility-specific planning volume, and render a bounded route
horizon as temporary local purple GPS markers. Markers are replaced as the
vehicle advances and removed when the waypoint or opt-in disappears. This proves
the planner/goal/visualization loop but deliberately labels its output as
free-space: it does not yet claim terrain or obstacle awareness.

HYBRID vehicles retain both interpretations rather than being silently flattened:
LAND candidates are purple and AIR candidates are cyan. Each marker includes a
short controller identity so simultaneous previews remain attributable.

## Stage 2B — bounded world sampling

Sample a bounded local terrain/occupancy region, plan asynchronously within a
fixed per-tick budget, and render/debug the proposed path. Compare it with
vanilla AI behavior without changing block control.

## Stage 2C — actuator-neutral guidance core (current)

Convert a vehicle-local target into deterministic normalized guidance. LAND
emits forward throttle and yaw intent, reducing throttle while badly
misaligned. AIR emits forward/right/up translation plus yaw intent. Arrival,
invalid input, and non-finite input all produce neutral output. This layer has
no game API dependency and cannot mutate a block; the game-facing actuator
adapter remains part of Stage 3.

## Stage 3 — guarded route following (destructive-lab bridge only; ordinary mod adapter pending)

The ordinary mod has not gained actuator authority. A separate Magnetar plugin
can drive a copied fixture only under VRageCage's destructive-lab contract.
The local stdio MCP adapter can submit a fixed, low-speed GPS-zone goal to a
pinned ordinary Remote Control and same-grid depth camera in that fixture; it
uses a reviewed plan and durable request journal, then checks correlated
operation evidence. It stops only its owned run with fresh zero-human evidence;
otherwise it reports review-required cleanup. The
MCP tool delegates continuous control to the local controller and does not
expose wheel commands. This is a development instrument, not the final
in-game adapter. Arrival remains unproven for any trial whose terminal receipt
does not explicitly report a verified arrival.

The bounded plugin command is authorized by the destructive-lab run contract,
not by an empty-player check. An operator may remain connected during a test;
automatic shutdown remains separately guarded by fresh zero-human evidence.
The lifecycle has local policy/fixture tests and SDK compilation; this is not a
new real-engine trial.
See [the vehicle workflow](agent-vehicle-workflow.md).

Before an ordinary-save adapter is considered, design mobility-specific
controllers behind an explicit per-block opt-in. Preserve manual override,
ownership/faction checks where applicable, server authority, speed limits, and
clean cancellation. Reactive collision avoidance can stop or temporarily divert
the follower, but cannot become the primary planner.

## Stage 4 — AI-block goal adapter

Translate supported vanilla AI goals into Voidwright planning requests. Keep
goal/task selection above the navigation boundary so future tactical, fleet, and
construction behavior does not have to manipulate wheel or thruster mechanics.

Each stage requires an in-game receipt before the next gains control authority.

## Dedicated authority boundary

Voidwright body identity belongs to its own Agent Core block rather than a
renamed Remote Control. Small- and large-grid variants reuse Keen's non-DLC Event
Controller models while remaining Voidwright-owned terminal blocks. The DLC-
gated Automations programmable-block reskin is deliberately not repackaged. The
persisted `Navigation armed` setting records explicit operator intent; until the
guarded follower stage lands, diagnostics report `authority=none` even when the
setting is armed.

The dedicated Agent Core is the future root of body identity and discovery.
Destination/waypoint ownership, planner state and visualization, perception,
and arbitration with vanilla AI blocks remain distinct concerns. The renamed
Remote Control path is temporary migration scaffolding and must be removed
before a control adapter receives propulsion authority.

The replacement uses the stable Event Controller shell with a Voidwright-owned
terminal surface and has no inherited movement component. Adjacent AI Basic,
Recorder, Offensive Combat, and Defensive Combat blocks are inputs only. Initial
arbitration priority is active defensive flee, configured offensive combat,
playing recorder path, then selected basic mission. This order is diagnostic in
the current slice and grants no propulsion authority.
# Navigation controller presentation

The controller currently reuses the vanilla Event Controller models but deliberately omits an
`Icon` entry. Icon paths in mod definitions are resolved inside the mod package, so referencing
the identically named vanilla DLC textures produces a misleading `MOD_ERROR`. Add an original,
redistributable Voidwright icon to the package before restoring the field.

## Headless integration contract

The packaged `vragecage.integration.json` declares the exact observation-probe marker that must
appear during a real-engine load. VRageCage snapshots this bounded contract into the stage receipt
and refuses a passing run receipt when the mod's own bootstrap marker is absent. This distinguishes
"the loader saw a DLL" from "Voidwright's session component actually initialized."
