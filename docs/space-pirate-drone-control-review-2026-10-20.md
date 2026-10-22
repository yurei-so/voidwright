# Space Pirates drone control review — 2026-10-20

## Scope and evidence

Read-only decompilation of the workstation's installed server SDK at
`/home/alu52/.local/state/voidwright/tooling-20261015/server-sdk`, using
ILSpyCmd 9.1.0.7988. Primary implementation code is in `Sandbox.Game.dll`
(SHA-256 `f58e7025a3c768f0908ece08474958912092f0f446ae7d30cb040c3003c0ae01`);
`SpaceEngineers.Game.dll` SHA-256 is
`ab4319733da25f07153062a6f1565a0ffa1a5879b7a9c98fd2bd42fd36838c4e`.
No server, save, cache, fixture, or game session was opened or changed.

## Spawn and behavior assignment

1. `Sandbox.Game.World.MyPirateAntennas.UpdateDroneSpawning` schedules
spawn-group prefabs. `SpawnIfNearPlayers` calls `SpawnDrone` with
`antenna.OwnerId` as `ownerId`.
2. `SpawnDrone` creates each prefab and pushes two callbacks: one calls
`ChangeDroneOwnership(createdGrids, ownerId, antennaEntityId)` and the other
injects `shipPrefab.Behaviour` with `MyDroneAI.SetAIToGrid` when configured.
`MyPrefabManager.CreateGridsData.OnGridsCreated` adds the grids and pops its
callback stack, so callbacks execute last-in-first-out. The manager's own
spawn-property and prefab-spawned callbacks were pushed later and execute
before those two. Thus ownership registration runs before behavior injection.
3. `ChangeDroneOwnership` assigns each spawned grid to the supplied identity
with `MyOwnershipShareModeEnum.None`, searches that one grid for a Remote
Control, and registers the Remote Control (or grid) with `MyPirateAntennas`.
4. Pirate identity is a separate concern. `MyPirateAntennas.BeforeStart`
looks up faction tag `SPRT`, obtains or creates the Pirate identity, joins it
to that faction if needed, and marks it NPC. `GetPirateId` returns that
identity. `MyVisualScriptLogicProvider.SetDroneBehaviourBasic`, `Advanced`,
and `Full` all route to `SetDroneBehaviourMethod`; its default
`assignToPirates=true` explicitly calls `CubeGrid.ChangeGridOwnership` to
that Pirate identity with faction sharing. Setting `assignToPirates=false`
skips that ownership change, but does not supply a separate operator or
vehicle claim.
5. `SetDroneBehaviourMethod` constructs `MyDroneAI`, attaches it through
`MyRemoteControl.SetAutomaticBehaviour`, optionally enables autopilot, then
calls the behavior's `Update`. In normal operation
`MyRemoteControl.UpdateBeforeSimulation` updates an active behavior, and
`UpdateBeforeSimulationParallel` calls
`MyAutopilotComponent.UpdateAutopilot`.

`DroneGetRemote` resolves an entity by name, accepts a Remote Control directly,
or scans `GetBlocks()` on that one grid for the first Remote Control. It does
not search mechanical subgrids. `MyDroneAI.LoadShipGear` separately scans
`MyCubeGridGroups.Static.Logical.GetGroupNodes` for guns/tools. The public
decompiled paths therefore have distinct scopes: one-grid Remote Control
selection, logical-group weapon/tool enumeration, and physical-group
controller coordination. Do not infer that any one of these enumerates every
mechanically connected grid or actuator.

## Selection, authority, and physical dispatch are different

- **NPC identity/faction:** `MyPirateAntennas.BeforeStart`, `GetPiratesId`,
  and `GetPirateId` represent the NPC Pirate identity and `SPRT` faction.
- **Ownership/access:** spawn ownership receives `antenna.OwnerId`, and the
  behavior helper can separately transfer ownership to the Pirate identity.
  These are explicit game ownership mutations. They are not a safe way to
  adopt a player vehicle, and no identity impersonation or ownership transfer
  is warranted for Voidwright.
- **Controller selection:** `Sandbox.Game.GameSystems.MyGroupControlSystem`
  implements the engine's grid-control interface. It chooses a controller by
  `HasPriorityOver`; when comparing controllers on separate grids it checks
  membership in the physical grid group. It does not use Pirate faction
  identity as a movement command or as a player cockpit claim.
