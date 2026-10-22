# Driving Lab capture foundation

This document describes the offline data contract, bounded JSONL replay reader, and opt-in exact-grid producer seam. The producer polls only a server observation source for one grid already selected under a trusted explicit opt-in, then appends through the existing bounded JSONL buffer. The source interface exposes observation only; no observer is registered by the mod or server plugin, and no live producer test occurred.

## Record contract

`DrivingLabCaptureRecord` uses schema version 1 and binds each sample to a run, session, stream, source, UTC timestamp and clock domain. Simulation tick/time and exact grid ID are optional because current producers do not provide a common clock or an arbitrary-grid observer. Configuration SHA-256 and cumulative dropped-record count are optional until a producer can report them. The reader is limited to 1 MiB per line, 64 MiB total, and 100,000 records.

Human input, actuator observations, physics, pose, grid state, software geometry, and visual frames are separate sections. Each section is `observed`, `partial`, or `unavailable`, with a reason required for partial or unavailable data. The actuator section defaults to unavailable and, when populated, must use the ownership marker `observed_only_no_control`. These fields describe observations; they do not provide commands or actuator adapters. The producer buffer rejects backward simulation ticks/times before append, and replay rejects them in existing files. Replay also rejects duplicate JSON object keys, invalid identity/time bindings, duplicate or backward sequence numbers, duplicate actuator entity/kind pairs within a record, and decreasing producer drop counters. Replay ordering is scoped by run/session/stream/grid identity. Forward gaps are preserved as `MissingSequencesBefore`; the reader does not interpolate them.

## Current source capabilities

- **Grid selection:** The current `AgentCoreSnapshotBuilder` observes physical grids enrolled through an Agent Core. It does not offer a passive explicit-grid subscription independent of enrollment. Snapshot export runs on the server path every 300 simulation ticks and is bounded to eight agents and 16 MiB.
- **Human input:** No human-attributed vehicle input axes are captured today. The installed client API does expose raw local device state, while controller movement indicators combine user and autopilot input; see the SDK inventory below. Existing player observation is an anonymous character pose only when exactly one human is connected.
- **Actuators:** Navigation preflight and telemetry can report observed wheel/controller state while a navigation operation is active. That path is operation-bound. This foundation does not query actuators, and its `observed_only_no_control` marker is a schema guarantee rather than a live runtime claim.
- **Physics:** Grid snapshots expose velocity and gravity at structural snapshot cadence. Higher-rate motion telemetry is currently coupled to a navigation operation.
- **Software geometry:** Enrolled construct snapshots expose bounded grid/block geometry and coverage flags. They omit terrain, unrelated entities, detailed meshes, and physical contacts. Virtual perspective raycasts and HoloView software rendering use that bounded geometry, not the game framebuffer.
- **Visual frames:** A bounded HoloView software-rendered frame exists through the separate MCP renderer. No native game-client framebuffer capture path is present. Renderer counters and game simulation ticks are distinct clock domains.
- **Uncertainty and drops:** Existing snapshots report truncation/coverage; they do not provide a shared uncertainty model or a producer-side dropped-sample counter. The capture schema carries explicit fields for these but does not invent values.
- **Replay/comparison:** The offline replay viewer validates/sequences JSONL and compares two streams only under an explicit simulation-time or start-offset policy. It does not infer missing samples or UTC synchronization. Missing producer drop counters remain unknown in its summaries.

## Next live-capture prerequisite

Before adding observer plumbing, define and review the producer API that selects one exact grid without Agent Core enrollment, navigation, or control ownership. The live implementation must prove that selection remains exact as grids split/merge, stays on the authoritative server, and never enters actuator dispatch. A disposable, single-player fixture with a verified source hash and a stopped baseline must then be explicitly prepared; only after that should passive capture be tried. Human input, native game-client framebuffer capture, and run comparison remain separate follow-up capabilities.

## Exact-grid passive-selection contract

`AgentMcp/DrivingLabPassiveSelection.cs` defines the offline selection boundary. A trusted caller must supply explicit opt-in for one positive grid ID, opaque 32-character session and run IDs, UTC confirmation/expiry, and capture limits. The contract binds that tuple only against a complete, fresh (at most five seconds old) passive grid inventory carrying the same session/run IDs. Missing IDs, duplicate IDs, incomplete inventories, stale/future timestamps, expired consent, and mismatched identities are rejected. Consent expires within 15 minutes. The maximum configured capture rate is 10 Hz; a selection may buffer at most 100,000 records and 64 MiB. The returned selection has only the `read_only_observation` permission scope and contains no control operation.

