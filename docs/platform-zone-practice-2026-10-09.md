# Platform zone practice — 2026-10-09

## Result

The baseline now covers ten zone legs across all four color zones on the
disposable V2R3 proving-ground platform: two Safe Spawn ↔ Yellow round trips,
plus one Safe Spawn round trip each for Red, Green, and Blue. All ten ended
within the configured 3 m gravity-plane arrival radius. Each one-shot command
reported `complete`, each terminal receipt passed, and no fatal server markers
were recorded. Terminal speed across the ten legs ranged from 0.031 to
0.106 m/s. No manual steering or in-run intervention was used.

This is a repeatable open-platform waypoint baseline, not evidence of general
obstacle avoidance, collision-free operation, or full-course capability.

## Fixture and method

- Instance: `test-suite-world-v2-r3-hvvideo-001`, a disposable imported copy of
  the platform fixture (source-tree SHA-256
  `191f3f4d8ecc10a0591f6e8226542b958b0c52637121d71c6fb091a4aeed9873`).
- Vehicle: `[Voidwright] Blue Utility Truck`, grid `106313676705820376`,
  controller `125276672883538998`. The hovercraft and camera drones were not
  actuated.
- Plugin source tree SHA-256: `435a3ba80b3a3e9a618520f158919d0391081e92bd91c0eea9d8bd441c060943`.
  Magnetar loaded the plugin and passed the destructive-lab activation gate.
- Existing zone GPS targets were used directly. Commands used the standard
  wheel-guidance path at a 1 m/s cap, 3 m arrival radius, and 7,200-tick
  per-command ceiling. The arrival metric is gravity-plane distance, appropriate
  for a land vehicle; it is not 3D distance to the GPS marker.
- A short 0.5 m/s approach to Safe Spawn was used as a warm-up. It also
  completed, but is not counted in the ten color-zone legs.
- Core tests were run before live trials: 25/25 passed. No code changes were
  needed for this practice slice.

## Trial record

Elapsed time is simulation ticks divided by 60. Start/final distances are the
controller's ground-plane distance to the named target. The independent
position-derived path and center-of-mass displacement show that the vehicle
actually traversed the platform; the final speed is the terminal physics
readback after stopping.

| Leg | Start → target | Start → final distance | Ticks (sim seconds) | Path / COM displacement | Final speed |
|---|---|---:|---:|---:|---:|
| Warm-up | Initial pose → Safe Spawn | 13.111 → 2.996 m | 1,683 (28.1 s) | 10.314 / 10.271 m | 0.049 m/s |
| 1 | Safe Spawn → Yellow | 66.023 → 3.000 m | 4,014 (66.9 s) | 64.078 / 63.035 m | 0.052 m/s |
| 2 | Yellow → Safe Spawn | 64.425 → 2.987 m | 3,805 (63.4 s) | 61.845 / 61.645 m | 0.068 m/s |
| 3 | Safe Spawn → Yellow | 64.508 → 2.990 m | 3,875 (64.6 s) | 61.687 / 61.545 m | 0.041 m/s |
| 4 | Yellow → Safe Spawn | 64.308 → 2.999 m | 3,759 (62.7 s) | 61.514 / 61.440 m | 0.047 m/s |

Across the four Yellow zone legs, success was 4/4. They covered about 249.1 m
of position-sampled path in 15,453 ticks (257.6 simulated seconds); average
route speed was approximately 0.97 m/s. Each endpoint had a terminal receipt
and the server was cleanly stopped before the next one-shot command.

### Red, Green, and Blue

The remaining colors were each tested outbound from Safe Spawn and back. These
six receipts are preserved separately under
[`additional-zones/`](../artifacts/platform-zone-practice-2026-10-09/additional-zones/).

