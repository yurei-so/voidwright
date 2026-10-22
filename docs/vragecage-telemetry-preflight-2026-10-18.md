# VRageCage telemetry preflight and ordinary Voidwright adapter

## Scope and safety

This local implementation adds a launcher-bound preflight and an ordinary
Voidwright `IMyMotorSuspension` adapter for the disposable VRageCage
`drive-to-coordinate-depth` action. It does not start a fixture or server,
move a vehicle, modify an original save, deploy, commit, or push. Existing
working-tree changes were preserved.

The launcher creates a fresh challenge for each process start and passes a
hash-bound context under that run's quarantine directory. The context pins the
run, fixture, operation, action, command, source artifacts, fixture-sector
snapshot, and staged Mods tree. The ordinary mod rechecks bound contract,
receipt, command, and staged Mods artifacts periodically, resolves the exact controller and camera,
and requires a fresh world-storage heartbeat readback bound to the current
session, challenge, run, grid, and camera. It samples a 19-ray fan every ten
simulation ticks; full context/tree validation is scheduled every 60 ticks.
The fixture-sector hash is an activation-time snapshot and is not rehashed
during simulation because the game may save the world while running. It does
not detect an out-of-band sector-file edit after activation. Every actuator dispatch goes through the shared fail-closed
`MovementCommandGate`.

The mod adapter uses bounded `IMyMotorSuspension` overrides and does not call
Magnetar's `GridWheels` movement path. It refuses a player-controlled remote,
non-neutral external steering or propulsion, a pre-existing brake input, missing or stale sensor evidence,
invalid command limits, blocked/unknown headings, and failed preflight checks.
It bounds commanded speed to 0.8 m/s and lowers propulsion while detouring.
Owned wheel properties are stopped on arrival, timeout, invalidation, or
takeover only while they still equal the adapter's last attempted values;
changed or non-finite values are left alone. A failed stop write is retained
for retry during shutdown. These are implementation properties, not proof of
in-game behavior.

At each movement boundary the sensor check now compares the current controller
and camera grids, the run-pinned camera ID, and the grid/camera IDs captured by
the scan. It also requires all 19 rays and both the 20-tick and one-second age
limits. The fresh-session heartbeat remains an exact serialized world-storage
readback bound to the current run, fixture, operation, challenge, mod session,
grid, and camera.

## Current preflight checks

| Check | Evidence source | Local implementation |
| --- | --- | --- |
| Fixture/source integrity | Run context hashes and staged Mods tree | Revalidates immutable source artifacts and the staged Mods tree; world-sector digest is pinned to the activated run snapshot. |
| Runtime connectivity | Authoritative server session | Requires server/dedicated session status. |
| Vehicle identity | Exact controller and camera entity IDs | Requires both entities to resolve on the same grid. |
| Voidwright module/session | Fresh challenge heartbeat via world storage | Writes, reads back, and validates session, challenge, run, grid, camera, and age. |
| Controller readiness | Remote control state and grid physics | Requires functional controller, physics, and no player input. |
| Actuator availability | Ordinary suspension blocks | Requires steerable and propulsive wheel channels with neutral/owned overrides. |
| Sensor freshness | Same-grid camera ray fan | Requires all 19 rays from a recent scan, with current and scanned grid/camera IDs matching the run-pinned camera; bounded to 8 m. |
| Position/speed | Current remote control sample | Requires finite coordinates and speed. |
| Safety configuration | Bound command values | Enforces speed, timeout, arrival-radius, and operator-state bounds. |
| Movement interface | Ordinary mod code path | Uses only `IMyMotorSuspension`; Magnetar movement is not called for this action. |

## What remains unverified

No V4 engine run has occurred in this implementation pass. Therefore there is
no client-confirmed evidence that the actual wall is detected correctly, the
19-ray fan covers the needed route, the destination lies in the observed
heading fan, steering polarity and wheel overrides move the rover as intended,
or the complete Ramp Bottom to Green Zone route finishes without collision.
The clearance model also treats ray results as an obstacle fan, not a full
vehicle footprint or swept-volume collision proof. A compile or synthetic
test cannot establish those physical properties.