The request's `ExplicitOptIn` flag is a contract input, not an authentication primitive. With the Voidwright client mod, `/vw telemetry consent` sends a bodyless start request through the authenticated secure multiplayer channel. The passive server plugin derives the player from the transport sender and verifies a unique connected human, cockpit occupant, and exact current grid. The Magnetar server command path also exposes `!vw telemetry help`, `!vw telemetry consent`, and `!vw telemetry stop`, so clients do not need the Voidwright mod for consent. These commands are available to ordinary players; consent is bound to the authenticated caller identity and the grid whose cockpit they currently occupy. Help discloses the fields and limits before opt-in, and stop revokes only that caller's capture. No player or grid ID is accepted as a command argument. The dedicated `DrivingLabPassiveTelemetryPlugin` assembly contains only this consent protocol, chat commands, and the read-only grid observer. VRageCage stages it through a hash-bound `read_only_observation` contract that is distinct from, and incompatible with, the destructive-lab contract. Launch validation creates a fresh passive run identity without setting destructive-lab authority or creating a command quarantine. The older adapter copy inside the general actuator plugin remains destructive-lab-only; the passive assembly is the intended human-driven workflow. Capture writes completed JSONL and summary files to shared `VRage.Game` WorldStorage on revoke, expiry, binding loss, plugin dispose, or bounded termination. The separate VRageCage `passive-telemetry-export` command retrieves a unique completed pair by exact instance and run ID; it validates the pair and copies it locally without worker writes. Its displayed metadata omits player identity, while the stored consent summary retains that binding for audit. The 10 Hz ceiling is a configured cap, not a claim of wall-clock scheduling precision.

## Installed Mod SDK/runtime API evidence

The inspected SDK is `/media/alu52/GSSD/SteamLibrary/steamapps/common/SpaceEngineersModSDK/Bin64_Profile`. The local `VRage.Game.xml` says WorldStorage paths include `{Assembly Scope}` and that `callingType` identifies the mod class. ILSpy 9.1 decompilation of the installed `Sandbox.Game.dll` implementation `Sandbox.ModAPI.MyAPIUtilities` shows `ReadFileInWorldStorage` and `WriteFileInWorldStorage` derive that scope from `callingType.Assembly.ManifestModule.ScopeName`, then place files beneath `MySession.Static.WorldSavePath/Storage/<scope>`.

Both the Voidwright mod and server plugin pass `typeof(MySessionComponentBase)`, whose defining assembly scope is `VRage.Game`; candidate-k logs also show `callingType='VRage.Game.Components.MySessionComponentBase'` and its save contains `Storage/VRage.Game`. Therefore the installed implementation supports a shared WorldStorage directory for these two callers when they run against the same world and keep passing that common type. This is implementation evidence, stronger than the XML's general path documentation; it does not say arbitrary plugin/mod calling types share scope. Candidate-k's heartbeat absence is explained by its tick-zero terminal failure before the writer was eligible, not by a different scope.

For brakes, local `Sandbox.Common.xml` documents the ingame `IMyMotorSuspension.Brake` property as a brake setting, and decompilation confirms that explicit interface property maps to `BrakingEnabled`. It is not the live per-wheel brake input. The installed `Sandbox.Game.dll` host implementation exposes `MyShipController.GridWheels` as `MyGridWheelSystem`; its `Wheels` collection contains `MyMotorSuspension` objects with separate `Brake` and `Handbrake` getters. The host can therefore inspect per-wheel active brake and handbrake state, and it also has grid-level `Brake` and `HandBrake` state. The current plugin uses those properties read-only in preflight telemetry. This is based on installed assembly metadata/decompilation and compile-time access, not a fresh runtime observation; the mod-facing interface still cannot prove those brake inputs itself.

