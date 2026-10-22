# LAND soft-steering-ramp experiment — 2026-10-18

## Result

The clear-path soft ramp materially reduced measured steering chatter in the
tested deadband and completed the full Ramp Bottom → Green Zone route. It did
not add bounded route operations: the candidate used 21 versus 22 for the
baseline, and both finished 2.265 m from Green Zone inside the stricter 2.5 m
radius (0.235 m margin). This is promising disposable-engine evidence, not a
production change or a client-video confirmation.

In matched two-tick samples on attempts 4 and 5 toward `Obstacle Empty Space`,
while depth decisions were `clear` and absolute heading error was 0.075–0.085
rad:

| Measure | Baseline | Soft ramp | Change |
| --- | ---: | ---: | ---: |
| Command total variation | 25.691 | 0.348 | −98.6% |
| Actual front-wheel angle total variation | 11.642 rad | 0.271 rad | −97.7% |
| Largest actual wheel-angle change over 2 ticks | 0.060 rad | 0.003 rad | −95.0% |
| Neutral ↔ active steering transitions | 167 | 0 | eliminated in this sampled band |

The baseline's mean absolute heading error across those samples was 0.07999
rad; the candidate's was 0.07850 rad. These are close samples from paired but
independently simulated runs, not a statistically controlled estimate of
heading accuracy. The largest sampled absolute heading error was 0.085 rad in
both.

The ramp lowers steering authority inside the measured band: it scales from
zero at 0.075 rad to full command at 0.085 rad, so it is about half strength at
0.080 rad. That is the responsiveness cost. At route level, the candidate
needed the same number of operations for the first three waypoints and one
fewer for Green Zone; its final distance exactly matched baseline. No route
completion penalty was observed in this run. Larger errors and obstacle
avoidance are not attenuated.

## Paired engine setup

- Both runs used disposable copies of the same saved world tree, SHA-256
  `2f23c14f7456a0b966202e9fbac8f80d3412db487a6b90d61566db9f581e8a29`.
- The truck began from the saved Ramp Bottom vicinity, approximately 2.17 m
  from the `Ramp Bottom` marker; it was not reset to the marker center.
- Ordered route: `Obstacle Empty Space` → `North wall bypass A` →
  `North-east corridor B` → `Green Zone`. Each waypoint used a 2.5 m arrival
  radius. The speed cap was 0.25 m/s.
- Baseline fixture: `test-suite-world-v2-r3-land-steer-baseline-20261018-c`,
  ID `596e614beefc66942c4bc54950f4cf20`, plugin tree SHA-256
  `8c851dd4d10dd0c134ed7010e3dfedcdf94eac225280c30e334eb8aa9b43fe06`.
- Candidate fixture: `test-suite-world-v2-r3-land-steer-soft-ramp-20261018-d`,
  ID `923e617d4999951521bbc451482b1224`, plugin tree SHA-256
  `1847c3e3f486e25ccffb3126e5ee687e006eeb1e1dffda5b3aa62f1c51be29bb`.
- The disposable plugin diff changes only the steering calculation. The ramp
  is applied on `clear`; `avoid` uses the unfiltered requested correction, and
  the existing invalid/stale/no-safe-corridor stop and emergency neutral paths
  remain outside the ramp.

## Route comparison

| Waypoint | Baseline operations / finish | Soft-ramp operations / finish |
| --- | ---: | ---: |
| Obstacle Empty Space | 6 / 2.261 m | 6 / 2.260 m |
| North wall bypass A | 3 / 0.957 m | 3 / 0.956 m |
| North-east corridor B | 4 / 0.957 m | 4 / 0.959 m |
| Green Zone | 9 / 2.265 m | 8 / 2.265 m |
| **Whole route** | **22 operations** | **21 operations** |

Both routes reported all four waypoints complete. Across every bounded
operation, both fixtures had zero fixture edits, zero self-grid hits, matching
plugin hashes, and clean shutdowns. Maximum reported speed was 0.170 m/s for
baseline and 0.141 m/s for candidate. Minimum reported corridor clearance was
6.514 m and 6.517 m respectively.

The engine did not expose an external collision-contact counter. Zero
self-grid hits and no fatal markers are useful diagnostics but do not prove
that no external contact occurred. The routes were tested on the dedicated
server; there was no synchronized native-client video in this experiment, so
the reduced wheel-angle variation is measured server behavior, not a direct
measurement of what the player client rendered.

## Reproducible evidence

The local evidence bundle is
[`artifacts/land-steering-soft-ramp-20261018`](../artifacts/land-steering-soft-ramp-20261018/experiment-manifest.json):

- `experiment-manifest.json` records fixtures, source and deployed plugin
  hashes, route results, and safety limitations.
- `baseline/` and `soft-ramp/` contain each bounded command and its terminal
  and clean-stop receipts.
- `sources/` preserves both exact disposable plugin source trees;
  `soft-ramp.patch` shows their narrow controller difference.
- `engine-steering-trace-extract.jsonl` contains only the instrumented
  steering lines. Every source game log was SHA-256 checked against its clean
  stop receipt before extraction.
- `matched-clear-deadband-comparison.json` contains the measurements above.
- `run-route.py.txt` preserves the bounded experiment runner for review.

The original save was not touched. Both disposable fixtures are now stopped.
No repository controller source, production service, or deployed runtime was
changed; nothing was committed, pushed, or deployed.

## Recommendation

Keep the candidate as a local experiment, not the default controller yet. Its
smoothness result is strong and route tracking did not regress in this paired
run. Before adopting it, repeat the paired route at least once and capture a
synchronized client view to confirm that server wheel-angle smoothing matches
the visible twitch reduction. Keep immediate `avoid` and safe-stop behavior as
hard invariants.
