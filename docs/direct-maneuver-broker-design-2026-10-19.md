# Direct-maneuver broker design — 2026-10-19

## Decision

The narrowest viable funnel is a **single server-plugin writer for a direct
maneuver**, with the ordinary Voidwright mod providing fresh read-only
preflight/telemetry and refusing all actuator writes for that operation. The
plugin already resolves the exact controller and grid and can observe the
host-side wheel and active brake state. Its `direct-maneuver` command now
waits for the host-minted operation token to return in a fresh mod heartbeat
before actuator dispatch. The broker lives in
`ServerPlugin/DirectManeuverActuatorBroker.cs`; the token travels through a
short-lived run-bound WorldStorage artifact and returns in the existing mod
heartbeat. Magnetar's `ProhibitedMemberRule` rejects `AppDomain` calls from the
mod script compiler, so the mod does not use an in-process callback.

The broker would own one lease record per grid, keyed by run, fixture,
operation, grid, controller, and wheel/channel set. It would be advanced from
the server update path, mint an opaque per-operation token, and serialize all
**participating Voidwright/VRageCage** proposals, writes, readbacks, stop
requests, and cleanup. The host would write every steering/propulsion channel
through that broker; the mod would remain passive for the leased operation.
Any other in-repository plugin/mod setter path that can run during that
operation must be routed through the same broker or made unavailable while
the lease is active. The codebase currently has several legacy host write and
neutralization paths, so a dedicated direct-command branch alone does not
establish a complete repository-wide funnel.

For each command the broker would require a fresh exact host and mod
attestation, operator-free controller, known inactive initial brake state,
neutral complete wheel/channel snapshot, and a live matching token. It would
record before/write/readback values for each channel, accept only complete
channel readback, and bind cancellation/emergency-stop requests and receipts to
the operation and request ID. Stop acceptance would remain distinct from
stationary confirmation. Cleanup would restore or clear only values still
matching the broker's last accepted value and would leave changed channels
untouched.

## Cooperative ownership versus external writers

This funnel could serialize participating Voidwright/VRageCage writers because
one server update path would own their writes and the other in-repository paths
could be routed through it. It cannot provide engine-enforced exclusive
ownership against arbitrary other mods, plugins, or game/human input. The
installed setters have no owner token, revision, or compare-and-set. Reads
followed by setters and cleanup checks followed by setters retain a race.

The contract must therefore say **cooperative ownership among participating
writers**, not global exclusive ownership. The broker can monitor exact wheel
values and controller state each update, revoke future commands when it sees a
change, preserve changed channels during cleanup, and report the detection.
That is detection and mitigation after observation; it cannot prove a writer
did not interleave between a check and write or guarantee an external writer
will not undo a stop.

The current `AuditDirectManeuverWriters` scan inspects only blocks returned by
`target.CubeGrid.GetBlocks()`: running PBs, flight-movement autopilot state,
active movement AI, and controlled ship seats. It does not prove timers or
event controllers cannot trigger writers, does not walk mechanically attached
grids, and cannot enumerate other mods/plugins or observe an interleaving
setter race. Its result is named `LocalParticipatingWriterAuditPassed`; it is
not evidence that no competing writers exist. The evidence fields
`ParticipatingWriterPathAvailable` and `ControlledStopPathAvailable` describe
available paths, not verified actuator ownership or a guaranteed physical
stop. Runtime movement additionally requires a separately verified lease
authority and terminal stationary evidence.

## Shared host/mod protocol boundary

The existing host/mod channel is `MyAPIGateway.Utilities` WorldStorage
read/write, with the plugin and mod resolving the same `VRage.Game` storage
scope in the inspected runtime. It can carry immutable run-bound requests,
fresh attestations, acknowledgements, and durable evidence. It does not expose
an atomic claim, compare-and-set, or revocation operation. Replacing or
deleting a lease file therefore cannot fence a mod that already read the old
contents, and an expiry checked before an actuator setter still leaves a
check-to-write interval.

The available actuator APIs have the same limit: mod-facing
`IMyMotorSuspension.SteeringOverride` / `PropulsionOverride` and
`IMyShipController.HandBrake` are setters without an owner token, revision, or
expected-value compare-and-set. Host `GridWheels` writes and existing legacy
neutralizers are additional setters outside the in-memory direct-maneuver
broker. A serialized JSON lease and operation-bound stop receipt can improve
correlation and auditability, but cannot enforce exclusive ownership or
linearize a stop against those setters.