The launcher generates a fresh 128-bit lowercase-hex challenge for each start;
the mod reuses that run nonce for its periodic heartbeat and rejects markers
whose run, challenge, or other identities differ. This is replay protection
between runs, not a one-use token or a cryptographic boundary against a hostile
local process sharing the same account. World-storage write/readback validates
the game-facing marker, not protection from an administrator who can modify the
disposable fixture.

## Validation

- Voidwright core tests: **45/45 passed**, including no-dispatch on failed
  preflight, an exception-triggered emergency-stop callback, run/context/
  action binding, tamper checks for each bound source artifact, heartbeat
  challenge freshness/replay rejection, same-grid/pinned-camera scan checks,
  and external wheel-change preservation during stop.
- Obstacle simulation: **5/5 synthetic scenarios passed** (detour, no-path,
  blocked sensor corridor, target out of view, malformed sensor scan). Both
  additional depth-policy probes reached their modeled goal. The isolated
  report and probe JSON files are preserved in
  [`artifacts/vragecage-obstacle-sim-local-20261018/`](../artifacts/vragecage-obstacle-sim-local-20261018/).
- Space Engineers mod: **build passed**, 0 warnings / 0 errors against the
  installed SDK.
- Server-plugin compile check: **passed**, 0 warnings / 0 errors.
- VRageCage worker suite: **passed**: 4 CLI-wait tests; 2 mocked-launcher
  preflight tests; 2 fixture-sector snapshot tests; 23 lab-trial tests; Python
  worker script checks; and HoloView periodic and synchronized multiview checks.
  The launcher tests mock process calls and the fixture tests use temporary
  synthetic directories; they do not invoke `systemd-run` or start a game.
- No engine fixture or server was started; no vehicle moved.

Read-only provenance reconciliation confirmed that the preserved
`/tmp/voidwright-test-suite-world-v4-source-20261018` matches the historical
source-copy receipt exactly: `9ae4286ebb6fad94516612cfb083c347ccb197c62276d5658a49d65ede373f8f`
(21 files / 6,447,291 bytes). The earlier `f6e2...` discrepancy came from my
verification script adding an extra trailing NUL after each file; the original
collector hashes `relative-path + NUL + file-bytes` with no trailing delimiter.
The complete current per-file manifest and reconciliation are in
[`artifacts/v4-source-provenance-20261018/`](../artifacts/v4-source-provenance-20261018/).
The current source world has five published Workshop mods and no Voidwright
entry in either `Sandbox.sbc` or `Sandbox_config.sbc`; the VRageCage depth-run
preflight therefore requires staging and registering the local Voidwright
package on a disposable copy before any engine test.

These checks validate code and contract behavior only; they do not count as an
engine traversal.

### 2026-10-18 offline revalidation

After moving activation prerequisites ahead of run creation and queued-command
consumption, the Voidwright core suite passed **45/45** and the full
`vragecage/worker/tests/test-scripts.sh` suite passed, including the new test
that a depth command is left queued and the active-run/quarantine state remains
unchanged when the exact local Voidwright mod is not staged. Python syntax
checks and `git diff --check` also passed. No engine fixture or vehicle was
started.

The earlier revalidation did not have `SpaceEngineersBin64` configured. A
later read-only inspection found the installed SDK at
`/media/alu52/GSSD/SteamLibrary/steamapps/common/SpaceEngineersModSDK/Bin64_Profile`;
the latest source state was rebuilt against that SDK below.

