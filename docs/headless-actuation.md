# Headless actuation boundary

Voidwright's route planner and follower can produce bounded LAND guidance. The
ordinary Space Engineers mod remains observation-only: the script sandbox does
not provide a verified way to apply that guidance to an unpiloted wheeled grid.

## Verified on the real dedicated-server fixture

- Setting public suspension propulsion and steering overrides was accepted and
  read back on four healthy, attached wheels, but the dynamic grid remained at
  zero speed.
- The public controller movement path depends on player/extended-control
  propagation and likewise did not actuate the headless grid.
- Direct grid wheel-system angular velocity is the engine path that reaches the
  wheel subsystem, but Magnetar's real script compiler rejected that member as
  prohibited. The rejected build was not retained.
- Vanilla autopilot is not a production fallback for Voidwright. For an
  isolated R5 reference experiment, we separately enabled the real Flight
  Movement block and Path Recorder to establish an engine-side route baseline;
  that does not grant the ordinary mod movement authority or count as a
  Voidwright-controlled run.

The imported world remains fixture data, not a schema. Observed controller
names and block inventories are reported as evidence; guesses about whether a
grid is a truck, hovercraft, or trailer are never baked into classification.

## Privileged development adapter

The local tooling extension adds reviewed, idempotent trial orchestration for
operator-observed testing in a disposable world. It does not alter the ordinary
mod's authority. Human presence does not block the bounded one-shot actuator;
server shutdown independently requires fresh zero-human evidence and ownership
of the same run, so a connected operator is never disconnected by cleanup.
These changes have policy/fixture tests and SDK compilation, with live
activation and engine verification still gated. See
[higher-level vehicle tools](agent-vehicle-workflow.md).

A working headless adapter needed one of:

1. a sanctioned public game API that propagates autonomous wheel input; or
2. a narrowly scoped, separately reviewed server-plugin bridge that accepts a
   bounded command for an explicitly armed controller and applies the internal
   wheel input outside the mod-script sandbox.

The second option is implemented only behind VRageCage's destructive-lab
boundary. It uses a universal harness principal rather than player/faction
identity and treats every copied grid as an unsupported development target.
Commands are bounded and one-shot, neutralize on completion/timeout/disposal,
and remain correlated with immutable source, package, log, run, and terminal
receipt hashes. It must never initialize in an ordinary save. This is a
development adapter, not the mod's production actuator or a substitute for a
future explicit in-game authority design.

VRageCage destructive-lab instances provide that boundary for development. The
harness validates the full instance/import/run contract before launch; the
`ServerPlugin` independently checks the activated lab identity and quarantine
before initialization. Neither check is a standalone authorization system.
The initialization marker only proves that the gate opened; a separately
correlated terminal receipt proves whether a command actuated and neutralized a
fixture. Live tests have confirmed the internal wheel-system input moves the
r4 utility truck, and that a route command can complete after the vehicle has
been brought within its requested arrival radius. This remains limited
evidence: with the corrected drive-sign mapping, a 46.29 m Yellow Zone run
closed to 8.60 m spatial distance over 90 seconds, but timed out just outside
its 8.5 m radius (best recorded planar distance: 8.49 m). A new operation with
a 9 m radius then completed on its first simulation tick from 8.59 m spatial
distance. Do not interpret that as proof of precise waypoint tracking,
obstacle avoidance, broad vehicle compatibility, or production readiness. The
test created no player or faction identity.

A separate Red Zone trial first exposed a drive-sign defect: from 64.19 m away,
the old sign pairing moved away from the target and stalled after 1,201 ticks.
The bridge now reverses the sign mapping and records the applied input. On a
clean r4 copy that mapping reduced distance to Red Zone from 64.19 m to 41.25 m
over 1,800 ticks (30 seconds), with a clean server and no fatal markers. A
follow-up from that position moved to 40.94 m, then stalled at the same point
for 1,200 ticks; the terminal receipt correctly reported `stalled`. This is
evidence that the sign correction improves open-ground convergence, not that
route following is solved. It still cannot plan around or recover from the
proving-ground obstacle it encountered.

