# V4 mod-owned movement diagnostic — 2026-10-19

## Follow-up candidate f — brake ownership and terminal behavior

This later run was a disposable, bounded diagnostic in the open parking area. It was not the obstacle course, did not attempt Ramp Bottom → Green Zone, and did not dispatch a wheel command.

### Finding

- Fresh private instance: `test-suite-world-v4-land-timeout-brake-diagnostic-20261019-f`; imported source tree SHA-256 `9ae4286ebb6fad94516612cfb083c347ccb197c62276d5658a49d65ede373f8f`. The instance and workshop cache were isolated from the earlier candidate and shared cache. The server is stopped.
- Candidate e initially failed before host activation because its staged Magnetar plugin folder omitted the three shared core sources (`MovementPreflight.cs`, `PreflightRuntimeEvidence.cs`, and `DepthAvoidancePolicy.cs`). The offline compile check had linked those files, while the staged plugin did not contain them. I stopped e cleanly, then included those exact source dependencies in f's per-instance plugin package. Magnetar compiled the plugin in f; host activation and the genuine 10/10 host preflight then appeared.
- Candidate f used operation `a9157da1f0e94b52fcec69d06561d139` (run `ad1a24260bdbe17c03de39fe380838d6`), capped at 0.25 m/s and 300 ticks. The source package tree hashes were mod `e0731e1fe3e2672ae7319b99dd94d16bec3a2398f4ed3f61cefd2059a69dfe90` and plugin `db0411d00abf37a13742b2830f8ba45269220b7bc5057f90bf9fdcb7294e40fa`.
- Host preflight: **10/10 PASS**, with run, fixture, controller, camera, and command bound. The live scan was fresh: **19/19 valid rays**, each returning the full 8 m in this open-area diagnostic.
- Mod preflight: **9/10**; `actuator_availability` failed closed. All four wheels reported `Brake=true`, `IsParkingEnabled=true`, and zero steering and propulsion overrides. The remote control reported `IsAutoPilotEnabled=false` and `IsUnderControl=false`. The mod API cannot read the server plugin's global `GridWheels.HandBrake`; the telemetry marks that field unavailable rather than guessing.
- The command ended terminally as `preflight-blocked` at elapsed tick 9, with `actuatorDispatch=none`. The server shutdown receipt reports `clean_shutdown=true` and `fixture_edit_count=0`. Candidate f is stopped and no wheel input was sent.
- This establishes a non-neutral external brake state at the mod adapter boundary; it does **not** establish which game subsystem or earlier action set it. Keen's ModAPI describes `Brake` as the wheel's applied-brake state and `IsParkingEnabled` as whether the suspension reacts to the parking brake; those properties alone do not identify the current owner or prove that parking behavior caused this state ([IMyMotorSuspension API](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/Sandbox.ModAPI.Ingame.IMyMotorSuspension.html)). The fail-closed ownership guard remains unchanged.

### Code and test status

- Added read-only per-wheel brake/parking/override details plus RC autopilot and operator-control state to mod preflight evidence. A live API compile exposed that `GridWheels` is unavailable on the ordinary mod-facing controller interface, so global handbrake is explicitly reported as unavailable.
- The operation deadline now emits a terminal timeout event and invalidated evidence even if telemetry writing fails. The live run did not reach that path because the mod preflight correctly terminated first; timeout behavior is covered offline rather than claimed as live-verified.
- Magnetar plugin compilation succeeded in candidate f with the core sources included. Voidwright Core tests: **49/49 passed**. VRageCage worker scripts: **23 passed**, including launch/preflight checks. HoloView periodic and synchronized multiview capture tests passed. `git diff --check` passed in both repositories.
- Evidence bundle: [`artifacts/land-v4-brake-ownership-diagnostic-20261019/`](../artifacts/land-v4-brake-ownership-diagnostic-20261019/). It contains the command, distinct host and mod preflight JSON, run and terminal receipts, source/stage receipts, and selected non-sensitive event lines. It omits the full server log and its unrelated network/runtime data.

### Blocker and next step