SDK evidence file hashes: `VRage.Game.xml` `029c963cfd520fdbf8d10f1f4c841273a2bd4b1bbc2ade4dc0c814a27015acda`; `Sandbox.Common.xml` `0ee06292dcb76ae06fded953d18c07d3251c9666cc5e1b32a723ccc8a516d655`; `Sandbox.Game.dll` `29bd826ad29f1b42b0be418df54b1da64cdd06f40e44750405b7dc86f28db7f8`.

## Observation source and timing boundaries

## Permission boundary for the human-driven workflow

| Capability | Gate | Granted scope |
| --- | --- | --- |
| Multiplayer participation | Existing server/world access configuration | Join and play; no telemetry enrollment or plugin control follows from joining. |
| Telemetry enrollment | `/vw telemetry consent` over the authenticated secure channel; server resolves the sender and requires the sole connected human to occupy one cockpit | Start bounded capture for that exact grid. `/vw telemetry stop` revokes consent for the bound player. |
| Read-only observation | Separate `voidwright-passive-telemetry` assembly staged on a stopped Magnetar instance under a hash-bound `read_only_observation` contract | Capture grid pose, speed, simulation time, and observed wheel state after consent. The package denies movement, actuator control, programmable-block control, and world-entity changes; it only writes its consented telemetry files to WorldStorage. |
| Autonomous vehicle control | Existing general Voidwright server plugin, destructive-lab authority contract, validated run/quarantine environment, and an exact bounded operation command | Test-harness control operations. The passive observer package is not part of this path. |

The passive staging contract and destructive-lab contract cannot coexist on one
instance. Ordinary `stage-plugin` remains destructive-lab-only; the separate
`stage-passive-telemetry` command accepts only the reviewed telemetry package
identity and records its tree hash. This separation is enforced by the worker
stager and launch verifier, not just by user-facing documentation.

- **Input:** No raw steering, throttle, or brake axes are available. The existing player observation is an anonymous character pose when exactly one human is connected; it is not vehicle input and is withheld when player count is ambiguous.
- **Geometry and views:** Current grid/block geometry is bounded to Agent Core enrollment and reports coverage. It excludes terrain, unrelated grids, detailed collision meshes, and physical contacts. Virtual viewpoints and `raycast_voidwright_perspective` operate on that snapshot geometry. HoloView produces a dedicated-server software raster from the saved-grid geometry and configured camera/output blocks.
- **Client framebuffer and Codex camera markers:** No native Space Engineers client framebuffer path is present. The `VW CAM`, `VW LOOK`, and `VW UP` GPS markers display a server-published virtual pose in a player's client; they are display-only points, not captured pixels or a controllable spectator camera. They do not fill the visual-frame channel in this schema.
- **Replay:** The JSONL viewer validates one stream's sequence, preserves gaps, and rejects backwards sequence/drop counters. Its comparison mode requires explicit alignment and does not interpolate. Missing producer drop counters are shown as unavailable rather than zero.
- **Timing uncertainty:** UTC timestamps are producer wall-clock times; simulation ticks are local to their producer/run. Snapshot export currently occurs every 300 simulation ticks, while HoloView rendering and client markers use separate counters/updates. There is no shared monotonic clock or cross-source tick mapping. Preserve both clock domains and age/uncertainty; do not claim sample-level synchronization.

## Installed Mod SDK API inventory (2026-10-19)

Inspection used `/media/alu52/GSSD/SteamLibrary/steamapps/common/SpaceEngineersModSDK/Bin64_Profile`, the installed `Bin64_Profile` API assemblies/XML dated 2026-10-18. The inspected folder has no separate semantic SDK version marker, so it is identified by path, file timestamps, and SHA-256 values below rather than a guessed game release. API names are from local XML/decompiled metadata, not runtime probes.