A separate fresh-copy Green Zone run tested a different bearing. It reduced
distance from 76.96 m to 21.68 m over the 3,600-tick (60-second) cap. Two
bounded continuations brought it to 10.30 m, then 9.90 m spatial distance, but
both timed out before the 9 m arrival radius. At the 9.90 m timeout, a forward
truck-camera frame hit geometry in 2,151/2,304 rays, with median hit depth
about 5.71 m; because the camera looked
along the truck's nose while the target was nearly perpendicular, this shows a
nearby, cluttered forward scene but does not prove the target path itself was
blocked. A final continuation from 9.83 m with the speed cap raised from 1.2 to
2.0 m/s completed at 9.00 m after 645 ticks (10.75 seconds). This supports
modestly increasing the cap for this approach, but does not erase the prior
timeouts or establish a generally safe speed. The server remained healthy and
shut down cleanly. Together, the trials show useful movement and inadequate
near-target/obstacle behavior—not general autonomous driving.

The lab adapter applies a temporary per-wheel speed cap derived from the
command's bounded `maxSpeed` (maximum 18 km/h) for both distance-only and
navigation commands; it restores each wheel's captured speed limit when it
neutralizes. Navigation also bounds steering angle and restores captured
steering settings and temporary steering-axle flags. Propulsion input is set to
neutral. A terminal receipt must be checked after every run; a timeout is not
an arrival, even if the vehicle is visibly close to the GPS point.

### Front-camera depth-guided lab controller

`drive-to-coordinate-depth` is a separate, explicit destructive-lab command for
short low-speed trials. It resolves a camera ID on the same wheeled grid as the
selected controller, verifies that camera faces into the controller's forward
hemisphere, and samples a 19-ray horizontal fan from -90 to +90 degrees every
six simulation ticks from that camera pose. The fan is an engine-physics raycast depth proxy registered to
the front camera, not a claim that the game's camera block exposes a native
depth stream. A pure deterministic policy keeps the requested heading when a
three-ray corridor has at least 3.4 m clearance, selects a clear neighboring
corridor otherwise, and stops if all corridors are blocked, the target leaves
the 180-degree fan, the scan is stale/invalid, or the camera cannot be used.

Depth-guided commands are capped at 0.8 m/s and 1,800 simulation ticks by both
the VRageCage command contract and the plugin. The CLI defaults this mode to
0.5 m/s for 900 ticks. Before releasing the brakes, it requires twelve
consecutive ticks below 0.05 m/s; if the truck cannot settle within a bounded
startup window, the command terminates as `depth-startup-motion` for RGB review.
It releases the handbrake only after settling and restores the captured setting
when the run terminates.
Depth mode caps propulsion at 0.4 duty in a 20-tick pulse window, the same pulse
ceiling used by legacy lab guidance but with much lower wheel power. It preserves
the saved wheel-power ratio while capping it at 15%. The 40% ceiling is a
throughput adjustment for the zone-baseline practice: the earlier 10% ceiling
held the truck to about 0.08 m/s, and a 25% test remained at about 0.17 m/s on
clear terrain. The requested speed cap, tick cap, depth guards, and
telemetry-based braking remain unchanged. This still avoids full-power wheel
impulses that can exceed the low-speed target before the next telemetry check.
Braking engages before the requested speed is exceeded. A three-second no-motion
guard hands off before the longer route-stall timeout. The controller also stops
on excess speed, a 45-degree attitude
excursion, ten seconds without progress, or sustained motion away from the
target. It never reverses using a forward-only sensor. A terminal depth or
telemetry stop is a handoff point for RGB/HoloView inspection and a fresh,
operator-directed command; it does not autonomously resume after a failure.
Receipts classify depth-mode terminal outcomes as terminal failures, so a lost
or stopped operation cannot be mistaken for an active run. They include the
camera identity and sampled pose, decision, chosen heading, corridor clearance,
scan age, self-grid hit count, and motion telemetry. This allows the physics-ray
pose to be compared with RGB/HoloView frame metadata during handoff. This remains a
lab-only reactive steering experiment, not full obstacle avoidance or a
production movement authority.