No steering, route, collision, or route-completion result was produced. The 19-ray open-area scan does not demonstrate obstacle classification. Because existing wheel brakes are non-neutral and the source of that state is still unknown, the adapter must continue preserving them. Further autonomous movement is blocked unless a supported, authorized brake-ownership handoff is established for the disposable fixture. The Ramp Bottom → Green Zone route remains unverified.

## Follow-up candidate g — direct grid brake observation

To distinguish grid-level handbrake state from per-wheel brake state, the host preflight now records `GridWheels.HandBrake` and `GridWheels.Brake` as read-only evidence. This changes telemetry only; the adapter and its refusal to adopt externally braked wheels remain unchanged.

- Candidate: `test-suite-world-v4-land-timeout-brake-diagnostic-20261019-g`, run `12ecf558cbc52f2a5be02bcc58b09fc5`, operation `8df2dc66277697c793ba3bfa1ec7a9f6`. Host preflight passed **10/10** and the Magnetar plugin compiled successfully with the included shared core sources.
- Host evidence reports `gridHandBrake=false` and `gridBrake=false`. At the same instant, the mod evidence still reports `Brake=true` on all four individual wheels, `IsParkingEnabled=true`, no steering/propulsion overrides, `IsAutoPilotEnabled=false`, and no operator control. The host preflight therefore rules out the grid-wide handbrake being active at that sample; it does not identify who set the individual wheel brake flags.
- The source save's controller record has `AutoPilotEnabled=false`, and each wheel record has `IsParkingEnabled=true` with zero overrides. No `Brake` field is serialized for those wheel builders in this source snapshot. This shows the per-wheel brake value is observed at runtime rather than specified in that saved block record; it does not prove whether the engine initializes it or another runtime component changes it.
- The operation terminalized as `preflight-blocked` at tick 9 with `actuatorDispatch=none`. It was stopped cleanly with `fixture_edit_count=0`; the original V4 save was not touched. This remains an open-area preflight diagnostic, not an obstacle course trial.
- Candidate g evidence, including the host and mod records plus a readout of the imported source controller state, is preserved in [`candidate-g/`](../artifacts/land-v4-brake-ownership-diagnostic-20261019/candidate-g/).