| Evidence | API and available data | Runtime boundary and limitation |
| --- | --- | --- |
| Manual device input | `Sandbox.ModAPI.MyAPIGateway.Input` is `VRage.Input.IMyInput` in `VRage.Input.dll`. Members include `IsKeyPress`, `IsNewKeyPressed`, `GetJoystickAxisStateForGameplay`, `GetJoystickPositionForGameplay`, and `GetJoystickRotationForGameplay`. | This is local input-device state and is suitable only for client-side local sampling. It does not identify the active ship/controller or prove that the server accepted the control. No server-side physical-device input stream was found; dedicated-server access is unsupported/unavailable and must be null-checked. |
| Controller indicators | `Sandbox.ModAPI.Ingame.IMyShipController.MoveIndicator`, `RotationIndicator`, `RollIndicator`, `HandBrake`, `ControlWheels`, and `IsUnderControl`. | These are controller/world state available to gameplay code, including server observers with the entity present. SDK documentation calls move/rotation values “user/autopilot” input: they do not separate human from autopilot or preserve keyboard/gamepad provenance. They are the smallest server-observable input channel, not a human-input audit stream. |
| Grid and block geometry | `VRage.ModAPI.IMyEntities.TryGetEntityById`/`GetEntityById`; `VRage.Game.ModAPI.Ingame.IMyCubeGrid.GridSize`, `Min`, `Max`, `GetCubeBlock`; and `VRage.ModAPI.IMyEntity.WorldMatrix`/`PositionComp`. Mod API also exposes grid/block enumeration. | Server and client world observers can resolve an authorized exact entity ID and extract bounded block/cell geometry. Keep inventory scoped to the selected grid; broad world enumeration is not needed for exact-grid capture. |
| Terrain / voxels | `VRage.Game.ModAPI.IMyVoxelMaps.GetInstances`/`GetOverlappingWithSphere`; `VRage.Game.ModAPI.IMyVoxelMap.GetVoxelContentInBoundingBox`, `GetVoxelCoordinateFromMeters`, and overlap/intersection methods. | Game-side code can query bounded world/voxel metadata on server or client. This is not a terrain mesh or camera image. Sample only a bounded region around the selected grid. The same API exposes destructive methods such as `Clear`, `CutOutShape`, `FillInShape`, and `PaintInShape`; none belong in a read-only capture path. |
| Camera pose / raycasts | A camera block exposes its entity transform (`WorldMatrix`); `Sandbox.ModAPI.IMyCameraBlock.IsActiveLocal` identifies current-player local use, while ingame `IMyCameraBlock.IsActive` addresses whether any player controls it. `Sandbox.ModAPI.Ingame.IMyCameraBlock.Raycast`/`CanScan` provide range-limited ray observations. | Server can observe camera block state and server-authoritative ray results; local-active attribution is client-specific. These APIs do not return the native client framebuffer. Existing `VW CAM` GPS markers only visualize a virtual pose. |
| Rendered frames | `AgentMcp.PerspectiveMcpTools.CaptureView` / MCP `capture_view` requests one bounded 256x256 HoloView software-rendered frame from the separate renderer and reconciles it against the grid snapshot. | This is an external software view with separate renderer/snapshot provenance. It does not capture Space Engineers' native client camera, HUD, or framebuffer. No supported native framebuffer readback/screenshot API was found in the inspected dedicated-server plugin surface. |
| Wheel controls and brake state | `Sandbox.ModAPI.Ingame.IMyMotorSuspension.SteeringOverride`, `PropulsionOverride`, and `Brake`; `Sandbox.ModAPI.Ingame.IMyShipController.HandBrake`. Host internals expose `MyShipController.GridWheels`, `MyGridWheelSystem.Wheels`, and each `MyMotorSuspension.Brake`/`Handbrake`. | Mod-facing steering/propulsion overrides are writable actuator settings, not raw manual pedal samples. The installed implementation maps the mod-facing wheel `Brake` property to `BrakingEnabled`; host internal wheel getters expose active brake/handbrake signals. Current server preflight uses those host getters read-only; the mod cannot independently establish live brake-input ownership. |

### Exact-grid producer and consent boundary

`DrivingLabPassiveCaptureProducer` is an offline producer over `IDrivingLabPassiveGridObservationSource`. Its start path binds a trusted caller-supplied opt-in to a fresh exact session/run/grid selection. Each poll asks the source for only that selected grid, rejects stale or mismatched observations, distinguishes available physics, pose, grid state, and wheel observations from unavailable channels, and appends through `DrivingLabCaptureBuffer`. The producer applies the selected sample-rate ceiling before polling, preserves dropped-record counts for invalid samples, and stops polling after explicit `Stop()` or opt-in expiry. The buffer continues to enforce sample rate, record, byte, timestamp, and identity limits.

## Player-consented offline enrollment prototype

