# Space Engineers integration plan

## Stage 0 — installed planner substrate (current)

The session component loads, reports its version boundary, and does not take
control. LAND/AIR planners are independently exercised by deterministic tests.

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

## Stage 3 — guarded route following

Add mobility-specific controllers behind an explicit per-block opt-in. Preserve
manual override, ownership/faction checks, server authority, speed limits, and
clean cancellation. Reactive collision avoidance can stop or temporarily divert
the follower, but cannot become the primary planner.

## Stage 4 — AI-block goal adapter

Translate supported vanilla AI goals into Voidwright planning requests. Keep
goal/task selection above the navigation boundary so future tactical, fleet, and
construction behavior does not have to manipulate wheel or thruster mechanics.

Each stage requires an in-game receipt before the next gains control authority.

## Dedicated authority boundary

Voidwright control belongs to its own Navigation Controller block rather than a
renamed Remote Control. Small- and large-grid variants reuse Keen's non-DLC Event
Controller models while remaining Voidwright-owned terminal blocks. The DLC-
gated Automations programmable-block reskin is deliberately not repackaged. The
persisted `Navigation armed` setting records explicit operator intent; until the
guarded follower stage lands, diagnostics report `authority=none` even when the
setting is armed.

The dedicated controller is the future root of destination/waypoint ownership,
planner state and visualization, and arbitration with vanilla AI blocks. The
renamed Remote Control path is temporary migration scaffolding and must be
removed before the controller receives propulsion authority.

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