- **Autopilot authority:** `MyAutopilotComponent.UpdateAutopilot` requires a
  working terminal, enabled autopilot, a current group ship controller, and
  the selected controller to be its own Remote Control. It then computes
  waypoint steering, writes `MyGridGyroSystem.ControlTorque`, and computes
  `MyEntityThrustComponent.AutoPilotControlThrust` in `UpdateThrust`. The
  thrust component consumes that AI thrust in `UpdateThrusts`, computes
  `FinalThrust`, and updates physical thruster strength. This is the actual
  autonomous drone actuation route.
- **Manual/controller input:** `MyShipController.MoveAndRotate` stores
  movement/rotation indicators. Its scheduled `UpdateControls` applies
  `ControlThrust` and `ControlTorque`; when `ENABLE_WHEEL_CONTROLS_IN_COCKPIT`
  is enabled and `ControlWheels` is true, it also writes
  `GridWheels.AngularVelocity = MoveIndicator`. This is a separate controller
  path from `MyAutopilotComponent.UpdateAutopilot`.
- **Wheel dispatch:** `MyGridWheelSystem.Update` iterates registered
  `MyMotorSuspension` blocks on that grid. It calls `Steer(m_angularVelocity.X,
  speed)` for working steering wheels and `UpdatePropulsion(reverse, forward)`
  for working propulsion wheels. It can therefore physically dispatch both
  wheel steering and propulsion when a controller supplies nonzero
  `AngularVelocity`.

## Does pirate AI drive wheel propulsion or steering?

In the decompiled `MyDroneAI` → `MyAutopilotComponent.UpdateAutopilot` route,
**no**: the autopilot writes thruster and gyro requests and contains no wheel
system, `MoveAndRotate`, `ControlWheels`, steering, or suspension-propulsion
dispatch. Wheel control is reachable through the separate
`MyShipController.MoveAndRotate` route, which feeds
`MyGridWheelSystem.AngularVelocity`, but this decompilation does not show
`MyDroneAI` invoking that route. So pirate Remote Control autopilot is not a
drop-in AI driver for a wheeled Blue Truck. A grid having a Remote Control
does not establish that the NPC autopilot will drive its suspension motors.

## Voidwright reuse and Blue Truck blocker

The safe reusable parts are observational: exact Agent Core/root-grid
identity, a capped typed inventory, physical-grid scope, and read-only state
from the existing discovery export. A software claim can bind a run to exact
grid/controller IDs, but it cannot grant game ownership, authenticate as
another identity, choose a physical controller, or exclude other actuator
writers. Do not reuse `SetDroneBehaviour*` for this purpose: the built-in
helper can transfer ownership, and its autopilot actuates thrusters/gyros
rather than the target wheel interface.

The smallest sound integration is an Agent Core observation/claim record
bound to the exact Core entity, anchor grid, controller ID, run, and operation,
with explicit expiry and readback. Keep it separate from game ownership and
from the actuator write path. An Agent Core claim alone must not authorize a
write. No code change is made here because the engine path offers no
atomic, operation-owned wheel setter or matching scoped stop receipt to
integrate safely.

The current exact blocker to Blue Truck movement is the runtime direct-maneuver
broker gate: the host plugin and mod share run-bound WorldStorage
activation/heartbeat/approval, but the engine-facing interfaces provide no
shared owner-token compare-and-set for wheel overrides, actuator revision and
complete readback receipt, or operation-scoped stop receipt. The local broker
capability gate therefore revokes approval and returns before actuator writes.
The previous disposable 0.1 m/s probe is not a safe basis to bypass that gate:
it actuated and drifted away, with steering override `-1` while measured wheel
angle was `+0.261 rad`; that mapping remains unexplained. Current source also
requires a corrected build and fresh preflight evidence. The exact runtime
broker boundary and required evidence are recorded in
`docs/direct-maneuver-api-2026-10-19.md`; no live state was refreshed for this
review.

No fixture was launched, no actuator value was written, and no tests or builds
were needed because this review made no code changes.

## Cockpit designation and movement authority follow-up