The run-bound broker record, WorldStorage token artifact, heartbeat echo,
per-channel readback, stop-only capability, and stationary receipt form a
cooperative protocol for participating direct-maneuver writers. WorldStorage
availability is only transport; it does not provide an independently
authoritative lease arbiter. The production dispatch gate therefore remains
closed until such an authority is implemented and independently verified. A token cannot make an engine
setter atomic against a nonparticipating writer. The runtime gate therefore
also requires fresh controller and external-writer checks, and its behavior
still needs live validation before any deployment.

## Runtime topology and setter inventory

The installed dedicated-server runtime loads the server plugin through
`MySandboxGame.RegisterAssemblies` / `VRage.Plugins.MyPlugins.Load`. Its
`MySandboxGame.Update` calls `MySession.Static.Update` and then each plugin's
`IPlugin.Update` synchronously on the game update thread. Voidwright's
`VoidwrightSession.UpdateBeforeSimulation` runs inside that session update.
The mod publishes its run-bound heartbeat during the session update; the
plugin reads it afterward. The plugin writes a short-lived, host-minted token
artifact to the same `Saves/<Session>/Storage/VRage.Game` WorldStorage scope,
which the mod reads on its next heartbeat. Before every participating host
wheel or brake setter, the plugin rereads the heartbeat and capability
artifact, validates the exact run, fixture, challenge, session, operation,
discovery fingerprint, grid, controller, Core, camera, and token, then checks
controller occupancy and priority. Missing, stale, mismatched, expired, or
unreadable evidence blocks the setter; stop-only writes use their separate
broker capability and never require the movement token. The WorldStorage API
does not provide compare-and-set, so this remains cooperative writer
serialization rather than engine-enforced global exclusion.

Setter audit for this checkout:

* The mod's only wheel override setter sites are in
  `VoidwrightLabPreflight.ApplyOneBoundedStep` and `StopActuators`. The direct
  maneuver branch of `ApplyOneBoundedStep` returns before movement; cleanup is
  a no-op unless the ordinary mod adapter already owns actuators. A fresh
  direct-maneuver operation therefore does not write from the mod. Other
  mod-owned depth-navigation operations still use those setter sites directly.
* Direct-maneuver dispatch in `Plugin.UpdateDirectManeuver` calls
  `DispatchDirectManeuverStep`. Its normal wheel and controlled-stop writes
  run in `DirectManeuverActuatorBroker.TryRunParticipatingTransaction`, with
  exact operation identity and per-channel readback.
* `EmergencyStopDirectManeuver` now routes every wheel neutralization and both
  grid-brake writes through `TryWriteStopChannel`. The broker mints a separate
  stop-only capability bound to run, fixture, operation, grid, controller, and
  request ID. It remains usable after movement-lease expiry, permits only
  neutral wheel values or active brakes, checks the previously accepted value
  before invoking the setter, and records immediate readback under the broker
  lock. The lease remains reserved until neutral readback, active brakes, and
  stationary evidence produce a successful stop receipt. A failed setter,
  failed readback, identity mismatch, or externally changed channel leaves the
  stop unresolved and keeps dispatch closed.
* Remaining setters in `Plugin.cs` belong to legacy route, drive, overspeed,
  cancellation, restore, and generic-navigation paths. Command routing returns
  before those paths for `direct-maneuver`, so they are not concurrent callers
  from this plugin instance during that command. They remain independent
  writers for other command kinds and would need routing or exclusion before
  a repository-wide contract could cover all Voidwright operations.

The direct-maneuver mod path reads the short-lived
`voidwright-operation-capability-v1.txt` artifact from WorldStorage and copies
its token into the existing `VWPF5` heartbeat. The artifact binds run, fixture,
operation, context hash, challenge, session, grid, controller, Core, camera,
discovery run/tick/fingerprint, token, issue time, and expiry. The plugin polls
the heartbeat after the session update, verifies its freshness and exact
binding, then rereads both heartbeat and artifact immediately before each
participating setter. The mod never uses `AppDomain`; the worker log showed
that the prohibited-member rule rejects those calls during mod compilation.
The plugin revokes the movement artifact before requesting the separate
stop-only capability, so a stale movement echo cannot authorize stop writes.

The mod itself still refuses actuator writes for direct-maneuver; the plugin is
the only participating movement writer for that operation. Ordinary mod-owned
depth-navigation writes and legacy plugin route/drive setters remain separate
command paths and are not covered by this direct-maneuver capability. Routing
those operations through one host authority would require a separate callback
contract; these tests do not cover them. Third-party/game writers still cannot
be forced through any Voidwright callback, so this remains cooperative
authority rather than global exclusion.