V3 testing found that distance-only commands had previously skipped the wheel
speed-cap setup, so a requested 1.5 m/s run remained limited by the fixture's
saved 1.8 km/h setting. The adapter now applies the same bounded cap before any
drive command. On the disposable V3 clone, a 2 m straight command completed in
123 ticks with a 5.4 km/h temporary wheel limit; terminal evidence showed the
saved 1.8 km/h limit restored. GPS guidance made several metres of progress but
still oscillated and did not reach its target, so this is an actuator-control
milestone, not autonomous-navigation validation.

The same headless plugin can also sample a camera block in bounded batches of
server-side physics raycasts. On r4, the Camera Drone returned 973 of 2,304
rays hitting geometry at 64×36, 90° horizontal FOV, and 250 m range. The static
Obstacle Course Overview camera returned 981 of 2,304 rays at the same
settings. At the Red Zone stall point, the truck-mounted camera returned 1,436
of 2,304 rays at 90° FOV and 100 m range. A 32×18 drone baseline had 246 of
576 rays hit. These are hash-validated 16-bit depth images, not rendered/color
screenshots or reconstructed meshes. Four verified frames (drone, overview,
Red truck, and Green truck) were transformed by their recorded camera poses
into a combined, color-separated world-space point cloud: 5,541 ray-hit
endpoints spanning 191.9 m. This is a useful spatial sanity check without a
licensed graphical client, but it is not surface reconstruction: there is no
ICP registration, denoising, voxel fusion, or meshing. The two truck captures
are from separate copies and positions, so moving geometry is intentionally
kept as distinct colored samples rather than falsely presented as one
time-consistent surface. This is visualization evidence, not an obstacle map
or a basis for authorizing movement.

The sensor now also emits one signed 64-bit little-endian hit-entity ID beside
each depth sample and a signed 16-bit XYZ surface-normal vector. The normal is
read from the raycast hit itself and normalized into the raster; misses have a
zero vector. A bounded entity summary records each hit ID's pixel count,
display name, and runtime type; receipts reconcile these against the ID raster
and verify the normal vectors align with hit pixels and have unit magnitude.
Labels are diagnostic only and do not classify surfaces as safe terrain,
obstacles, or vehicles. A no-hit ray still only means that this physics query
returned no hit within its tested segment, not that a route is safe.

Each frame also records the physics API's natural-gravity vector and derived
local-up vector at the camera origin. This is necessary for classifying hit
normals on a spherical planet: comparing a normal to global Y would mislabel
many ordinary floors and walls. The receipt checks that both vectors and the
reported gravity magnitude agree within the metadata's rounding precision.

The V3 gravity sandbox provides a receipt-backed entity-label check across all
four cameras on both trucks. Eight sequential 32×18 frames at 50 m and 90°
horizontal FOV produced 2,278 hits, each paired with an entity ID, and zero
hits on the observing truck's own grid. Runtime metadata identified 2,213
returns as `MyVoxelPhysics`, 57 as the known Axolotl cargo-drone grid, and 8 as
other fixture grids. The voxel-physics result is consistent with the planet's
surface in this fixture, but the plugin does not make that semantic inference
for control. A point cloud derived from these frames is approximately
registered from rounded poses; the views are not synchronized, fused, or
evidence that the route between points is clear. These runs used only the
disposable V3 clone, not the source save.

A second eight-camera pass added signed surface normals and the local natural-
gravity vector/up direction to each receipt. All 2,281 hit rays had a verified
entity ID and a verified unit normal; none hit the observing truck's own grid.
Using each frame's own local-up vector, 2,217 normals were floor-facing, 56
wall-like, four slope-like, and four down-facing. These are orientation bins
for inspection only, not semantic labels or traversability decisions. The
local gravity was 9.81 m/s² and local up was approximately
`[0.857, 0.100, -0.507]` in world coordinates, demonstrating why global Y is
not an appropriate ground reference here.

Two additional front-camera sweeps used 64×36 rays, 100° horizontal FOV, and a
250 m cap. Red returned 1,974/2,304 hits (85.7%); median hit depth was 12.83 m
and the 90th percentile 164.75 m. Blue returned 1,092/2,304 (47.4%), with a
median of 8.97 m and a 90th percentile of 68.47 m. The Red view included a
large far-field band of wall- and slope-like normals, while Blue's different
camera pose mostly saw floor-facing terrain. This cross-view difference is
useful evidence that sensor pose changes the observed surface mix. Combining
these two sweeps with the eight directional frames yields 5,347 approximate
endpoints; the set remains sequential, uses rounded camera poses, and does not
prove route clearance or construct a fused mesh.

