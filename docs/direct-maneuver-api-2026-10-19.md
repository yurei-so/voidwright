# Direct maneuver API groundwork — 2026-10-19

## Scope

`DirectManeuverPlan` defines a planner-independent sequence of forward/reverse
distance, heading correction, wheel-angle, stop, and stationary-hold segments.
The direct contract caps forward distance at 25 m and 0.8 m/s, reverse at 3 m
and 0.5 m/s, wheel angle at 40 degrees, heading correction at 90 degrees, and
the complete operation at 7,200 ticks and 12 segments. Stop and hold segments
require zero commanded speed.

Before dispatch, the policy requires exact run, fixture, operation, grid,
controller, and same-grid sensor bindings; fresh passing host and mod
preflights; a fresh clear sensor scan with at least 3.4 m clearance; Voidwright
actuator ownership; and known, available brake control. These checks fail
closed. `DirectManeuverTelemetry` records commanded and observed steering signs,
wheel response, commanded and actual heading, displacement, speed, sensor
state, and terminal outcome separately. An accepted command is not a completed
maneuver. Cancellation, emergency stop, and normal completion request an owned
stop and stay nonterminal until three distinct telemetry samples confirm speed
at or below 0.05 m/s and displacement at or below 0.02 m per sample.

VRageCage now validates and queues the bounded `direct-maneuver` command,
exports its exact grid and compact segment plan into the immutable preflight
context, and hands that binding to the server plugin and ordinary Voidwright
mod. The offline executor and lifecycle policy cover command proposals,
readback acceptance, cancellation, emergency stop, and terminal stationary
confirmation. Runtime dispatch remains blocked: the host rejects this action
before publishing mod approval or entering its generic drive handler, and the
mod rejects it before actuator dispatch. The host guard also prevents an
unset motion-owner setting from falling through to the legacy drive-distance
path.

## Current fixture evidence and stop condition

The latest saved evidence in
`artifacts/land-v4-brake-ownership-diagnostic-20261019/candidate-g/` is an
older diagnostic snapshot, not a current preflight authorization. Its host
preflight is 6/10 and the mod preflight is 9/10. The mod denied dispatch because
all four wheels were braked and brake ownership was unavailable through its
current API. The recorded terminal outcome is `preflight-blocked` with
`actuatorDispatch=none` and zero fixture edits. The timestamps are
2026-10-19 06:33 UTC, so they cannot authorize a new run.

No fixture was launched or moved in this turn. Fresh disposable V4 clones,
staged-artifact readback, and exact host and mod preflight PASS receipts are
still required before any engine skill step. Unresolved brake ownership remains
a hard stop; no brake override or safety-limit change is allowed.

## Verification

- `dotnet run --project tests/Voidwright.CoreTests/Voidwright.CoreTests.csproj` — 55/55 checks passed, including bounded composition, stale/cross-grid/failed gates, brake-ownership denial, separated steering/terminal telemetry, and stationary-confirmed cancellation/emergency stop.
- `dotnet build Voidwright.csproj -c Release -p:SpaceEngineersBin64=<read-only Mod SDK Bin64_Profile>` — succeeded with 0 warnings and 0 errors.
- `dotnet build tools/PluginCompileCheck/PluginCompileCheck.csproj -c Release -p:SpaceEngineersBin64=<read-only Mod SDK Bin64_Profile>` — succeeded with 0 warnings and 0 errors.
- `python3 worker/tests/test-lab-contract-preflight.py` — 7/7 contract tests passed, including exact grid/camera binding, segment bounds, and immutable pending-command creation.
- `dotnet build AgentMcp/Voidwright.AgentMcp.csproj -c Release` and `dotnet build tests/Voidwright.AgentMcpTests/Voidwright.AgentMcpTests.csproj -c Release` — both succeeded with 0 warnings and 0 errors. The Agent MCP test runner printed all 30 checks passed, then remained open after its completion summary; it was interrupted after confirming its local child processes exited. No live fixture was involved.
- Engine movement and staged-artifact verification were not run. No fresh disposable copy was made because the command is blocked in preflight and no movement test is authorized by the current evidence.

## Offline executor and ownership boundary — 2026-10-19 follow-up

`Data/Scripts/Voidwright/Core/DirectManeuverExecutor.cs` now implements the
offline bounded command policy. It issues low forward/reverse propulsion and
steering proposals, binds every sample to the exact run, fixture, operation,
grid, controller, and same-grid sensor, and requires fresh clear scans. Each
proposal awaits explicit adapter acceptance. Acceptance, observed physical
movement, and completion confirmation remain separate fields. Distance and
heading/angle segments request a stop before advancing; completion, cancellation,
emergency stop, and timeout require an accepted stop plus three distinct fresh
stationary samples. Simulation tick deadlines also apply while command
acceptance is pending.

`DirectManeuverOperationLease` requires fresh passing host and mod preflight,
matching run/fixture/operation/grid/controller identity, a fresh neutral wheel
override snapshot, and known inactive wheel, grid-brake, and handbrake state.
Its injected in-process authority allows one unexpired operation per grid;
heartbeats compare fresh observed values, and expiry or external changes revoke
dispatch. The executor binds each command acknowledgement to its command ID
and exact identity, and accepts it only after every leased wheel steering and
propulsion channel has an accepted readback for that command. A partial write
therefore requests stop and cannot be mistaken for command acceptance.

Cancellation and emergency-stop requests carry the exact operation identity and
a request ID. Their acceptance is separate from the controlled-stop command
acknowledgement; the terminal receipt carries that same request ID and becomes
stationary-confirmed only after three distinct fresh samples. If stop acceptance
or confirmation times out, the receipt reports failure with
`StationaryConfirmed=false`. Cleanup proposes restoration only for channels
with accepted writes whose current value still equals that operation's last
accepted value; it carries expected and original values and never touches
external changes.

