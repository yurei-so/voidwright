# LAND obstacle-corridor milestone — 2026-10-17

> Follow-up: the initial synthetic-only and stalled-engine status below was
> accurate when this report was written. A later disposable-engine traversal
> reached Green Zone using a waypoint-guided bypass. See
> `land-ramp-green-engine-trial-2026-10-17.md` for the actual engine evidence
> and remaining autonomous-planning limits.

## Status

**Planner scenario: PASS. Reactive depth-policy detour: FAIL in the synthetic probe. In-game obstacle navigation: NOT VERIFIED.**

The new reproducible scenario exercises Voidwright's land route planner and
route follower against a defined grid world. That route-following case advances
between discrete safe waypoints with ideal in-place turns; it does not integrate
continuous rover physics. Separate checks cover no-path,
blocked depth fan, unlocated destination, and malformed depth data. It does
not load Space Engineers or call ModAPI. No game server was started or
controlled.

The Voidwright checkout was already heavily modified before this milestone.
Those pre-existing changes were preserved. This continuation added the
isolated `tests/Voidwright.ObstacleSim` project, its diagnostic output, this
report, and a small shared navigation-permission predicate used by the existing
depth-controller adapter and tested by the new project. Nothing was committed,
pushed, or deployed.

## Reproduction

From the Voidwright repository root:

```bash
dotnet run --project tests/Voidwright.ObstacleSim/Voidwright.ObstacleSim.csproj -c Release
```

The scenario writes
`artifacts/land-obstacle-corridor-2026-10-17/synthetic-simulation.json`, plus
separate centerline and footprint-sweep depth-policy diagnostics. Scenario
metrics and traces are deterministic; generated timestamps make the JSON file
hashes vary between runs. The planner scenario uses the existing
`RoutePlanner`, `LandTraversalModel`, `VehicleProfile`, and `RouteFollower`.
Fail-safe checks call the same `DepthAvoidancePolicy` source used by the
game-facing plugin.

## Pass/fail criteria

1. **Safe alternative:** the direct column is blocked; the planner returns a
   chain of adjacent occupiable LAND waypoints; a grid-step rover follows the
   route to the goal; an independent swept-rectangle check retains at least
   0.20 m clearance from the blocked cells.
2. **No path:** a wall spanning the map returns `NoPath` and produces zero
   rover moves.
3. **No observed corridor:** when the forward ray fan has no qualifying
   three-beam corridor, the shared policy denies navigation. The production
   adapter's neutralization behavior is source-reviewed, not executed by this
   synthetic harness.
4. **Unlocated or invalid observation:** a target at 75 degrees (outside the
   +/-60-degree fan) and a scan containing `NaN` each fail closed; neither
   status permits navigation.

## Results

| Scenario | Result | Evidence |
| --- | --- | --- |
| Alternative around 5x5-cell obstacle | PASS | 28 route steps, cost 28.5, 273 expanded nodes, 4-cell lateral offset, 3 turns, minimum swept-footprint clearance 0.25 m |
| Full-width wall | PASS | `NoPath`, 65 expanded nodes, 0 route points, 0 rover moves |
| Fully blocked sensor fan | PASS | `NoClearCorridor`; scan range minima/maxima 2.0/4.0 m; navigation permission false |
| Destination outside sensor fan | PASS | `TargetOutOfView` at 75 degrees; navigation permission false |
| Malformed scan | PASS | `InvalidScan` for a NaN beam; navigation permission false |

Commands and checks run:

- Obstacle scenario: **5/5 checks passed**.
- Existing Voidwright core suite: **30/30 checks passed**.
- `git diff --check`: passed.
- Local game-assembly path: unavailable, so the game-facing assembly was not
  compile-checked in this environment.

The stop tests assert that the shared navigation-permission gate rejects
`NoClearCorridor`, `TargetOutOfView`, and `InvalidScan`. Source inspection
confirms the game adapter routes those statuses through `FinishNavigation` and
`Neutralize`, which zeroes wheel drive/steering inputs and applies braking. The
adapter itself was not executed in the synthetic project.

## Depth-controller integration risk