### R5 rebuilt obstacle-course truck

R5 is a materially different fixture from the earlier R4 trials. Its recorded
24-point path is attached to the rebuilt `Obstacle Course Truck` (four wheel
suspensions and a forward camera), not the `Blue Utility Truck` used in the
older failed zone-navigation runs. The original local R5 save was staged as
`test-suite-world-v1-r5-path-inspection`; all actuator experiments below used
that disposable copy. A clean load/save before actuation preserved the route
data and its `Repeat=true` setting. The original save was not edited.

The rebuilt truck completed a bounded 2 m drive-distance command in 178 ticks:
the receipt reported 2.009 m displacement and 0.968 m/s. Its camera origin
shifted about 2.6 m between integrity-checked frames bracketing the run, which
independently confirms real grid motion. This establishes that the rebuilt
truck's wheels respond; it does not establish successful obstacle-course
following.

The first low-speed drive-to-coordinate probe exposed a ground-target bug. The
`Safe Spawn Zone` GPS marker was 5.66 m above the truck along local gravity. The
old arrival check used full 3D distance even though wheel guidance only moved in
the gravity plane: it timed out at 5.90 m planar distance / 8.18 m spatial
distance. Land guidance now uses planar distance both for arrival and approach
speed; air guidance remains three-dimensional. With the change, the same
disposable fixture reached a 2 m planar arrival radius after 1,431 ticks while
still 6.00 m away in 3D. The server shut down cleanly. The long approach and
remaining steering behavior are still limitations; this is not a route replay
or obstacle-avoidance result.

### R5 Flight Movement reference and Voidwright comparison

A fresh disposable copy of the unchanged R5 save established the route baseline
using the actual Flight Movement block, not its Remote Control. AI Behavior was
enabled on Flight Movement block `97423065414544907` before Path Recorder
`108829740530593017` was played. The recorder exposed 24 points with
`Repeat=true`; telemetry advanced from Waypoint 9 through Waypoint 32, then
wrapped to Waypoint 9. At the wrap, the vehicle was 6.07 m from its starting
position after traversing the course, and the server reported no fatal markers.
Wheel propulsion/steering overrides remained zero, while Flight Movement AI
and autopilot were active. This confirms the engine's Move-block route behavior
on this fixture; it is not evidence that Voidwright drove the truck.

The fresh save did not load its onboard rover programmable block as running,
so the disposable harness explicitly started the inspected script. It remained
reported as not running in periodic samples and the wheel override sums stayed
zero. The observed movement therefore came from Flight Movement; this test
does not establish a contribution from the PB or explain why its saved running
state did not survive the server import/load path.

A separate fresh-copy comparison let the VRageCage wheel-guidance adapter
follow the same saved recorder points without enabling Flight Movement or
playing the recorder. At 1.5 m/s maximum speed and a 4 m waypoint radius, it
reached the first two selected targets, then stalled while pursuing the third:
best distance to that target was 6.13 m, while the final distance grew to
13.12 m after the 1,200-tick no-progress guard. The server remained healthy and
stopped cleanly. A bounded 32×18 forward-camera sample at the stall captured
442/576 ray hits, indicating a cluttered view but not proving that an obstacle
caused the stall. A second sample at the maximum supported 120° horizontal FOV
captured 366/576 hits; the target waypoint was still about 71° off the camera
axis and outside that frame. Neither sample establishes that an obstacle
caused the stall. This makes the comparison useful but not causal: the vanilla
Move block completed and repeated the route, while the current custom wheel
guidance did not complete the same course. Steering/waypoint progression and
obstacle interaction remain the next debugging targets.

A third clean-copy sensitivity run held the 1.5 m/s speed cap constant and
increased waypoint acceptance from 4 m to 6.5 m. It passed the next waypoint
that the tighter run could not hand off, advanced one further, then stalled
on the following target at 8.35 m after its no-progress guard. A 32×18 camera
frame from that stop hit all 576 rays (median depth 1.03 m), but the target was
about 60.5° off the camera axis—just outside the supported 120° frame—and the
truck was pitched down toward nearby ground. The larger radius therefore
improved waypoint progression without solving the course; these camera data
still do not identify the stall cause.