This is an offline policy contract, not production exclusivity. The registry is
process-local and ModAPI has no atomic compare-and-set or owner token, so
separate mod/plugin processes can still race between observing neutral values
and writing. The contract is covered by adversarial offline tests; no game
adapter currently produces a shared lease or safe-stop proof.

Installed ModAPI evidence was inspected in `Sandbox.Common.xml` and
`Sandbox.Game.dll`. `IMyMotorSuspension.SteeringOverride` and
`PropulsionOverride` are shared settable proportions (`-1..1`); decompiled
setters update synchronized override fields, with steering sanitizing NaN to
zero. There is no ownership token, compare-and-set, write revision, or
per-operation acceptance receipt. The explicit interface implementation of
`IMyMotorSuspension.Brake` maps to `BrakingEnabled`, a configuration setting;
the host's separate internal `MyMotorSuspension.Brake` is an active brake
signal. `IMyShipController.HandBrake` is get/set shared controller state. The
plugin's `GridWheels.Brake`/`HandBrake` are shared controls, and its legacy
neutralizer changes them. VRageCage's process-level stop is not maneuver-scoped
and has no actuator-stop receipt tied to operation telemetry.

For general runtime use, direct-maneuver dispatch remains blocked. The narrowest
candidate broker is a single server-plugin writer for the direct operation,
with the ordinary mod providing fresh preflight/telemetry and issuing no
actuator writes. Every participating Voidwright/VRageCage writer path must
either route through that broker or be disabled during its lease. Within that
cooperative writer set, the broker can serialize writes, bind opaque owner
tokens to exact run/fixture/operation/grid/controller identity, read back all
channels, and issue operation-bound stop and cleanup receipts. The game
property setters still cannot enforce atomic compare-and-set against arbitrary
external mods or human/game writes; a local lease must not be described as
global exclusive ownership.

A short bounded test could be considered under an explicit disposable-fixture
invariant that audits and disables all competing agent/plugin/script/autopilot
writers and excludes human control. That would validate behavior under the
audited environment, with external changes monitored and treated as failure;
it would not validate or claim global exclusion. A separate exact-fixture
movement authorization and the preconditions in
[direct-maneuver-broker-design-2026-10-19.md](direct-maneuver-broker-design-2026-10-19.md)
are still required. No such test is authorized by this assessment, and the
runtime remains dispatch-blocked.

The latest candidate-p preflight result remains preflight-only (10/10 checks);
it did not dispatch movement. No fixture was launched or moved for this
follow-up. The executor tests cover forward, reverse, steering, explicit stop,
forward/reverse/forward sequencing, cancellation, emergency stop, normal and
cancellation timeout, pending/mismatched command acceptance, stale sensor
evidence, identity mismatch, competing leases, stale leases, partial writes,
external channel/brake changes, and owned-value-only cleanup.

Follow-up verification before the broker gate was added: the core suite passed 65/65 checks, the game mod built
with 0 warnings and 0 errors, and the server plugin compile check built with 0
warnings and 0 errors. No engine movement, deployment, commit, or push was run.

## Runtime broker boundary and decision request — 2026-10-19

The server plugin and ordinary mod currently communicate through run-bound
WorldStorage activation, heartbeat, and approval files. These establish fresh
attestation but are not an atomic lease primitive. The compiled plugin/mod
interfaces provide no shared owner-token compare-and-set for wheel overrides,
no actuator revision/readback receipt, and no operation-scoped stop receipt.
The broker capability gate requires shared authority, fresh host and mod
attestations, operator-free control, owner-bound writes, complete channel
readback, and scoped stop. The runtime supplies no such capability and blocks
the command before actuator writes or mod approval.

Remaining external-writer races are concrete: another mod or human input can
write between a channel read and setter call; it can also change the channel
between ownership comparison and cleanup setter. A per-process lock cannot
serialize other writers. A neutralization attempt through `GridWheels` or
shared brake properties is likewise not operation-scoped and could override
another owner. There is no safe partial broker to expose as production
authorization under the current interfaces.

**Architecture decision requested:** provide one server-authoritative broker
boundary used by both plugin and mod, with an enforceable actuator write
funnel. It must atomically claim the exact run/fixture/operation/grid/
controller, bind fresh host and mod attestations and operator state, mediate
every wheel-channel write with owner-token plus revision compare-and-set and
complete readback receipts, and provide operation-scoped cancel/stop receipts
tied to fresh stationary telemetry. Define whether unmediated third-party or
human writes can be excluded; if not, the broker must revoke on every observed
change and dispatch remains disabled. Keep direct-maneuver dispatch and
cleanup writes disabled until these semantics exist and are verified.

The narrower cooperative-writer design and isolated-fixture assessment are
recorded in [direct-maneuver-broker-design-2026-10-19.md](direct-maneuver-broker-design-2026-10-19.md).
That assessment finds a bounded test conditionally supportable only when the
fixture's participating writer set is explicitly audited and competing agent,
plugin, script, autopilot, and human writers are excluded. It does not claim
engine-enforced exclusion of arbitrary external writers and does not authorize
or enable such a test.

The current runtime guard also revokes any host approval, records
`preflight-blocked`, and returns before the mod-owned branch or legacy generic
drive handler. It intentionally performs no actuator cleanup write.
Post-guard verification: core suite 66/66; mod build and plugin compile check
both succeeded with 0 warnings and 0 errors; `git diff --check` passed.
