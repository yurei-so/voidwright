# LAND Ramp Bottom to Green Zone preflight — 2026-10-17

> This was a read-only preflight. A later, separate disposable-engine drive
> completed the waypoint-guided route; its results are in
> `land-ramp-green-engine-trial-2026-10-17.md`.

## Scope and evidence boundary

This preflight identifies the existing V2R3 proving-ground fixture and target
markers for a real-engine LAND navigation test. It uses read-only
`vragecage status` and `fixture-inspect` checks plus existing local records.
No game service was started, no fixture was changed, and no vehicle was moved.
The active Voidwright snapshot is stale; saved GPS coordinates and prior
receipts do not establish the rover's current pose or current sensor view.

The existing Voidwright checkout is already dirty on `main` at `03c16f6`.
Those changes were preserved. This report is an additional local uncommitted
file.

## Correct markers in the inspected V2R3 proving grounds

Read-only fixture inspection confirmed these exact GPS labels and coordinates:

| Role | Exact GPS label | World coordinates (m) | Fixture description |
| --- | --- | --- | --- |
| Requested start | `Obstacle Ramp Bottom` | (34.22, 32.75, 48.37) | Connects to the wall and empty-space area |
| Requested destination | `Green Zone` | (53.16, 14.49, 38.46) | Existing named color zone |
| Obstacle marker | `Obstacle Wall` | (39.40, 25.21, 42.91) | Surface facing Ramp Bottom; description says it blocks the direct Green-to-Empty-Space path |
| Possible route landmark | `Obstacle Empty Space` | (24.11, 25.42, 35.25) | Entry to the central obstacle-course area |

The straight line between the Ramp Bottom and Green Zone marker coordinates is
about **28.11 m**. The wall marker lies about **2.68 m** from that line in this
3D coordinate calculation. Those are marker-geometry measurements only: the
wall marker is not the wall's full collision volume, so this does not prove
that the direct rover path intersects it or that either side is passable.
`Obstacle Empty Space` is confirmed as a separate existing waypoint, not a
replacement for the requested Green Zone destination.

## Fixture candidates and preservation

| Fixture | Current read-only state | Decision |
| --- | --- | --- |
| `test-suite-world-v2-r3-hvvideo-001` | Fixture ID `66ec2850a0c03e77885df943b101a08c`; service inactive/dead, last shutdown clean, zero fixture edits; HoloView mod and a previously staged Voidwright server plugin are present | Candidate for the requested sensor preflight. Its staged plugin is an older build and must be compared with the current local source before any driving. |
| `test-suite-world-v2-r3-voidwright-unified-20261013-c` | Fixture ID `19dc9f16e7eaf050e470d3f1624956b2`; service inactive/dead, last shutdown clean, zero fixture edits; unified Voidwright observation mod present, no server plugin staged | Clean alternative, but it needs a separately built/staged game-facing plugin before sensor or vehicle tests. |
| `test-suite-world-v2-r3-demo` | Service inactive, but latest receipt has `fixture_edit_count=2` and an unfinished `lab_operation_status=running` | Preserve; do not reuse for this trial. |

The configured Voidwright snapshot was captured on 2026-10-13 and reports
`fresh=false`. Its last-known truck positions cannot be used as today's start
pose. Fixture inspection identifies the `[Voidwright] Obstacle Course Truck`
controller `76786533951282871`, its onboard camera `96346417539617834`, and
the fixed `Obstacle Wall Camera` `86513833901100433`. These entity IDs are
fixture-specific and must be rechecked against a fresh run before use.

## Available sensing and navigation boundary

The existing game-facing adapter uses `MyAPIGateway.Physics.CastRay` from the
selected rover's front camera. LAND depth navigation samples a 19-beam
horizontal fan across a 180-degree field, with an 8 m range and a 3.4 m
clearance threshold. This is supported source already in the adapter; no new
ModAPI surface is assumed. The separate high-resolution sensor-frame command
also uses game-side raycasts and returns depth, hit IDs, and surface normals.

The current depth controller selects a reactive heading from one fan. It does
not build a world obstacle map or plan a multi-turn route. The previous
synthetic probes found centerline-ray collision and a footprint-swept
conservative stop; neither is actual Space Engineers evidence. Fresh sensor
frames are needed before choosing a side route. The previously staged plugin
must not be treated as current code until its hash is reconciled.

## Proposed pass/fail criteria

1. Confirm a fresh, exact-fixture run, the selected truck/camera IDs, zero
   connected human players, and a rover pose within the chosen Ramp Bottom
   start radius before motion.
2. Record fresh engine-raycast evidence showing whether the wall obstructs the
   direct heading. The observation must include the run, camera pose, and tick;
   stale snapshot geometry is not a substitute.
3. Plan and follow a route with the rover's measured footprint and bounded
   wheel steering/speed. Maximum speed for the initial low-speed trial should
   be no greater than **0.5 m/s**. Do not cross unknown space when the sensor
   view is insufficient.
4. Pass only if the truck reaches the exact `Green Zone` GPS arrival radius
   without a collision, human steering, or safety stop. Any sensor, progress,
   or clearance failure must neutralize the truck and be reported as a fail,
   not an arrival.
5. Retain the correlated sensor frames, operation trace, and terminal receipt;
   report synthetic checks separately from in-game evidence.

## Approval gate

The next evidence-gathering step requires starting
`test-suite-world-v2-r3-hvvideo-001` for a bounded, **sensor-only** preflight
using the Obstacle Course Truck camera, followed by a clean stop. No motion is
included in that preflight. The user was asked for approval before starting
the dedicated server. A separate route execution must not be claimed until
the current candidate code and actual sensor results are reviewed.