| Requested case | Result | Offline evidence |
| --- | --- | --- |
| Replay / stale heartbeat marker | **PASS** | `PreflightHeartbeatBindsFreshSensor` rejects an old serialized response when the exact expected response has changed, rejects expired data, and rejects replay under another run. |
| Malformed or absent marker/challenge | **PASS** | The same test rejects null, empty, malformed marker, malformed challenge, and altered-but-well-formed serialized telemetry. |
| Run, fixture, operation, context, session, controller, grid, camera binding | **PASS** | `PreflightHeartbeatBindsFreshSensor` rejects each identity mismatch, including controller grid and sensor grid. |
| Current same-grid sensor scan | **PASS** | `SensorPreflightBindsCurrentGridAndCamera` accepts a fresh 19-ray scan only for the current controller grid and pinned camera; cross-grid, replacement-camera, partial, stale, future, and unknown-grid evidence is rejected. |
| Ordinary actuator availability / failed preflight | **PASS** | `ActuatorFailureDoesNotDispatch` verifies missing actuator availability prevents movement callback dispatch; `WheelStopPreservesExternalChanges` verifies emergency stop preserves externally changed wheel channels. |
| Worker activation and fixture-snapshot safeguards | **PASS** | Full VRageCage worker suite passes; the missing-local-mod depth activation test verifies no queued command, active record, or quarantine entry is consumed/created. |
| Core test build | **PASS** | `dotnet run --project tests/Voidwright.CoreTests`: 45/45 checks. |
| VRageCage worker suite | **PASS** | `bash worker/tests/test-scripts.sh`: all included Python and shell checks pass. |
| Space Engineers mod build at current source state | **PASS** | `dotnet build Voidwright.csproj --no-restore` with the installed SDK path: 0 warnings, 0 errors. |
| Server plugin compile at current source state | **PASS** | `dotnet build tools/PluginCompileCheck/PluginCompileCheck.csproj --no-restore` with the installed SDK path: 0 warnings, 0 errors. |
| Workstation Magnetar engine-validation substrate | **NOT TESTABLE** | The workstation's default worker root `/home/alu52/.local/share/vragecage-worker` is absent. |
| `mpai` Magnetar engine-validation substrate | **READY (read-only check)** | `vragecage --local doctor --engine magnetar` reports all seven checks true: private .NET, Magnetar, dedicated server, Steam layout, both Steamworks libraries, and worker root. |
| Existing pristine disposable V4 candidate on `mpai` | **ELIGIBLE, NOT STAGED** | `test-suite-world-v4-land-preflight-20261018-a` has a current world digest equal to the verified source receipt; inactive service; no active run, pending command, fixture edit ledger, staged mod/plugin, or quarantine entries. |
| Real engine sensor geometry, ordinary suspension response, collision-free route | **NOT TESTABLE** | No V4 fixture was started and no in-game traversal occurred. |

Final read-only integrity check rehashed the preserved V4 source copy against
its per-file manifest: **21 files / 6,447,291 bytes**, aggregate
`9ae4286ebb6fad94516612cfb083c347ccb197c62276d5658a49d65ede373f8f`, matching
the recorded receipt. The offline obstacle simulator was rerun after the
latest source changes and passed **5/5** synthetic scenarios. The installed SDK
uses the `Bin64_Profile` directory; a directory literally named
`SpaceEngineersBin64` is absent. The two successful builds passed that
`Bin64_Profile` path through the MSBuild `SpaceEngineersBin64` property. The
read-only process check found no Space Engineers dedicated-server or client
process. No fixture or game process was started during this validation pass.

The workstation itself lacks the Magnetar worker, but the previously
authorized `mpai` test device contains the complete worker substrate. Its
read-only doctor passed, and
`/home/alu52/.local/share/vragecage-worker/instances/test-suite-world-v4-land-preflight-20261018-a`
is an eligible pristine disposable copy: the per-file rehash reports the exact
source receipt digest, **21 files / 6,447,291 bytes**; its service is inactive;
no active-run or pending-command record, edit ledger, staged packages, or
quarantine entries are present. Read-only fixture inspection confirms the
`Codex Truck` controller `137709089579453168` and front camera
`74011513536052165` are on the same non-static grid. No files were staged and
no service or game process was started on `mpai`.

## Next evidence needed

The workstation's Mod SDK supports compile checks only. The `mpai` worker is
ready, and one pristine disposable copy is eligible for the bounded engine
diagnostic. The current read-only inspection stopped before staging or launch.
The diagnostic must still establish module load, fresh heartbeat, observed
19-ray geometry, bounded wheel response, and actual measured displacement
before a later full route can be considered. Preserve telemetry and distinguish
every observed result from inference. Do not call the milestone complete until
in-game route evidence exists.
