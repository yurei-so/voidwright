# LAND V4 steering trial — 2026-10-18

## Result

**The soft-ramp reduced steering-command and wheel-angle variation in one
matched near-threshold engine pair, with similar heading and translation.
The V4 route was not completed, and the physical wheels retained the same
largest sampled angle step. Keep the candidate experimental.** This is not a
V4 navigation or obstacle-avoidance pass.

## Source and safety

- The workstation save was copied before use to
  `/tmp/voidwright-test-suite-world-v4-source-20261018`.
- The copied tree hash is
  `9ae4286ebb6fad94516612cfb083c347ccb197c62276d5658a49d65ede373f8f`
  (21 files, 6,447,291 bytes). The source save was not launched or edited.
- Engine trials used two Magnetar disposable fixtures: baseline
  `test-suite-world-v4-steering-baseline-20261018-f` and candidate
  `test-suite-world-v4-soft-ramp-startup-20261018-e`. Their final receipts
  report `fixture_edit_count=0`, `clean_shutdown=true`, and no fatal markers.
  Both services were confirmed inactive after the trials.
- Trials were bounded to 0.25 m/s and ran near Platform Center, within the
  approximately 180 m safe zone. No weapons were used. No client joined.

The V4 GPS inventory identified `Obstacle Ramp Bottom`, `Obstacle Wall`,
`Obstacle Wall Corner`, `Green Zone - Obstacle Entrance`, and `Green Zone`.
The tracked controller was Codex Truck (`137709089579453168`) and its front
camera was `74011513536052165`. The controller faces opposite the truck grid's
forward axis, so the test setup explicitly accounted for a `-1` forward
alignment. Gravity was approximately 9.81 m/s². Armed warheads were kept out
of the trial area.

## Startup-gate adjustment used only in test packages

An earlier V4 attempt stopped during startup because the engine's reported
speed remained around 0.052 m/s while 20-tick center-of-mass displacement was
about 0.001 m/s. The disposable candidate and baseline packages used a
startup-only experimental settling check: when a fresh 20-tick COM speed is
available, require COM speed at most 0.05 m/s and reported speed at most
0.08 m/s; otherwise require reported speed at most 0.05 m/s, for 12 stable
ticks. Both fixtures settled at tick 41 and tick 38 respectively. This change
exists only in the staged test packages; it was not applied to mod source.

## V4 steering measurements

The most comparable pair started at the same measured heading error of
`+0.085 rad`, targeted Platform Center, and ran for 900 ticks. Each produced
429 steering trace samples. Both timed out before arrival. The candidate
started 0.103 m farther from the resolved target and traveled 1.566 m by COM
displacement versus 1.604 m for baseline (about 2.4% less).

| Measure | Baseline | Soft-ramp | Change |
|---|---:|---:|---:|
| Steering-command total variation | 1.585 | 0.710 | 55.2% lower |
| Largest sampled command step | 0.154 rad | 0.011 rad | 92.9% lower |
| Each front wheel's angle total variation | 1.213 rad | 0.599 rad | 50.6% lower |
| Largest sampled wheel-angle step | 0.060 rad | 0.060 rad | unchanged |
| Heading-error total variation | 0.069 rad | 0.039 rad | 43.5% lower |
| Final heading error | 0.080 rad | 0.076 rad | similar |
| COM displacement in 900 ticks | 1.604 m | 1.566 m | 2.4% lower |

This supports a softer controller command near the deadband. The engine still
applied wheel angles in the same 0.06 rad sampled increments, so the ramp did
not remove the largest physical wheel step. Since this pair remained on a
clear path, it says nothing about the candidate's obstacle response.

A second low-angle pair is less controlled: baseline remained inside its
hard deadband with zero command and zero wheel motion, while the candidate
issued small steering and moved its wheels. Its initial headings differed by
about 0.011 rad; it is evidence of the different deadband response, not a
clean smoothness comparison. The earlier 1,800-tick approach pair also timed
out far from its target in both variants.

## Interpretation and regression status

- **Smoothness:** promising at the command level and lower wheel-angle total
  variation in the matched V4 sample. The largest observed physical wheel
  step did not improve.
- **Responsiveness:** V4 candidate made about 2.4% less COM progress in the
  matched 900-tick window, with similar final heading error. One short pair is
  insufficient to establish a general responsiveness cost.
- **Route regression:** no V4 Ramp Bottom → Green Zone run was completed in
  this trial. The separate V2 experiment completed its four-waypoint route
  once for baseline and once for candidate, both ending 2.265 m inside the
  2.5 m arrival radius. Keep that as a limited regression result, not a
  repeatability claim; see
  [`land-ramp-green-soft-steering-ramp-experiment-2026-10-18.md`](land-ramp-green-soft-steering-ramp-experiment-2026-10-18.md).
- **Obstacle and collision claims:** the V4 depth controller reported a clear
  corridor with 8 m minimum sampled clearance and zero self-grid hits. The
  available receipt has no general external collision counter, and no client
  view was recorded, so zero collisions cannot be claimed.
- **Engine boundary:** these were Space Engineers engine/physics trials using
  a staged Magnetar server plugin. The receipts say `definitions_loaded=false`
  and `scripts_loaded=false`; Voidwright's in-game mod behavior and client
  presentation were not exercised. Magnetar also reported `Unlimited
  Raycasting` and `GFA - Repulsorlift Engines` mod warnings.

## Evidence and next experiment

The hash-checked commands, run receipts, and steering traces are in
[`../artifacts/land-v4-trial-20261018/paired-steering-check/`](../artifacts/land-v4-trial-20261018/paired-steering-check/).
`runs.json` ties each operation to its fixture, staged plugin tree, command
hash, terminal receipt hash, and game-log hash. `comparison.json` contains the
per-segment calculations. The collector is
[`../artifacts/land-v4-trial-20261018/collect-v4-pair.py`](../artifacts/land-v4-trial-20261018/collect-v4-pair.py).

Next, run at least five fresh-copy baseline/candidate pairs on the same V4
Ramp Bottom → Green Zone route and one deliberate blocked-path stop case.
Capture synchronized pose, desired/issued steering, wheel angles, depth scan
age and clearance, route completion, and client video if an authorized
observer joins. Keep the existing speed cap, safe-zone and warhead exclusion,
emergency stop, and original-save boundary. Do not promote the soft-ramp
candidate until route completion, safety, and responsiveness remain acceptable
across repeated runs.