`AgentMcp/DrivingLabPlayerConsent.cs` layers a player-bound consent contract over the exact-grid producer. It binds explicit consent and authenticated player identity to fresh session/run/grid inventory. Other participants may be connected elsewhere; the selected grid must have only the consenting player as cockpit occupant and human controller. Missing identity verification, ambiguous occupants/controllers, another player on the selected vehicle, another occupant grid, stale/mismatched grid inventory, expired consent, and limits above 10 Hz / 100,000 records / 64 MiB fail closed. The participant roster is used only for this check and is never exported. The resulting scope is `read_only_observation`; `Revoke()` prevents any further telemetry source reads. Focused offline tests exercise bystanders, wrong player/grid, expiry, revocation, ambiguity, identity-verification failure, limits, exact source attribution, and the absence of human-input/control claims.

The policy input types alone are not an authentication boundary. The client mod now exposes `/vw telemetry help`, `/vw telemetry consent`, and `/vw telemetry stop`; its start/stop wire payload contains only one operation byte. The standalone server plugin derives the player from the secure-message sender, resolves the game identity, finds that player's occupied cockpit, and revalidates the exact-grid single-human-occupant/controller binding during capture. Other connected participants are permitted when they are not occupying or controlling the selected vehicle. The installed SDK documents `IMyCharacter.ControllerInfo.ControllingIdentityId` as the controlling identity, matching `IMyPlayer.IdentityId`. The code and policies compile and pass offline tests, but no authenticated live-player behavior has been exercised.

The offline server adapter samples selected-grid pose, physics linear velocity and speed, `GameplayFrameCounter`, elapsed simulation time, and host wheel override/brake state. Its JSONL formatter preserves `server-passive-grid` provenance, UTC sample timestamps, simulation tick/time, exact grid/run/session identity, sequence, and cumulative dropped-record count. It distinguishes steering/propulsion overrides, configured brake state, active brake, and handbrake as observation-only actuator fields. A summary sidecar records final record/drop/byte counts, selected player/grid, and timestamp bounds. Capture data stays in a bounded in-memory buffer and is written to WorldStorage only on explicit revoke/stop, expiry, invalidated identity/occupancy, plugin disposal, or terminal shutdown. This does not reveal keyboard, gamepad, throttle, or steering intent: controller indicators conflate human and autopilot, wheel overrides are actuator state rather than physical input, and no server device-input stream is established. `HumanInput` remains unavailable unless a separate opt-in client input channel is implemented and its player, device, clock, and controller attribution are independently verified. The server cannot infer player intent from vehicle telemetry alone.

The legacy passive adapter inside the general server plugin remains destructive-lab-only and requires the active run ID plus the Voidwright mod heartbeat session ID. The separate passive telemetry assembly has no movement command parser or actuator-control code; its only start path is the authenticated player opt-in message. No MCP tool invokes capture, and no player consent, live producer test, or runtime binding test has occurred. Wheel properties are observations only. Controller move/rotation indicators must not be reported as human input because they also represent autopilot. Every record reports human intent as unavailable with reason `human_intent_unavailable_server_indicators_mix_autopilot`. Any client device-input stream would need separate opt-in, stream identity, sequence/timestamp, and clock-alignment uncertainty. Reject missing, stale, duplicate, or changed identities; never fall back to world-wide polling.

Do not treat multiplayer access or a model-supplied value as telemetry consent. The player must use the in-game command, and the server must derive player and cockpit identity from the authenticated connection/game state. Default to no capture, at most 10 Hz, bounded buffers/retention, explicit stop/expiry, and no actuator capability. A dedicated-server observer must not collect unrelated players' local device input or start a world-wide recording.

## Propulsion adapter and runtime seam findings

The installed `Sandbox.Game.dll` implementation of `Sandbox.ModAPI.MyAPIUtilities.ReadFileInWorldStorage`/`WriteFileInWorldStorage` derives the directory from `callingType.Assembly.ManifestModule.ScopeName`, then uses `<world save>/Storage/<scope>`. Both Voidwright mod and server plugin pass `typeof(MySessionComponentBase)` from `VRage.Game`, so the inspected implementation resolves both to `Storage/VRage.Game` for the same world. This is implementation evidence, not a generic guarantee for arbitrary calling types. The current offline tests exercise production codecs/policies over in-memory strings; they do not call the engine static gateway. No isolated engine WorldStorage implementation or runtime-backed mock seam was found in the installed assemblies. A real round-trip still requires a no-movement isolated-world validation after the corrected mod publishes a heartbeat.

