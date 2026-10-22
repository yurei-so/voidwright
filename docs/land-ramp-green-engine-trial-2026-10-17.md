# LAND engine trial: Ramp Bottom to Green Zone — 2026-10-17

## Result

**The rover reached Green Zone in the disposable Space Engineers dedicated-server engine.** It followed a waypoint-guided bypass with the depth controller driving and steering each leg. No player steering was used.

This is real engine physics and game-facing raycast evidence, not the synthetic obstacle simulation. It is not a native-client-confirmed result. The old 0.06 m stall on the wall-facing attempt remains a useful safety failure: the route around the wall worked, while fully automatic global route planning is still not connected to the depth controller.

## Test setup and route

- Start marker: `Obstacle Ramp Bottom` at `(34.22, 32.75, 48.37)`.
- Destination: `Green Zone` at `(53.16, 14.49, 38.46)`.
- Obstacle landmark: `Obstacle Wall` GPS marker at `(39.40, 25.21, 42.91)`. The marker is not the wall's complete collision geometry.
- Disposable fixture: `test-suite-world-v2-r3-landcorridor-emptyspace-route-20261017-b`, fixture ID `8c69e377df205cffc3728d839196b6af`.
- Route: `Obstacle Empty Space` → custom north-wall bypass point A → custom north-east corridor point B → `Green Zone`.
- The server plugin used the truck's game-facing 19-ray depth camera and a 0.25 m/s speed cap. Each command was bounded to 1,800 ticks; the controller neutralized and braked at each bound before the route resumed from the saved rover pose.

The route map is a projection of sampled center-of-mass telemetry. Its wall symbol marks only the GPS point, not the wall boundary.

![Telemetry route from Ramp Bottom to Green Zone](/home/alu52/github/voidwright/artifacts/land-ramp-green-2026-10-17/navigation-track.png)

## 0.06 m stall investigation

The failed wall-facing attempt ended with `depth-actuation-stalled` after 209 ticks: 0.060 m center-of-mass displacement, 0.184 m reported path length, and 13.282 m still to the target. The controller had selected an avoidance heading of 0.873 rad (about 50°); the observed minimum camera corridor clearance was 5.198 m and the self-grid hit count was zero.

The evidence points to a mismatch between a clear ray heading and a useful rover path: the local fan chooses a direction but does not establish clearance for the rover's full swept footprint while its two steering wheels turn. No contact event was recorded, so the data do not prove that the truck hit the wall. The no-progress guard correctly neutralized the controller rather than continuing to push.

The successful route avoided that wall-facing turn by using known clear waypoint locations. The direct-path stall itself is not fixed by a broader obstacle planner; the engine trial shows that the existing controller can execute a safe waypoint corridor without manual steering.

## Engine results

- 20 bounded control operations: four reached their waypoint; 16 hit the per-operation tick cap, made positive progress, and resumed from the persisted pose.
- 33,117 total control ticks; 60.659 m summed controller path length across the bounded operations.
- Final rover center of mass: `(51.293, 14.618, 35.991)` m.
- Final world-space distance to Green Zone: **3.098 m**, inside the configured **3.1 m** arrival radius. The controller stopped with a 0.004 m/s position-derived speed and zero drive input.
- Minimum reported depth-corridor clearance across the route: **7.154 m**; self-grid hits: **0**; fatal markers: **none**.
- All operations shut down cleanly and reported zero fixture edits. The disposable server is now inactive.

No collision was observed in the available telemetry. The lab receipt does not include a complete physics-contact counter, and a sparse camera fan cannot prove that every part of the rover remained clear at every instant. Therefore this is a successful engine traversal with no observed collision, not a formal proof of zero contact.

## Validation and limits

- Voidwright core checks: **33/33 passed**.
- Obstacle simulation scenarios: **5/5 passed**. The separate reactive-depth-policy probes still stop with `NoClearCorridor`; they are not evidence of a successful autonomous obstacle detour.
- Server-plugin compile check against the local Space Engineers SDK: **passed, 0 warnings and 0 errors**.
- The dedicated fixture reported `definitions_loaded=false` and `scripts_loaded=false`, with warnings for `Unlimited Raycasting` and `GFA - Repulsorlift Engines`. Native client behavior and those mod features were not tested.
- No source-code changes, commits, pushes, or production actions were made in this continuation. Existing dirty work was preserved.

## Evidence

- Full per-operation records and route manifest: `artifacts/land-ramp-green-2026-10-17/`.
- Manifest: `engine-route-manifest.json` — SHA-256 `e47473b9e3afc2d7cf232104de3f5fd405eb31acc7da13ad7750621f1e9a592d`.
- Final clean-stop receipt: `green-leg7/terminal-receipt.json` — SHA-256 `476097cbcb6677f28138084edf84e992de9956e371bee976666e1b52557a08f3`.
- Staged Voidwright server-plugin tree: SHA-256 `8bf250539d2ccec8a00c3dc6814dac7c8a9283d01fcf06c3a1833a2220e8c479`.
- Disposable source world checksum recorded before staging: `a14b0a2da61cda7f07c76d2d70751ff597938473ca78807c9b0cc306d3efc4a5`.