The same installed `Sandbox.Game.dll` confirms that the concrete engine class
is `Sandbox.Game.GameSystems.MyGroupControlSystem`, implementing
`IMyGridControlSystem`; a concrete `MyGridControlSystem` class was not found.
`MyCubeGrid.HasMainCockpit`, `IsMainCockpit`, and `SetMainCockpit` are present
and simply query/set the grid's `MainCockpit` reference. In
`MyShipController.MainCockpitChanged`, setting the flag updates that reference.
The base `CanBeMainCockpit()` returns false; `MyCockpit` overrides it to return
`BlockDefinition.EnableShipControl`. `MyRemoteControl` has no override and is
not exposed as a main cockpit by that hook. Main-cockpit designation is not
player identity, access, or a grant of movement authority.

`MyShipController.IsMainCockpitFree()` means only that no cockpit is designated
or this block is the designated one. The public `IMyShipController.IsMainCockpit`
setter requires both `IsMainCockpitFree()` and `CanBeMainCockpit()`. Some
terminal settings and wheel-control initialization also use this restriction.
The actual `MyGroupControlSystem.UpdateControl()` chooses among registered
`IMyShipControl` blocks via `HasPriorityOver`; `MyRemoteControl.Priority` is
`AutoPilot` while autopilot is enabled and `Secondary` otherwise. It therefore
arbitrates registered controllers; the main cockpit flag gates some actions
but does not itself choose an authenticated player.

### Input and takeover paths

- **Human in a cockpit:** the controller's `ControllerInfo.Controller` is the
  engine `MyEntityController`; `IsUnderControl` is simply whether that value is
  non-null. `MyControllerInfo.ControllingIdentityId` reads
  `Controller.Player.Identity.IdentityId`. `IsLocallyControlled` instead asks
  whether the player client equals the local client, so locality is not the
  server authority test. `MyShipController.MoveAndRotate` stores the vectors;
  the scheduled no-argument method rejects a different selected group
  controller, a non-main cockpit (unless the control-transition override is
  set), missing movement systems/resources, and then requires one of
  `Sync.Players.HasExtendedControl(this, CubeGrid)`, entity replay, or a
  `Pilot` matching the character on the controlling player/controller. It also
  requires `m_enableShipControl`. Accepted vectors feed thrust, gyro torque,
  and, when enabled, `GridWheels.AngularVelocity`.
- **Human operating Remote Control:** `MyRemoteControl.CanControl` requires a
  valid prior controlled entity, autopilot disabled, a working block, no
  `PreviousControlledEntity`, and `CheckRangeAndAccess`. For a character or
  controlled terminal block, the latter checks antenna-system connectivity;
  the request event is validated with `Access | Ownership`, and the success
  callback calls `GetUserRelationToOwner` and assigns control only for a
  friendly relation. `CheckPreviousEntity` permits a character or cockpit,
  but rejects a cryo chamber and other controller types. Since `CanControl`
  rejects an enabled autopilot, human takeover must first disable it through an
  authorized path; `AcquireControl` also disables autopilot before it stores
  the previous entity and switches control. `UpdateAutopilot` returns movement
  authority only while enabled, working, and selected as the group controller.
  An enabled autopilot is not itself a human controller identity.
- **Pirate `DroneBehaviour`:** the NPC path does not create cockpit player
  control or call `MyShipController.MoveAndRotate`; it uses
  `MyAutopilotComponent.UpdateAutopilot` to set thruster AI force and gyro
  torque. `MyRemoteControl.Priority=AutoPilot` participates in controller
  arbitration. The traced code has no bridge from this behavior to
  `GridWheelSystem.AngularVelocity` or the suspensions' `Steer`/
  `UpdatePropulsion` calls.

`MyGridWheelSystem.Update()` itself has no player, identity, ownership, or
`ControllerInfo` test. It requires `ENABLE_WHEEL_CONTROLS_IN_COCKPIT`, grid
physics, and working suspensions whose `Steering`/`Propulsion` settings are
enabled; it consumes the grid system's angular input. Thus this system is a
physical consumer, not an authentication boundary. A direct server write to
that raw input is not a supported, actor-bound claim. The public
`MoveAndRotate(Vector3, Vector2, float)` method is the engine input seam, but
its scheduled application still enforces the checks above; public visibility
does not bypass them. Client `SyncMoveIndicator` is an engine event with
`Server(ValidationType.Access | ValidationType.Ownership)` validation.