The plugin contains legacy route/distance actuator paths that read `controller.GridWheels.Wheels`, remember prior steering/propulsion values, and dispatch through `GridWheels.AngularVelocity`. For mod-owned depth, direct-maneuver, and preflight-only commands, however, it is a passive reviewer and explicitly does not dispatch Magnetar actuator calls. The ordinary mod adapter discovers wheel blocks and writes `SteeringOverride`/`PropulsionOverride`; stops clear only properties still matching the adapter's last attempted values, preserving changes attributed to another owner. Brake ownership remains incomplete: host internals can observe active grid/per-wheel brake state, but ordinary mod `IMyMotorSuspension.Brake` is a configuration property and is deliberately excluded from the mod stop ownership plan. No cross-component proof currently binds the host's brake observation to the mod's ownership ledger.

The direct-maneuver path remains blocked in production. The offline executor, identity-bound operation lease policy, per-channel accepted-write accounting, run-bound cancel/emergency-stop acceptance, and stationary terminal receipts are implemented and tested. `ServerPlugin` still parses/attests plans and gates legacy movement commands; no production update loop advances these segments, no direct-maneuver MCP start/cancel endpoint exists, and no shared host/mod lease broker or controlled-stop dispatcher is wired. Missing/unknown brake ownership must continue blocking the path. A no-movement shared-storage round-trip and read-only host/mod telemetry comparison can establish the runtime seam, but cannot supply atomic actuator ownership or safe-stop semantics.

## Candidate-p preflight and independent propulsion status

Candidate-p completed one preflight-only run with a passing 10/10 host snapshot and terminal receipt, a fresh 19/19 sensor scan, four observed wheel channels with neutral overrides/brakes, zero fixture edits, and `actuatorDispatch=none`. Evidence and the wait-process timing caveat are recorded in `artifacts/preflight-only-20261019/candidate-p/evidence/summary.md`. No movement was attempted.

This preflight result does not implement propulsion. The next propulsion work remains the independent offline maneuver executor, cancel/emergency-stop ordering, owned-value-only cleanup, and terminal stationary evidence described above. No live producer test occurred in the capture slice, and no new fixture was started.

SDK evidence file hashes: `VRage.Game.xml` `029c963cfd520fdbf8d10f1f4c841273a2bd4b1bbc2ade4dc0c814a27015acda`; `Sandbox.Common.xml` `0ee06292dcb76ae06fded953d18c07d3251c9666cc5e1b32a723ccc8a516d655`; `Sandbox.Game.dll` `29bd826ad29f1b42b0be418df54b1da64cdd06f40e44750405b7dc86f28db7f8`.

## Runtime baseline recovery status

Existing receipts identify the source as `/tmp/voidwright-test-suite-world-v4-source-20261018`, used by imported instance `test-suite-world-v4-land-timeout-brake-diagnostic-20261019-f`. I rechecked the source tree read-only: SHA-256 `9ae4286ebb6fad94516612cfb083c347ccb197c62276d5658a49d65ede373f8f`, 21 files and 6,447,291 bytes, matching the preserved import receipts. The missing private preflight root was `/home/alu52/.local/share/vragecage-v4-private-20261019-preflight-i`, with fixture `test-suite-world-v4-preflight-only-20261019-i`. The baseline is now independently proven, so reconstructing a private root and fresh disposable fixture is within the earlier authorization to retry in a verified disposable root. This turn's instruction explicitly forbids starting a fixture; neither root nor fixture was reconstructed or started here.

The remote inventory showed that the old `preflight-i` root did exist on mpai; it was only absent from the workstation filesystem. I preserved it and created a sibling private root, `vragecage-v4-private-20261019-preflight-j`, with its own empty Workshop cache and read-only links to the existing dedicated runtime. Fresh fixture `test-suite-world-v4-preflight-only-20261019-j` imported the same verified source and used current locally built mod/plugin packages. The host and mod preflights failed closed; exact run evidence is saved under `artifacts/preflight-only-20261019/candidate-j/evidence/`. The server stopped cleanly with zero fixture edits and no actuator dispatch.