### R5 latest-save reference and guarded follower rerun

The local R5 save later changed from the earlier imported tree hash
`6c2a6d96…` to `b814e0e2…`. Read-only fixture inspection confirmed the same
Obstacle Course Truck, camera, recorder, Remote Control reference, and waypoint
set, so the newer copy was tested separately rather than silently treated as
the old baseline.

On a fresh copy of this newer save, Flight Movement AI Behavior was enabled
before recorder playback. Across the full bounded 7,200-tick window, the live
receipt showed both AI Behavior and autopilot active, no manual wheel overrides,
and progress from the route start through Waypoint 31. It ended 20.52 m from
its initial position at `bounded-window-complete`, with no fatal markers and a
clean shutdown. This is a strong vanilla reference, but the bounded run ended
before a complete route wrap on this newer save.

The matching Voidwright wheel-follower run used a 2.0 m/s speed cap and 6.5 m
waypoint radius. It stalled at route index 4 (Waypoint 13) after 4,967 ticks,
8.41 m planar distance from the target and 2.03 m vertical offset. Chassis-up
was still about 41° from local up (dot 0.756), so the new 60° attitude cutoff
did not fire; the existing no-progress guard neutralized the wheels first.
There were no fatal markers and the disposable server stopped cleanly. A
64×36 forward-camera depth sample from the stalled position saw nearby geometry
(1,768 hit rays; center sample about 0.79 m), but it does not establish that
the next waypoint was physically occluded.

An experimental reverse-mode exit threshold of 0.2 and a fixed steering
polarity were compared in separate fresh copies, then reverted. Neither gave a
repeatable gain: the fixed-polarity run stalled on its first waypoint, while
the altered reverse threshold still stalled at Waypoint 13 and had a worse
best approach distance than the prior 6.5 m-radius sensitivity run. Retain
the original reverse-specific steering polarity and 0.9 exit threshold until
a more controlled steering test says otherwise. The current evidence narrows
the next investigation to terrain/approach geometry and near-target steering;
it does not yet justify adding an obstacle planner or claiming autonomous
course completion.

### V3 planetary pursuit follow-up

Additional bounded trials on the disposable V3 clone tested half steering gain,
the repository's full steering gain, and temporary wheel-power floors. Half
gain covered about 2.5 m at its best but ended with a large heading error. The
full-gain continuation moved from 51.62 m to 49.64 m from the `Starting Area`
target in 600 ticks, so the half-gain variant was discarded.

V3's four propulsion wheels report internal `MyMotorSuspension.Power=0.15`,
which means **15%**. The earlier telemetry incorrectly labelled the internal
0–1 ratio as a percentage. Inspection of the worker's game assembly confirms
that `GetPowerForTerminal()` multiplies `Power` by 100 and the public interface
uses that adapter. Telemetry now records both `wheelPowerRatio` and the
corrected `wheelPowerPercent`. Historical raw receipts retain their original
values; interpret their old percentage field as a ratio.

The inspected worker assembly was `Sandbox.Game.dll`, SHA-256
`f58e7025a3c768f0908ece08474958912092f0f446ae7d30cb040c3003c0ae01`.
The relevant implementation is `Sandbox.Game.Entities.Cube.MyMotorSuspension`:
its concrete `Power` property returns the synchronized ratio directly, while
the explicit `IMyMotorSuspension.Power` property delegates to the terminal
adapter. This distinction matters because the server plugin iterates concrete
wheel objects through `GridWheels.Wheels`.

Temporary power-floor trials were previously labelled "15%" and "2%" in these
notes. Those labels are withdrawn because the internal/public API units were
confused; they must not be used as a calibrated percentage sweep. Their
observed motion remains useful only as historical behavior: the stronger
trial reached 3.26 m/s against a 0.5 m/s request, and the less aggressive trial
eventually tripped the no-progress guard at 1,390 ticks while 43.16 m from the
target. No segment reached the 2 m arrival radius. A low-power explanation for
the stall is not established by the original `0.15` reading.

