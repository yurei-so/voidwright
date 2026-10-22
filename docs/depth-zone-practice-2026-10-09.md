# Depth-guided zone practice — 2026-10-09

## Result

The depth controller completed one reproducible, forward-only arrival from the
V2R3 platform's starting area to Blue Zone. It reached the 3.1 m world-space
arrival sphere with a final 3D separation of 3.099 m and a gravity-plane
separation of 2.906 m. The truck traveled an aggregate 62.665 m of sampled path
and 62.100 m of center-of-mass displacement over 19,006 simulation ticks
(316.8 s). The fixed GPS target's gravity-plane distance decreased from
64.782 m to 2.906 m, a 61.876 m reduction. These independently reported route
measures agree within 0.9%.

This is **not** the same complete ten-leg, four-color baseline as the legacy
controller: only the Safe Spawn-to-Blue outbound leg was attempted. The return
to Safe Spawn was safely rejected because the target was 2.979 radians behind
the truck and outside the forward camera fan. The other colors and round trips
remain untested in depth mode.

## Fixture and controller

- Disposable instance: `test-suite-world-v2-r3-hvvideo-001` (Magnetar).
- Vehicle: `[Voidwright] Blue Utility Truck`, grid `106313676705820376`,
  controller `125276672883538998`; the same-grid forward camera was
  `139372174302979798`. No drone or hovercraft was moved.
- Depth mode remained capped at 0.5 m/s and 1,800 ticks per command. Its
  13-beam fan repeatedly reported a clear corridor at 8 m with zero self-grid
  hits. The final arrival segment still had 6.948 m minimum corridor clearance.
- Arrival used a 3.1 m world-space radius, accounting for the Blue marker's
  1.079 m vertical offset. This corresponds to a 2.906 m planar radius, close
  to the legacy baseline's 3 m gravity-plane tolerance.
- Calibration preserved the 15% wheel-power ceiling and all speed, timeout,
  braking, depth-validity, attitude, progress, and motion-divergence stops. The
  depth pulse-duty ceiling was raised from 10% to 25%, then 40% for subsequent
  runs after measured clear-terrain throughput was only 0.08 and 0.17 m/s. The
  40% pulse cap is still under closed-loop braking and a 0.5 m/s command cap.
- The return test used a 6.8 m world-space sphere for the elevated Safe Spawn
  marker. It stopped at tick 13 with `depth-target-out-of-view`, zero COM
  displacement, and no attempt to drive blind.

## Run record

`Start → final` is the controller's gravity-plane distance to the fixed GPS
target. `Path / COM` is the per-command position-derived path length and
center-of-mass displacement. All segment timeouts were expected one-shot
handoffs, not arrivals; the final Blue row is the only successful arrival.

| # | Command / outcome | Start → final | Ticks | Path / COM | Position-derived speed | Receipt |
|---:|---|---:|---:|---:|---:|---|
| 1 | Blue, 10% calibration | 64.782 → 62.492 m | 1,800 | 2.393 / 2.312 m | 0.082 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/01-segment-10pct-a.json) |
| 2 | Blue, 10% calibration | 62.493 → 60.200 m | 1,800 | 2.384 / 2.305 m | 0.081 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/02-segment-10pct-b.json) |
| 3 | Blue, 25% calibration | 60.201 → 55.274 m | 1,800 | 5.018 / 4.942 m | 0.172 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/03-segment-25pct.json) |
| 4 | Blue, 40% | 55.277 → 48.044 m | 1,800 | 7.291 / 7.255 m | 0.274 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/04-segment-40pct-a.json) |
| 5 | Blue, 40% | 48.047 → 41.074 m | 1,800 | 7.033 / 6.993 m | 0.183 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/05-segment-40pct-b.json) |
| 6 | Blue, 40% | 41.078 → 34.174 m | 1,800 | 6.969 / 6.924 m | 0.333 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/06-segment-40pct-c.json) |
| 7 | Blue, 40% | 34.177 → 27.396 m | 1,800 | 6.849 / 6.801 m | 0.235 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/07-segment-40pct-d.json) |
| 8 | Blue, 40% | 27.401 → 20.496 m | 1,800 | 6.966 / 6.924 m | 0.288 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/08-segment-40pct-e.json) |
| 9 | Blue, 40% | 20.501 → 13.487 m | 1,800 | 7.076 / 7.032 m | 0.237 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/09-segment-40pct-f.json) |
| 10 | Blue, 40% | 13.490 → 6.512 m | 1,800 | 7.038 / 6.994 m | 0.289 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/10-segment-40pct-g.json) |
| 11 | Blue arrival, complete | 6.517 → 2.906 m | 1,006 | 3.648 / 3.618 m | 0.042 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/11-blue-arrival.json) |
| 12 | Safe Spawn return, out of view | 64.543 → 64.542 m | 13 | 0.001 / 0.000 m | 0.034 m/s | [JSON](../artifacts/depth-zone-practice-2026-10-09/12-return-out-of-view.json) |

## Interpretation and limits

- The 10% pulse cap was too slow for a practical platform trip. The 25% and
  40% trials moved measurably faster; repeated 40% segments covered roughly
  6.8–7.3 m per 1,800-tick handoff while remaining below the 0.5 m/s cap.
- The controller reached Blue without manual steering or a safety bypass. The
  arrival receipt reports `complete`; every preceding segment ended at its
  explicit tick ceiling, so none is mislabeled as arrival.
- Physics-reported instantaneous linear speed and the 20-tick COM-displacement
  speed disagree materially during steady travel (for example, 0.274 versus
  0.037 m/s at one terminal sample). Near arrival the ordering reverses (0.166
  versus 0.042 m/s while braking). Fixed-target distance, COM position, and
  accumulated path reconcile route-level motion, but the instantaneous-velocity
  discrepancy remains unresolved and should not be hidden by reporting only one
  speed field.
- The forward-only controller cannot safely reproduce the legacy round trips
  from the Blue endpoint: Safe Spawn is behind it, so the sensor correctly
  stopped before propulsion. A future full baseline needs either a safe,
  depth-observed turn/reorientation primitive or an independently validated
  rear-view sensor/controller. No such capability was added here.
- The copied fixture had zero fixture edits, no fatal target markers, and
  clean shutdown after the return handoff. The original/source fixture was not
  modified. The experimental server instance is inactive.

## Code and verification

The depth-only pulse ceiling is now centralized and tested at 40%; the 15%
wheel-power cap and 0.8 m/s hard command ceiling are unchanged. Core tests pass
26/26. Magnetar loaded the staged plugin successfully for the live trials; no
production deployment, commit, or push was performed. The worktree already
contained unrelated user changes, which were preserved.

The twelve copied terminal receipts under
[`artifacts/depth-zone-practice-2026-10-09/`](../artifacts/depth-zone-practice-2026-10-09/)
retain operation IDs, tick-by-tick lab events, run IDs, target positions,
camera identity, and terminal-receipt hashes.
