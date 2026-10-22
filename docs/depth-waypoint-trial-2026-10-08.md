# Depth-guided waypoint arrival trial — 2026-10-08

## Result

The Blue Truck autonomously reached a fixed waypoint in the open V3 gravity
fixture using the front-camera depth controller. The final center-of-mass (COM)
position was **0.499391 m** from the exact world-space waypoint, inside the
configured **0.5 m world-space arrival radius**. The controller reported
`status=complete`, a COM-displacement speed of **0.009 m/s**, and the VrageCage
terminal receipt passed. This is a narrowly passing margin (0.000609 m), not a
claim of sub-centimeter arrival precision.

No manual actuation or RGB takeover occurred. The front depth fan remained
clear to its 8 m range with zero self-grid hits. The vehicle used its four
wheel actuators under the existing 0.15 wheel-power cap and 0.5 m/s command
limit. No safety limit was bypassed.

## Measurement finding

The command target was fixed in world coordinates at
`[7821.479994, 869.924461, 9204.985096]`. The moving body was Blue Truck grid
`80250867127967277`; the reference point was its physics COM. Grid-origin,
grid-position-component, and physics-world-origin transforms agreed, and the
target and COM were compared in the same world frame. The depth camera and
controller poses were logged independently. The original speed discrepancy
was therefore **not explained by mixing a wheel-center point with a grid-origin
velocity or by a target-frame error**.

Motion telemetry records actual session elapsed time, tick interval, world
position/orientation, physics linear and angular velocity, point velocity,
COM displacement, and fixed-target distance. Samples were taken every 10
ticks (normally 0.160 simulation seconds); depth-mode control speed is the COM
displacement over a full 20-tick propulsion cycle (0.320 seconds). The
propulsion input is deliberately pulsed for 2 of every 20 ticks, so a short
sample can land inside either the moving or resting part of the pulse. In the
first run, 10-tick COM-derived speed alternated between roughly 0.15–0.22 m/s
and near-zero while the physics velocity often stayed near 0.05 m/s. This
accounts for substantial sample-phase aliasing.

There is still an unresolved readback discrepancy near a stop: on the final
arrival sample, the physics velocity magnitude was 0.188 m/s while the
20-tick COM-displacement estimate was 0.009 m/s. The body position and target
distance remained consistent with the displacement estimate. Treat the
position-derived cycle average as the controller's progress/settling signal;
keep physics velocity as an independent diagnostic and conservative guard.
The evidence points to a physics-velocity/timing readback mismatch under this
headless run, not a coordinate-frame mix-up. It does not establish the
engine-level cause, so instantaneous physics speed must not yet be presented
as a verified measure of chassis translation in this fixture.

## Arrival correction

The first approach run used a 0.5 m gravity-plane radius. It moved the COM
from 2.019573 m to 0.522248 m from the waypoint in 1,062 ticks (16.992
simulation seconds), traversing 1.613 m. Its old completion check accepted
0.499397 m of ground-plane distance despite a 0.522248 m full 3D separation.
That run is **not counted as an arrival**.

Depth-mode arrival now interprets the requested radius as a world-space
sphere. Given a vertical error, the controller derives the remaining planar
radius as `sqrt(worldRadius² - verticalError²)`, requires a near-stop, and
logs `arrivalMetric=world-3d`, `worldArrivalRadius`, and
`planarArrivalRadius`. A wheeled target whose vertical offset is outside the
world-space radius fails explicitly instead of being reported complete from
planar proximity. Legacy navigation remains unchanged. The new policy has
unit coverage for vertical offsets, points outside the 3D radius, and invalid
inputs.

## Trial record

Both commands addressed the same fixed point and same Blue Truck/front-camera
pair. The engine was cleanly stopped between runs; there was no user movement.
The slight difference between the first run's terminal pose and the next
session's acquired pose is retained below rather than hidden.

| Run | Start COM-to-target | End COM-to-target | Elapsed sim time | COM path | COM net displacement | Result |
|---|---:|---:|---:|---:|---:|---|
| Approach, `1f9cc8a8642edef11ffe7b4303c1f19e` | 2.019573 m | 0.522248 m | 16.992 s / 1,062 ticks | 1.613 m | 1.533 m | Rejected as arrival: full 3D radius not met |
| World-radius recheck, `9105e04da1f303676e314f50b6a33177` | 0.526619 m | **0.499391 m** | 1.744 s / 109 ticks | 0.145 m | 0.036 m | **Complete: inside 0.5 m 3D radius** |

Across the two active controller windows, total recorded COM path was **1.758
m**, initial-to-final COM displacement was **1.564 m**, and summed active
simulation time was **18.736 s** (the clean stop/reload interval is excluded).
The closest world-space COM sample in the corrected run was its terminal
sample at 0.499391 m. Its contemporaneous gravity-plane distance was 0.471892
m and its vertical error was 0.163 m. The destination is reached in the
controller's explicit 3D metric, not inferred from the decreasing planar
distance alone.

## Reproduction and artifacts

- Fixture: `test-suite-world-v3-gravity-001`, destructive-lab disposable copy.
- Target: `[7821.479994, 869.924461, 9204.985096]`.
- Controller: `83781253011528772`; depth camera: `80564599189339180`.
- Corrected plugin tree SHA-256:
  `435a3ba80b3a3e9a618520f158919d0391081e92bd91c0eea9d8bd441c060943`.
- Core/policy checks: **25/25 passed** with
  `dotnet run --project tests/Voidwright.CoreTests/Voidwright.CoreTests.csproj`.
- Approach receipt: `Quarantine/096ad87d1f2361146dc65b53f86ab723/terminal-receipt.json`,
  SHA-256 `2a0fac8cbde3e480635192990735df39b3b634526a57160598c8b637e3b7e87b`.
- Approach server log: `SpaceEngineersDedicated_20261008_184323853.log`,
  terminal SHA-256 `f894df17c3e2d4ac518541f596e23fb2148934e19e63c829c565f409ab4b41fc`.
- Corrected arrival receipt: `Quarantine/4ffef628a9974ab3ab05fbbe4d57cb15/terminal-receipt.json`,
  SHA-256 `d11ba6dbcb43c10da40c1c4004a97ac07ebb660aca73af27e33448825350da03`.
- Corrected server log: `SpaceEngineersDedicated_20261008_185045221.log`,
  terminal SHA-256 `c0e45f4ff7c067e723fbc5b8bcd61639af81a2a44d03ab8625b0e0cea51273f7`.
- The corrected receipt reports `passed=true`, `clean_shutdown=true`, no fatal
  markers, and the expected plugin marker. The instance service is now inactive
  and no lab command is queued. Only the disposable copy's sector changed; its
  source-tree fingerprint remained `7e62e30c872778cf18da23a0c7ae2253eb7882901f991789ad16da9845f87b47`.

## Limits

This demonstrates one straight, open-terrain, depth-guided waypoint arrival.
It does not demonstrate obstacle negotiation, turning-course completion,
repeated-run reliability, RGB handoff, or that the raw physics velocity field
is trustworthy. The requested 0.4 m follow-up was rejected by the existing
VrageCage command validator (minimum supported arrival radius is 0.5 m); no
command or world change resulted. Nothing was deployed, committed, or pushed.