Keen's archived controller source maps the terminal HandBrake control to `GridWheels.HandBrake` and human wheel input to `GridWheels.Brake` when a player is controlling the ship. The fresh run's host and mod evidence reports both grid values false and no operator control, so that documented path does not explain the sampled individual wheel `Brake=true` state. The available source/API evidence still does not identify its runtime owner ([Keen controller source](https://github.com/KeenSoftwareHouse/SpaceEngineers/blob/master/Sources/Sandbox.Game/Game/Entities/Blocks/MyShipController.cs), [motor suspension API](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/Sandbox.ModAPI.Ingame.IMyMotorSuspension.html)).

## Follow-up candidate d — 2026-10-19

This follow-up supersedes the earlier “no second movement command” status below. One additional bounded diagnostic was authorized and attempted in a new disposable copy. It did not dispatch wheel input, did not reach Green Zone, and is not a route-completion result.

### Result

- Candidate: `test-suite-world-v4-land-preflight-20261019-d`, imported from the unchanged disposable source tree `9ae4286ebb6fad94516612cfb083c347ccb197c62276d5658a49d65ede373f8f`.
- One `drive-to-coordinate-depth` command targeted Green Zone at 0.25 m/s, 300 ticks, and 0.5 m arrival radius. Exact command: `artifacts/land-v4-mod-owned-diagnostic-20261019-followup/command.json` (SHA-256 `20a889ad2f1e406bc2eb5e8f7e74e9d2ce11ad4804782ebead566c9600e76d0c`).
- The two preflights are separate records: `host-movement-preflight.json` reports 10/10, while `voidwright-mod-preflight.json` reports 9/10. The mod's `actuator_availability` failed. Its generic reason says steerable/propulsive wheels exist, but the evidence and progress telemetry show all four brakes active, steering/propulsion overrides zero, and `actuatorDispatch=none`. The adapter therefore did not take ownership of the braked controls. The host checked channel counts and missed this ownership condition.
- Sensor status also has contradictory evidence in the host record: `sensor_validity_freshness` is PASS and its evidence says `scanReady=True`, 19 rays, age 0, but its reason says “No fresh depth scan is available.” The mod-owned record independently says PASS with 19/19 fresh rays. A local source correction now reports the fresh-scan reason when `scanFresh` is true; that descriptive fix has only compile-check evidence, not a new runtime record.
- No wheel override or steering command was dispatched. The controller settled from about 0.053 m/s to zero; position changed only 0.003 m over the logged samples and target distance changed from 77.915 m to 77.918 m. This is not route progress. Collision contact remains unavailable from the current ModAPI.
- The command was configured for a 300-tick timeout, but the saved event sequence continued reporting `progress` through elapsed tick 1380. There is no completion, timeout, or preflight-blocked terminal event. `terminal-receipt.json` is a worker/service shutdown receipt: it records `lab_operation_status=progress`, `lab_operation_terminal=false`, `passed=false`, and `clean_shutdown=true`. The last field means the service shut down cleanly; it does not mean the operation completed. The candidate is inactive, the pending command is gone, the original save was untouched, and no further movement command was issued.
- The earlier 64×36 sensor-only capture is preserved at `artifacts/land-v4-mod-owned-diagnostic-20261019-followup/sensor/`. It had 872 hits, all attributed to the platform grid, no self-grid hits, and a valid artifact receipt. This confirms a sensor capture, not semantic wall classification or a collision-free path.
- The command, distinct host and mod preflight records, worker shutdown receipt, and full game log are preserved alongside one another. Their hashes and a concise reconciliation are in `artifacts/land-v4-mod-owned-diagnostic-20261019-followup/reconciliation.txt`. Worker receipt SHA-256: `e69ab5bed0fdaf49dccb4a4188fba30c1b8cdb4d190b1e3207069f02f0e072c1`; game log SHA-256: `be530880984370e0a43c806afb3ffde5c5ef2c7a9e87542cb4e9093b46e7b07e`.

### Local fail-closed repair and verification

The mod now turns a fresh host approval plus any incomplete/failed mod-side check into a terminal, neutral denial instead of looping indefinitely. The adapter still refuses to adopt externally braked wheels. A regression test covers the split-preflight case. The host-plugin diagnostic text was also corrected so a fresh scan is not described as missing. Both changes are local only; they were not staged into candidate d or tested in-game.

- Voidwright Core tests: **48/48 passed**; `git diff --check` passed.
- Current mod source built against the private V4 server assemblies with 0 warnings/errors; `Voidwright.dll` SHA-256 `980ea1b251b20f6ecadf449cb7c211f115273539fee6dc558b280d689eab3fb2`.
- Server-plugin compile check built with 0 warnings/errors; SHA-256 `223a2b0c762c3cc9801d81c5e3ccf07b62dfc14910ac17954ae5272000f2e1f5`.
- A future movement test needs a fresh disposable run with the fixture's wheel state made safely adoptable and the updated mod staged. No such run is authorized by this one-command diagnostic allowance. Ramp Bottom → Green Zone completion and collision-free behavior remain unverified.

## Earlier candidate c result

The disposable V4 server loaded the synthetic Workshop build of Voidwright and the server plugin. The exact run-bound activation, mod heartbeat, host approval, live sensor binding, vehicle identity, and all ten host preflight checks passed. The one bounded `drive-to-coordinate-depth` diagnostic did **not** produce a terminal operation receipt. No second movement command is authorized or queued.

This was a low-speed diagnostic toward `Platform Center`, not the Ramp Bottom → Green Zone acceptance route. It was capped at 0.25 m/s and 300 simulation ticks, with the front camera and the left-offset target bound in the command.

## Earlier candidate c evidence

- Private disposable instance: `test-suite-world-v4-land-preflight-20261018-c`.
- Run / operation: `097d1c38dbf14e3e137e442dbf1aac26` / `b6601440164c74e3f5c8059005d5d6ca`.
- Synthetic Voidwright package SHA-256: `33ccbc4d19d7f46db389c17b1bf39759a22017ba879de5eef7b0cd10af683e5c`.
- Server plugin tree SHA-256: `59f70eec50c70cbc93b77d8485840dd3942ca5ff6706fbc62d1bd46d65c2a84a`.
- Host log recorded the genuine Voidwright session marker and fresh `(10/10) Preflight success`. The final heartbeat was bound to controller `137709089579453168`, grid `73396920176713659`, and camera `74011513536052165`, with 19 valid rays and four wheel channels (two steering, four propulsion).
- The worker receipt contains only `status=accepted`; it has no depth-controller progress or terminal event. The mod preflight JSON file was also absent. Therefore no trustworthy velocity history, wheel-angle history, route completion, or collision outcome is available.
- Comparing the imported disposable baseline with the stopped candidate's saved grid pose shows a 0.040398 m position delta and a 0.289932° forward-vector delta. This confirms a small saved pose change, but does not distinguish controller motion from ordinary physics settling. Saved steering and propulsion overrides are zero in both files. Do not interpret the small pose delta as route progress.
- The exact owned service is inactive, its pending-command file is absent, and its terminal receipt records clean shutdown, unchanged staged package hashes, and zero declared fixture edits. The workstation original save was not used or changed.

## Earlier instrumentation finding and local repair

In the ordinary mod's periodic evidence condition, `simulationTick - lastEvidenceTick` was evaluated while `lastEvidenceTick` was `long.MinValue`. In unchecked `long` arithmetic that first subtraction wraps negative, suppressing periodic evidence. Also, for mod-owned depth commands the server plugin deliberately does not run the legacy actuator/controller path, so it never emits the operation events the worker receipt parser relies on. Together these explain why the harness remained at `accepted` despite genuine preflight success; they do not establish whether an actuator step occurred.

Local uncommitted changes add an overflow-safe evidence cadence policy, a regression test for the sentinel/boundary cases, and bounded mod-owned progress/terminal log events with pose, target distance, and speed. The worker receipt now treats the added fail-closed terminal statuses as terminal, and its regression checks cover a progress event followed by completion.

## Earlier verification and limits

- Voidwright Core suite: **48/48 passed**.
- VRageCage worker script suite: **23 tests passed**; additional HoloView capture and multiview suites passed.
- `git diff --check`: passed in both repositories.
- Current-source build using mpai's bundled .NET SDK 10.0.401 and the private V4 server assemblies: `Voidwright.dll` built with **0 warnings and 0 errors**, SHA-256 `fa13ce99e24777007de89e073f2bd442e8573d9997466b7556ec85dc2789902d`. The server-plugin compile-check also built with **0 warnings and 0 errors**, SHA-256 `7009307b8eeb5147239c7dc72a41d95002f455285990462f0f6ad8952e6ee306`.
- Build outputs and copied current sources are retained under `/home/alu52/.local/share/vragecage-v4-private-20261018/builds/voidwright-current-source-telemetry-20261019`. The core suite passed **48/48** on the workstation's .NET 8 SDK. A repeat on mpai was unavailable because its bundled SDK has .NET 10 runtime only; this does not affect the successful workstation test.
- The current-source build artifacts were not staged into the fixture or loaded by the server. The retained runtime fixture still contains the earlier staged mod/plugin hashes recorded above; there was no post-fix game run.
- No further server boot or movement dispatch was performed. A future test requires fresh user authorization for a new movement command, followed by a build of the modified mod and a new disposable fixture/run. The existing route acceptance still requires genuine arrival and collision evidence.

## Follow-up candidate h — correct the wheel Brake interpretation

This SDK inspection supersedes the candidate f/g and d descriptions that treated mod-reported `Brake=true` as an active brake input.

- The installed local game assembly `/home/alu52/.local/state/voidwright/tooling-20261015/server-sdk/Sandbox.Game.dll` has SHA-256 `f58e7025a3c768f0908ece08474958912092f0f446ae7d30cb040c3003c0ae01` (`Sandbox.Common.dll`: `c75d283d3002e5d95ff4cb2267762b15bdbfca0d533a9817ed9d25724325fb16`). Decompilation shows the concrete `MyMotorSuspension.Brake` is the active wheel brake state, but its explicit implementation of `IMyMotorSuspension.Brake` gets and sets `BrakingEnabled`. `IsParkingEnabled` is a separate setting. This installed implementation disagrees with the ModAPI property's published description.
- `MyGridWheelSystem.UpdateBrake()` propagates the grid's active `Brake` and `HandBrake` values into each concrete wheel. Candidate g recorded `GridWheels.Brake=false` and `GridWheels.HandBrake=false`; it did not record the concrete per-wheel values. The old mod evidence therefore reported the wheel braking-enabled configuration, not proof that an active brake was on. The “external brake owner” conclusion was unsupported. An idle remote control does not need to release that reported value.
- The mod adapter no longer reads or writes `IMyMotorSuspension.Brake` as a movement actuator. Its ownership gate and stop plan cover only steering and propulsion overrides, leaving `BrakingEnabled` and parking configuration untouched. Host readiness now refuses dispatch if the actual grid brake or handbrake is active and records each wheel's concrete active brake, handbrake, braking-enabled, and parking-enabled states.
- A terminal timeout now preserves status `timeout` even if stopping an owned override reports an error; the stop error remains in the reason. It still cannot guarantee engine braking from the ordinary ModAPI path: the adapter's timeout stop neutralizes only owned steering and propulsion overrides. This limitation must be accounted for in any future movement envelope.
- Core tests pass **50/50**. Mod and server-plugin compile checks pass against the installed assemblies with **0 warnings and 0 errors**. The VRageCage timeout-receipt fixture now includes a stop-error reason to ensure the worker still recognizes the terminal timeout. No new live preflight or movement was performed after this correction.

### Current status

The root cause of the tick-9 preflight denial is identified and the local fix is compiled, but both preflights have not yet been rerun with the corrected package. The next safe check is a zero-displacement disposable operation that can verify fresh host and mod preflights without dispatching a steering or propulsion value. A movement run remains unverified, and the full V4 obstacle-course route has not been attempted.

## Candidate c — bounded open-area movement probe — 2026-10-19

After a fresh 10/10 host preflight, one distinct operation was allowed to drive toward a nearby target in the open-area disposable copy. This was a diagnostic of real actuator response, not an obstacle-course attempt.

- Fixture `v4-open-area-1m-20261019-c` (`8fb1ada975421fd19c9c1144232cfac1`), run `20a22428c3e217071f7e0324e11fcf26`, operation `8bd36ee678013018c867dffbdcb92335`. The command was pinned to SHA-256 `b1e74708c0ab024da71127ef9293654980e58a645596946490a35441fe2235d5`; host and mod preflights both passed. The command capped speed at 0.1 m/s and had a 0.5 m arrival radius.
- Genuine Magnetar/ModAPI telemetry shows actuation occurred: steering and propulsion overrides were applied, and 19/19 depth rays were valid. From the 60-tick sample (distance 0.986 m) the truck briefly closed to 0.878 m at tick 300. It then drifted steadily away, reaching 1.842 m at tick 1680 while moving about 0.08 m/s. Steering override reached -1 while measured wheel angle was +0.261 rad. This is a failed movement probe; no target arrival or route completion occurred.
- Collision contact is unavailable through the current ModAPI evidence. Some rays that initially read 8 m later read about 4.2 m; this establishes changing sensor returns, not a collision. The worker stopped the disposable server cleanly at tick 1680. Its save sector changed during engine operation; only the disposable copy was active, and no source save was loaded for writes.
- The increasing target distance and command/actual-wheel-angle records make the steering/frame mapping the leading controller-side hypothesis, but orientation and wheel-angle sign conventions need an explicit controlled comparison before changing code. The next experiment should log controller basis vectors alongside target-local right/forward, command heading, steering override, measured wheel angle, and world velocity. Do not repeat this target as another movement command until that trace explains the divergence.

The full Ramp Bottom → Green Zone acceptance remains **not demonstrated**. Existing movement proof from the earlier course trial is separate from this failed V4 probe and does not establish repeatability on this fresh V4 copy.