| Leg | Start → target | Start → final distance | Ticks (sim seconds) | Path / COM displacement | Final speed | Receipt |
|---|---|---:|---:|---:|---:|---|
| 5 | Safe Spawn → Red | 68.756 → 2.999 m | 4,174 (69.6 s) | 68.169 / 65.765 m | 0.060 m/s | [JSON](../artifacts/platform-zone-practice-2026-10-09/additional-zones/01-safe-spawn-to-red.json) |
| 6 | Red → Safe Spawn | 66.947 → 2.999 m | 4,014 (66.9 s) | 64.165 / 63.954 m | 0.031 m/s | [JSON](../artifacts/platform-zone-practice-2026-10-09/additional-zones/02-red-to-safe-spawn.json) |
| 7 | Safe Spawn → Green | 67.731 → 2.993 m | 3,975 (66.3 s) | 65.064 / 64.969 m | 0.106 m/s | [JSON](../artifacts/platform-zone-practice-2026-10-09/additional-zones/03-safe-spawn-to-green.json) |
| 8 | Green → Safe Spawn | 61.960 → 2.990 m | 3,765 (62.8 s) | 59.175 / 59.050 m | 0.039 m/s | [JSON](../artifacts/platform-zone-practice-2026-10-09/additional-zones/04-green-to-safe-spawn.json) |
| 9 | Safe Spawn → Blue | 66.822 → 2.993 m | 4,196 (69.9 s) | 67.123 / 63.748 m | 0.045 m/s | [JSON](../artifacts/platform-zone-practice-2026-10-09/additional-zones/05-safe-spawn-to-blue.json) |
| 10 | Blue → Safe Spawn | 64.509 → 2.987 m | 3,825 (63.8 s) | 61.869 / 61.672 m | 0.073 m/s | [JSON](../artifacts/platform-zone-practice-2026-10-09/additional-zones/06-blue-to-safe-spawn.json) |

The additional legs succeeded 6/6, covering 385.565 m of recorded path and
379.158 m of center-of-mass displacement in 23,949 ticks (399.2 simulated
seconds); mean path speed was 0.966 m/s. Combined with the four Yellow legs,
this is 10/10 successful zone legs: about 634.689 m of path and 626.823 m of
COM displacement in 39,402 ticks (656.7 simulated seconds). Every command
retained the 1 m/s cap, 3 m arrival radius, and 7,200-tick ceiling.

## What this demonstrates—and does not

- The truck reached all four color-zone targets and returned to Safe Spawn.
  Return legs used the controller's reverse-drive selection; this was
  autonomous actuation, not manual recovery.
- The route lengths remained close to center-of-mass endpoint displacement.
  Across all ten legs, path length ranged from 59.2–68.2 m; COM displacement
  ranged from 59.1–65.8 m.
- Safe Spawn's GPS marker is elevated relative to the driving surface. The
  vehicle ended about 6.77 m away in 3D while inside the 3 m gravity-plane
  radius. The Red, Green, and Blue zone endpoints were about 3.09 m away in 3D;
  Yellow's were about 3.08–3.09 m. This is why the result is reported as
  ground-plane arrival, not 3D GPS coincidence.
- No trial entered a stuck state or required a manual intervention, so stuck
  detection/recovery and overshoot recovery remain untested. Receipts do not
  record collision contacts, and these runs used standard GPS wheel guidance,
  not the front-camera depth controller; they do not establish collision
  avoidance.
- Intermediate event logs still sometimes report position-derived instantaneous
  speed as unavailable. Route-level path length and COM displacement over
  elapsed simulation time provide independent evidence of motion, but richer
  synchronized position/velocity sampling would make future diagnostics better.

The V2R3 demo instance was deliberately left untouched after its stopped
service still exposed an older non-terminal operation in its receipt. Trials
used the separate HoloView V2R3 copy. Its fixture edit count remained zero in
all six new receipts; after each leg the instance was stopped cleanly before
the next command. It is now inactive at Safe Spawn. Receipts retain the same
nonfatal environment warnings seen earlier (`Unlimited Raycasting` and `GFA -
Repulsorlift Engines`); no target fatal markers were recorded.

## Receipts

Full terminal receipts, including per-run event telemetry and hashes, are in
[`artifacts/platform-zone-practice-2026-10-09/`](../artifacts/platform-zone-practice-2026-10-09/).
The original five JSON files preserve the warm-up and Yellow trials; the six
new files are linked in the Red/Green/Blue table above.

## Next practice increment

Repeat selected pairs from deliberately different initial positions and
headings. Add explicit collision/contact observation, then test a bounded
temporary-stall/recovery case with front-camera depth guidance before treating
the controller as ready for the obstacle course.