A final 32×18, 90° front-camera depth sample at the resulting truck pose logged
305/576 hits (median 8.39 m, 4.7–45.28 m). The top seven pixel rows had no
returns; the bottom ten were fully occupied by nearer terrain. This supports an
open upper view and ground-dominated lower field, but does not identify a
specific obstacle or prove that the entire route is clear. Both depth and
metadata artifact hashes were verified by the runner receipt.

The power-floor experiment was removed from repository source after the trial:
the current actuator retains original wheel power and only applies/restores the
existing bounded speed-limit and steering setup. These observations show that
the V3 failure is not explained by a single propulsion-power setting. Gravity,
terrain, traction, reverse-mode transitions, and the steering loop remain
entangled. The V3 clone's engine-saved sector changed during simulation; the
original source world was not touched. Final no-command Magnetar readiness
smoke passed with the repository plugin staged, clean shutdown, and no fatal
markers.

An independent low-speed repeat in the V3 clone targeted the exact `Starting
Area` GPS with a 0.5 m/s cap, 2 m arrival radius, and 600-tick limit. It timed
out cleanly rather than arriving: target distance began at 43.152 m, the
closest logged sample was 43.064 m, and it ended at 43.139 m. The field named
`bestTargetDistance` was 43.101 m because it tracks 5 cm progress checkpoints,
not the exact minimum of every sample. The truck remained nearly
stationary (sampled planar speed roughly 0.03–0.08 m/s against a 0.5 m/s
request); the terminal sample reported `driveInput=1` while forward speed was
`-0.061 m/s`, with about 1 rad heading error. This reproduces the lack of
useful approach in a short, explicitly bounded pass. It is not sufficient to
blame drive polarity alone: wheel-ground contact, slope/traction, and the
large heading error remain plausible contributors. Operation
`b9557eb153d1a2bc22b6f8694767b195` ended `timeout`; the server then stopped
cleanly with no fatal markers. This changed only the disposable clone, whose
post-shutdown sector hash is now
`a5785a706aa8ebf76919d1554393b3bcc4bf7276e76e8d3c58e0358f12d9ce12`.

### V3 gravity-referenced surface sensing

The same bounded sensor was then extended to record each ray's hit normal and
the natural-gravity vector sampled at the camera origin. On V3, local up was
approximately `[0.857, 0.100, -0.506]`; global Y has only about 0.10 dot
alignment with that vector, so using a fixed world axis would have mislabeled
the surface orientation. The implementation uses the hit normal exposed by
[`IHitInfo`](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/VRage.Game.ModAPI.IHitInfo.html)
and local gravity from
[`IMyPhysics.CalculateNaturalGravityAt`](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/VRage.Game.ModAPI.IMyPhysics.html).

Eight 32×18, 90° views from the red and blue camera rigs contributed 2,281
valid hit points with aligned normals and no self-grid hits. Relative to each
frame's own local-up vector, 2,217 normals were floor-facing, 56 wall-like, 4
slope-like, and 4 down-facing. A gravity-leveled point-cloud view is saved as
`v3-local-gravity-pointcloud.png`; these categories are geometric bins only,
not semantic terrain labels or a clearance test.

A same-camera range/FOV sweep then used 64×36 rays, 250 m range, and the same
logged camera origin/direction. At 80°, 100°, and 120° horizontal FOV it hit
2,193/2,304 (95.2%), 1,974/2,304 (85.7%), and 1,758/2,304 (76.3%) rays. The
80° frame saw terrain only; the 120° frame logged 112 drone pixels and 30
incidental wheel-grid pixels, versus 68 and 4 at 100°. This is an early
indication of a narrow-FOV geometric-density / wide-FOV peripheral-object
tradeoff on the red rig, not a general sensor-performance result. The
blue-front sweep showed the opposite coverage trend: hit rate rose from 43.3%
to 47.4% to 53.7% as FOV widened from 80° to 120°, with terrain-only returns.
That contrast shows hit density depends on scene geometry and camera direction.
The exact operation IDs, artifact hashes, and receipt checks are recorded in
[`v3-sensor-trial.md`](v3-sensor-trial.md).

All server runs were stopped cleanly with no target-fatal markers. Engine save
state changed only inside the disposable clone during the final scans; its
source world was not modified. No movement or autonomous-navigation claim is
made from these sensing trials.