### Mechanical grid scope

`MyShipController.ControlGroup` returns the physical grid group. When
`MyGroupControlSystem.AddControllerBlock` sees controllers on distinct grids,
it tests whether the controller grids share `MyCubeGridGroups.Static.Physical`
membership before replacing group control. Group controller registration and
grid membership are separate internal collections. Wheel dispatch remains
per-grid (`base.Grid.GridSystems.WheelSystem`); thrusters and gyro systems are
also looked up from the controller/Remote Control's grid. The drone's
`LoadShipGear` uses the logical group for guns/tools. These facts do not prove
that every mechanically connected grid's suspensions are automatically
controlled from the anchor grid; discovery must report its grid scope rather
than assuming it.

No supported engine API found in this trace gives a server plugin an
identity-independent, operation-owned wheel-vector injection plus matching
stop receipt. This confirms the existing direct-maneuver broker blocker. The
offline follow-up keeps the runtime dispatch gate closed even if
`VRAGECAGE_LAB_MOTION_OWNER` is set: an environment variable is not evidence of
a shared host/mod lease. The actuator adapter and C12 stop behavior remain
untouched.

## Offline identity and capability follow-up

The direct-maneuver run context, activation, heartbeat, evidence, plan, and
operation lease can carry an exact `agent_core_id`; previous host commands
required `VRAGECAGE_LAB_AGENT_CORE_ID`, which was not a trusted discovery
binding. The mod resolved that supplied ID and checked that it remained on the
supplied anchor grid, but that only validated a manually provided claim. The
in-game discovery export reports Core, grid, controller, camera, and suspension
state, but the host does not consume that export when building a run context.
Its exporter run ID is independent of the mod session ID, the file is published
every 300 simulation ticks, and there is no pre-activation Core-discovery
handshake tying it to the current run. The runtime now rejects direct-maneuver
startup rather than accepting a manually supplied Core ID. It also requires a
fresh unique `Blue Truck` grid with the Core and an unambiguous controller to
be discovered and bound before direct maneuvers can be enabled; `Codex Truck`
must not be selected as a fallback. Suspension enablement and inversion remain
read-only discovery fields.

The minimal missing handshake is a current-session discovery observation that
the host can read before activation, containing the exporter/session nonce,
capture tick/time and completeness flags, plus unique exact `Blue Truck`
physical-grid, on-grid Core, controller, and camera IDs. The host must reject
stale, truncated, duplicate-name, missing, or cross-grid candidates, then bind
those discovered IDs into immutable context and activation. Until that exists,
the direct command fails closed and no manually supplied block ID can stand in
for discovery.

The worker now reads the mod's discovery export, rejects stale, duplicate,
truncated, incomplete and cross-grid inventories, and derives the exact Core,
grid, controller, camera and suspension binding without requiring IDs on the
planning command. It records the export run ID, simulation tick and SHA-256
fingerprint in the immutable context. The host and mod activation, heartbeat
and approval codecs carry those same values; the host and mod recheck the
snapshot fingerprint, session run ID, unique `Blue Truck` name and current
same-grid entity IDs before acknowledging it.

The remaining runtime seam is lifecycle ordering. `command_run` creates and
hashes the context before starting the game service, while the exporter creates
its run ID only when the mod session starts. A context made from the saved
WorldStorage therefore belongs to the prior mod session and is rejected by the
new host/mod session. The runtime needs a two-stage, no-motion path that starts
discovery-only, waits for the same session's export, lets the worker finalize
the immutable context and discovery receipt, then lets that still-running host
consume the context and write activation. The current worker has no mid-session
context update step. Until that seam exists, preflight cannot receive a matching
discovery heartbeat; direct-maneuver startup also remains explicitly disabled,
and actuator dispatch remains closed.

The plugin-local broker can serialize the participating server-plugin writer
and the executor samples every channel and brake state before advancing the
lease. It cannot audit arbitrary external plugin/mod writes or observe a
write-and-restore between samples; the public controller surface also does not
expose the engine's selected group-controller priority after takeover. Those
gaps prevent reliable external-writer exclusion and complete takeover
detection. Dispatch therefore remains fail-closed: the runtime supplies no
server-authoritative lease attestation and no stable-priority attestation. The
operation-bound C12 receipt and stationary-confirmation code was not changed.