The depth controller currently uses a reactive ray-heading policy; the static
route planner is not connected to a game-world obstacle map. I ran a separate,
explicitly non-gating 2D kinematic probe to expose that boundary:

- With centerline rays, the toy rover model collided with the obstacle at step
  69. Its modeled rover-surface clearance reached **-0.0253 m**. This is a
  warning from simplified geometry and steering assumptions, not an in-game
  failure report.
- With three lateral footprint samples per beam, the policy stopped at step
  63 with `NoClearCorridor`, while modeled surface clearance was still
  **+0.6488 m**. This is safe behavior in the model, but it rejected the
  available detour.

Both diagnostics are retained separately in
`artifacts/land-obstacle-corridor-2026-10-17/depth-policy-centerline-probe.json`
and `artifacts/land-obstacle-corridor-2026-10-17/depth-policy-footprint-probe.json`.
The footprint sweep is only a test model; it has not been added to the game
code. The existing adapter still uses the engine's already-present
`MyAPIGateway.Physics.CastRay` calls. I did not assume additional ModAPI
geometry or vehicle-width APIs.

This leaves a concrete integration blocker: the verified local detour uses a
known occupancy map, while the game-facing depth controller has only a sparse
forward fan and has not yet demonstrated a safe turn-and-rejoin path. The
next engineering step is to improve or constrain that reactive controller
using evidence available from supported raycasts, then test it against a
disposable Space Engineers world copy with a measured rover footprint. The
synthetic pass alone is not evidence that the game rover can safely execute
the route.

## Current source state and boundary

- Repository base commit: `03c16f6` (`main` / `origin/main`); existing
  unrelated and task-specific edits remain in the working tree.
- The only game-facing logic change in this continuation is the shared
  `DepthAvoidancePolicy.IsNavigationPermitted` predicate. The existing plugin
  uses it to accept only `Clear` or `Avoid`; terminal statuses still flow
  through the existing neutralization path.
- No live/production server, world, mod package, or configuration was changed.
- The disposable-engine trial below is not a client-confirmed in-game result.

## Disposable Space Engineers engine trial: rotor view and right-side bypass

Following the operator's camera tip, I used the rotor-mounted ramp camera's
`Rotate to ...` action at 45 degrees in a disposable world copy. The resulting
game-physics depth frame contained **1,373 surface hits**; 1,368 were the main
platform, four were the rover, and one was another grid. The wall surface was
visible near its GPS marker. This is sensor evidence, not a native client
frame or a complete reconstruction. Evidence is in
`artifacts/land-ramp-green-2026-10-17/rotor45/`.

I then started an isolated copy and issued one bounded `drive-to-coordinate-depth`
trial toward a map-derived waypoint just beyond the wall's right endpoint. The
waypoint was not treated as proof of clearance: the truck's front camera
continued to make the movement decision. It selected an `avoid` heading of
0.873 rad (about 50 degrees) with a measured minimum corridor clearance of
5.513 m, so the controller selected the sensor-observed right passage. The
truck failed to make useful progress and stopped with
`depth-actuation-stalled` after 209 ticks. The final evidence reports 0.184 m
path length, 0.060 m center-of-mass displacement, and 13.281 m remaining to the
waypoint. No collision or fatal log marker was recorded. The fixture shut down
cleanly and is inactive.

The run used a 0.5 m/s cap and did not relax any safety threshold. It repeated
an earlier `depth-actuation-stalled` outcome at the same right-hand corridor.
This is now the concrete blocker: depth sensing chooses a plausible clear
heading, but the four-wheel controller does not produce sufficient movement
while making the turn. Do not claim the rover reached Green Zone or passed the
obstacle course. The full server receipt, queued command, and run metadata are
preserved under
`artifacts/land-ramp-green-2026-10-17/offset-right-step4-stall/`.

The disposable dedicated engine loaded the session but reported
`definitions_loaded=false` and `scripts_loaded=false`, with warnings for
`Unlimited Raycasting` and `GFA - Repulsorlift Engines`. Therefore this trial
validates the loaded engine physics, game-facing raycasts, and lab controller
path only; it does not validate those missing mod features or client behavior.
The source tree was not changed during the trial, and the pre-existing dirty
worktree remains preserved.