## Offline replay/export and representation limits — 2026-10-19

`scripts/render-driving-lab-replay.py` is a file-only static HTML/SVG viewer for
one versioned JSONL stream or a two-stream comparison. Each input is capped at
64 MiB, 1 MiB per line, 10,000 records, and 512 actuator items per record;
comparison accepts at most 10,000 records combined, and exports are capped at
16 MiB. It requires stable run/session/stream/grid identity within each stream,
rejects backwards sequences and cumulative drop counters, and reports gaps,
drop counts, per-section observed/partial/unavailable status, missing/null
fields, reasons, and timing metadata. Gaps and missing pose/time/value samples
break plotted paths.

Comparison requires an explicit `--alignment` policy. `relative-simulation-time`
subtracts each stream's first recorded simulation time independently.
`explicit-start-offset` additionally requires user-supplied start offsets and
their uncertainty bounds for both streams. UTC is never used to synchronize
them. Different tick rates are supported through the recorded simulation-time
axis; simulation ticks remain shown as their own per-stream evidence. Optional
`simulationTimeUncertaintySeconds` values and explicit offset uncertainties are
reported; missing capture error bounds remain `unknown`, never zero. The
viewer plots observed speed and only explicitly present humanInput channels on
the aligned time axis, while keeping source labels and run/session/grid
identity cards separate. It does not merge actuator overrides between grids
or infer human control from generic actuator telemetry; generic override
observations are charted in separate per-stream panels with their meaning
unspecified. Trajectories stay in separate panels because a shared coordinate
frame is not established.

Example relative alignment:

```sh
scripts/render-driving-lab-replay.py comparison.html \
  --compare-human human-demo.jsonl \
  --compare-attempt voidwright-run.jsonl \
  --alignment relative-simulation-time
```

For `explicit-start-offset`, also pass both `--human-start-offset-seconds`
and `--attempt-start-offset-seconds`, plus each stream's
`--*-offset-uncertainty-seconds` value. These are explicit assumptions supplied
by the reviewer, not values inferred by the viewer.

The top-down view plots observed world X/Z positions. The oblique 3D view plots
observed X/Y/Z positions and available forward-vector arrows. Both show only
sampled chassis pose; they do not reconstruct terrain, voxels, obstacles,
collisions, wheel contact, detailed meshes, occlusion, or camera pixels.
Unknown geometry stays unknown and is shown with a hatched background. Physics
and control plots preserve source field names. In particular, actuator
`overrideValue` is generic in schema v1 and is not relabeled as steering or
propulsion; input telemetry is plotted only if explicitly present and does not
establish human attribution.

Offline tests exercise sequence gaps, missing pose/time/value data, partial
fields, unavailable sections, cumulative drops, and mixed-identity rejection.
Comparison tests cover distinct run/session/grid identities, explicit relative
and start-offset alignment with uncertainty, different tick rates, gaps,
missing values, and the bounded CLI export. No live capture or game launch was
performed. The replay viewer suite passes 12/12 tests.

The propulsion dispatch gate remains independent and closed. The offline
`DirectManeuverOperationLease` now defines exact preflight binding, a neutral
wheel and inactive brake snapshot, an exclusive per-grid lease within one
injected authority, heartbeat/expiry, per-channel accepted readbacks, external
change detection, and equality-guarded cleanup. Executor command acknowledgments
are bound to exact command IDs and accepted channel readbacks. Cancel and
emergency-stop requests carry run/fixture/operation/grid/controller identity and
request IDs; terminal receipts confirm only after fresh stationary samples or
report timeout without claiming confirmation.

Installed ModAPI review shows the explicit `IMyMotorSuspension.Brake` property
maps to `BrakingEnabled` configuration; the host's internal wheel `Brake` is a
separate active signal. `SteeringOverride` and `PropulsionOverride` are
synchronized shared values without owner tokens or compare-and-set revisions.
`IMyShipController.HandBrake` and host grid brake controls are shared state.
VRageCage has process-level server stop only, not maneuver cancellation and
operation-bound stop acceptance. The missing production architecture is a
single server-authoritative host/mod broker offering atomic exclusive grid
lease acquisition, revisioned owner-token writes/cleanup, and controlled-stop
plus cancel receipts tied to fresh telemetry. The current in-process policy and
WorldStorage heartbeat cannot provide those cross-component guarantees.
`ControlledStopGuaranteed` remains unavailable, and direct-maneuver dispatch
stays blocked.