The installed `MyShipController` exposes the selected main cockpit through
`CubeGrid.MainCockpit` / `IsMainCockpit`, current control through
`IsUnderControl` and `ControllerInfo.ControllingIdentityId`, and
`ControllerInfo.ControlAcquired` / `ControlReleased` events. The plugin polls
main-cockpit and occupant state before grant and before each setter, binds both
events on each controller in the vehicle grid, and permanently revokes
movement for that operation on either transition. Direct-plugin teardown also
uses this stop path instead of the generic legacy neutralizer. An unknown controller,
missing event binding, selected-cockpit change, or occupied seat fails closed.

The movement gate now requires successful bridge registration, exact fresh
host/mod evidence, controller takeover stability, the host-issued token echoed
by the mod, complete channel readback, and an operation-scoped stop path. The
stop-only funnel remains separate: it cannot authorize a new operation or any
non-neutral movement. The bridge and engine API have only been validated by
offline protocol tests and compilation; no live callback or movement was
started. The engine still cannot fence unrelated writers.

## Isolated disposable-fixture test boundary

**Assessment: yes, conditionally.** A bounded test could be considered under a
narrower, explicit environment invariant even though arbitrary external writers are not excluded by the API:
the disposable world has an audited allowlist containing only the required
Voidwright/VRageCage components; competing navigation/agent plugins, scripts,
programmable blocks, autopilot, and other actuator integrations are disabled;
no human is in control; and the broker is the only enabled participating
writer. The test would validate behavior under those stated conditions. It
would not validate atomic exclusion or safety against arbitrary external
writers.

The invariant can be supported by recording the copied world's mod/plugin
allowlist and hashes, checking its programmable-block/script inventory,
checking the controller autopilot and `IsUnderControl` state at each preflight
and command boundary, auditing every Voidwright/VRageCage actuator setter, and
monitoring all wheel channels throughout the short test. The current engine
API cannot independently prove that an unlisted or dynamically loaded server
component never writes a wheel property, nor eliminate the read/write race.
An unexplained value change is a lease loss and a failed test; it is not
evidence that the broker prevented the competing write.

## Conditions before considering a separately authorized test

1. Preserve the package correction from candidate-p. Candidate-o failed the
   Magnetar compile because its staged plugin omitted
   `DepthAvoidancePolicy.cs`; candidate-p included it and passed a preflight-only
   runtime run. Before any new fixture, rebuild and verify the exact staged
   plugin/mod hashes and runtime load markers. Candidate-p proves preflight
   lifecycle only; it is not movement evidence or authorization.
2. Implement and offline-test the single-writer funnel, owner-bound receipts,
   operation-scoped stop lifecycle, complete per-channel readback, and safe
   cleanup. Inventory and route or disable every participating legacy writer.
3. Create a fresh disposable V4 clone with an explicit component allowlist;
   disable scripts, programmable-block automations, autopilot, other agent
   writers, and human control. Preserve the baseline and prove zero fixture
   edits before dispatch.
4. Obtain fresh run/fixture/grid/controller-bound host and mod preflight,
   known inactive active-brake state, neutral complete channel state, and
   confirmed safe stopping behavior. The ModAPI `Brake` property is not the
   live brake signal.
5. Bound the proposed maneuver to a short low-speed path in a clear, level
   area, with a tested cancellation/stop path and a fresh stationary terminal
   receipt. Abort and preserve changed values if external changes appear.
6. Get separate explicit user authorization for that exact disposable fixture,
   target vehicle, bound, and stop plan. No live test is authorized by this
   design note.

## Validation status

Offline lease/executor policy tests cover identity binding, stale/competing
claims, partial writes, external value changes, accepted per-channel readback,
cleanup preservation, cancellation/stop receipts, and stationary confirmation.
The broker capability gate rejects dispatch unless shared authority, fresh
host/mod attestations, operator-free state, owner-bound writes, complete
readback, and operation-scoped stop are all present. The broker contract test
covers participating-writer serialization, unknown external-writer state,
capability rejection, lease expiry, generic-drive route rejection, and an
identity-bound stop-timeout failure. The stop-only contract test covers expired
movement authorization, wrong run/grid/controller/operation rejection,
unregistered-writer rejection, neutral-only writes, setter/readback failure,
stale-capability rejection after takeover, and the stationary proof requirement.
Capability-artifact tests cover exact WorldStorage round trip, wrong identity,
expiry, movement-token revocation, and new-operation takeover. Controller policy
tests cover missing main-cockpit state, player acquisition/release, and latched
control-transition events. Fake setters validate ordering and readback; they
do not validate live game-engine ownership. The writer-state enum is a
test/contract attestation, not proof that arbitrary external writers are
absent. The suite passed 86/86; plugin and mod builds passed. No fixture,
client, movement, deployment, or runtime dispatch was started.