## Latest preflight-only attempt

Run `da1ad38682b73f400c4d4f3a4bee6bc1`, operation `2117e9d3c7208fad4ac606f39a3794e9`, used controller `137709089579453168`, grid `73396920176713659`, and camera `74011513536052165`, capped at 0.25 m/s and 300 ticks. The staged mod hash was `4eff7d78ed8f66ae5c40073420ed5dd24e3662f33b4e0ac706696e30186ba635`; plugin hash was `1e2a8e29a97f5c524b820cb6fbdf2c58b35f024c99db9b62b06f4e5d5275f614`. Both matched their local uploads, and Magnetar compiled and enabled the server plugin.

Host preflight finished **6/10**. It resolved the exact controller/grid and reported `gridHandBrake=false`, `gridBrake=false`, and all four wheel `activeBrake=0`, `handbrake=0`; however it never received a live mod heartbeat, so module/session, actuator availability, sensor freshness, and movement readiness remained unknown. Mod preflight finished **2/10**: runtime and source integrity passed, but its entity API resolved controller/grid/camera as `0/0/0`, with zero wheels and zero of 19 sensor rays. Position and actuator ownership were therefore unknown; safety failed because the mod could not resolve the controller/operator state. The operation timed out at tick 300 with `actuatorDispatch=none`. The clean shutdown receipt reports `fixture_edit_count=0`; the service is inactive. This does not establish mod-side brake ownership, so movement remains blocked.

## Candidate-k heartbeat and startup-status follow-up

Candidate-k's first mod-side preflight record is preserved as `artifacts/preflight-only-20261019/candidate-k/evidence/mod-preflight-first-sample.json`. At tick 0, controller/grid/camera were unresolved, there were no wheel channels or rays, speed was `NaN`, and the operator state could not be observed. The sample correctly marked identity, controller, actuator, sensor, and position checks `UNKNOWN`, but incorrectly marked `safety_configuration` `FAIL`. That definite failure caused `preflight-only` to terminalize immediately; the ordinary 10-tick entity re-resolution and heartbeat publication never got another update. The host's missing-file reads for `voidwright-preflight-heartbeat-v1.txt` occurred before and after this terminal sample. Both sides use that exact filename and `typeof(MySessionComponentBase)` WorldStorage owner; host reads on plugin updates, and mod publication is gated on resolved controller/camera at its scan cadence. The absent heartbeat is therefore consistent with the early terminal state, though it does not prove that later entity resolution or cross-component storage exchange will succeed.

The offline fix makes safety `UNKNOWN` until controller and speed are available, with regression coverage for startup retry, the exact host-approval wait state, heartbeat/approval codecs, and brake/actuator-state classification. The host approval wait requires exactly the module/session, controller/brake-attestation, and movement-readiness checks to remain unknown; any unrelated unknown or failure breaks that wait state. Candidate-m exercised the corrected retry in one isolated preflight-only fixture. Its contemporaneous mod and host records show a successful run-bound WorldStorage heartbeat/approval exchange and 10/10 checks; host brake telemetry reported no active grid or wheel brakes, while the mod observed four wheels with neutral steering/propulsion overrides. The mod then terminalized but emitted a malformed/unrecognized preflight-only receipt event and mislabeled success as `preflight-blocked`. After its heartbeat aged out, the host revoked approval, leaving an empty approval file and later 6/10 record. Offline fixes now correct terminal classification and teach the worker receipt parser to recognize the preflight-only event. Validation passed: Voidwright Core 62/62, the VRageCage worker script suite, the C# game-target build, and the server-plugin compile check, all with no compiler warnings/errors. The run itself stopped cleanly with zero fixture edits and no actuator dispatch. See [the candidate-m run diagnosis](preflight-only-retry-root-cause-2026-10-19.md#candidate-m-live-retry-with-corrected-packages) and its preserved evidence. At the time of this earlier note, the source capture record schema was only an offline JSONL contract and no capture producer had been authorized; that constraint is superseded for the offline slice below. Movement still requires established mod-side brake ownership and separate authorization.
