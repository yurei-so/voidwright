# Labnote: Small wheel twitches during the Green Zone traverse — 2026-10-17

## Question

The rover completed the waypoint-guided route around the obstacle, but the
operator noticed frequent, very small wheel twitches while watching it drive
in-game. What is moving: the controller's steering target, the simulated wheel
angle, or only the rendered image?

## Observation

The operator's in-game observation was: **many tiny wheel twitches while the
truck was driving; the rest of the run looked excellent.** This is a
client-side visual observation. No video, timestamped frame sequence, twitch
frequency, or twitch magnitude was saved, so this note records the symptom
without assigning it a cause.

## Relevant engine evidence

The observation came during the disposable V2R3 LAND test. The final bounded
leg drove toward the existing `Green Zone` GPS target from the north-east
corridor:

- Fixture: `test-suite-world-v2-r3-landcorridor-supervised-20261017-a`
  (`c3430e4b9af94d17456bc74d22e2c9de`).
- Final operation: `80e76bc5d931051cc214ccca3f10d7c6`; run:
  `fe91fc24588220ac22f9da824d6cbb8f`.
- Destination: `Green Zone` at `(53.16, 14.49, 38.46)` m; arrival radius
  `3.1` m.
- The leg completed after `964` ticks. It started `13.826` m from the GPS
  point, finished `2.914` m away, and logged `3.745` m of path.
- Depth telemetry reported `7.167` m minimum corridor clearance and `0`
  self-grid hits. No fatal markers were present; the fixture edit count was
  `0`.
- Speed cap for this leg was `0.5` m/s. The same route's earlier engine trial
  used separate bounded legs and a `0.25` m/s cap; see
  [the engine-trial report](land-ramp-green-engine-trial-2026-10-17.md).

These server measurements establish that the depth-guided rover moved and
completed this leg. They do **not** measure wheel twitch amplitude or count
physics contacts. The previous route manifest likewise records that a
collision-contact counter is unavailable. The operator's visual report is
therefore retained as separate client-observation evidence.

## Initial interpretation before raw-log review

At note creation, the cause was unknown. Plausible explanations to test were:

1. The requested steering command or target heading changes slightly from
   control update to control update.
2. The steering command is stable, but the simulated wheel angle or suspension
   responds with small physical oscillations.
3. The game client renders small wheel motion that is not represented in the
   server's periodic telemetry.

Existing server-control records include fields such as `steerCommand`,
`avgSteerAngle`, per-wheel steering angle, heading error, yaw rate, and speed.
The captured progress records are too sparse to characterize tiny rapid
changes, and they were not synchronized with client video for this run.

## Follow-up experiment

Repeat a short, bounded drive on a fresh disposable copy with the same rover,
route, and speed cap. Capture the client view and a synchronized telemetry
window around one turn and one straight segment. Temporarily sample the
following at control-tick resolution for only that bounded window, if the
existing engine/plugin path can do so safely:

- requested steering command and chosen depth heading;
- each wheel's current steer angle and steering override;
- heading error, yaw rate, angular velocity, and forward speed;
- simulation tick and client video timestamp.

Compare the command and wheel-angle traces. Repeated command sign/target
changes would implicate controller jitter; steady commands with moving wheel
angles would point toward simulated vehicle response or client rendering.
Keep the existing speed bound, depth-clearance stop, progress stop, and
emergency neutralization. Do not tune steering to hide the twitch until its
source is measured.

## Result

The route's final engine leg **passed its GPS arrival criterion**. Wheel
smoothness remains an **open observation** pending synchronized capture and
measurement. No control changes were made for this note.

## Follow-up telemetry review — 2026-10-18

I inspected the stopped disposable fixture's raw server log as well as the
saved route receipt. The server log is on `mpai` at
`/home/alu52/.local/share/vragecage-worker/instances/test-suite-world-v2-r3-landcorridor-supervised-20261017-a/SpaceEngineersDedicated_20261017_210545850.log`,
SHA-256 `b14fa54b400a9a80e3592844edba316d9492896889072195fedd815e81c930ee`.
For operation `80e76bc5d931051cc214ccca3f10d7c6` (run
`fe91fc24588220ac22f9da824d6cbb8f`), it contains 98 motion samples at roughly
10-tick intervals and 17 steering summaries at 60-tick intervals. The
operation completed at tick 964 inside its 3.1 m arrival radius and reported
zero fixture edits.

### What the measurements support

- In the successful final-leg receipt (`green-leg7`, operation
  `abee1e770fbdedfb01b3ffafd7ff7212`), 21 running summaries show a clear
  depth decision throughout and heading error between 0.079 and 0.081 rad.
  The command changes between 0 and about 0.154 seven times. At those same
  60-tick samples, the two front wheels report either 0 or about -0.107 to
  -0.108 rad, matching the requested command's direction and scale.
- In the later supervised leg, steering summaries show the same pattern: the
  front-wheel angle follows the zero/small-command changes. At tick 964 the
  command had been neutralized while the measured wheel angle was still about
  -0.107 rad, showing that the wheel does not instantly return to center.
- The controller in [`Plugin.cs`](../ServerPlugin/Plugin.cs#L1940) uses a
  hard `abs(heading) < 0.08` cutoff with no hysteresis. The clear-path heading
  sits on both sides of that cutoff. Replaying the
  every-10-tick chassis pose against the target gives a reconstructed
  target-relative heading range of 0.0764–0.0814 rad, with 41 crossings of
  0.08. This reconstruction is approximate because the camera and control
  frames are not identical; the steering command itself was only logged every
  60 ticks.
- Depth decisions stayed `clear`; their reported corridor clearance stayed
  above 7.1 m in the final part of the route. There is no sign in these logs of
  the obstacle classifier rapidly switching between clear and avoid.
- The 10-tick samples also show small chassis-heading changes in both
  directions while driving, including intervals when the last 60-tick wheel
  sample was centered. That is consistent with vehicle dynamics contributing
  motion, but steering angles were not captured at the same 10-tick rate, so
  the wheel mechanism cannot be isolated from suspension/physics response.

**Assessment:** controller threshold chatter is a plausible and likely
contributor, not a complete explanation of the client's visual observation.
The server reports actual wheel-angle changes, so this is not solely a client
rendering artifact. There is no synchronized client video or high-rate wheel
trace, so the visual frequency and any additional rendering effect remain
unmeasured.

### Offline smoothing evaluation

I replayed the saved clear-path summaries through a candidate deadband with
hysteresis: enter steering above 0.085 rad, return to neutral below 0.075 rad.
Starting neutral, this removes all seven observed on/off transitions in that
trace. It also suppresses the entire small correction on that segment, so it
is not safe to adopt from this replay alone: the full route's final arrival was
only 0.002 m inside its 3.1 m radius. A smooth ramp through the deadband may
preserve more tracking authority, but it also needs a fresh engine run.

I also replayed a soft command ramp from 0.075 to 0.085 rad. Across the saved
clear-path samples it produces commands from 0.060 to 0.093 instead of toggling
between zero and about 0.154; the largest adjacent sample change falls to
0.033. This keeps some steering trim, but it is not a time-based slew limit,
and the full route's narrow arrival margin means its effect still needs a
disposable engine run.

A steering-rate limit is not the first choice: slowing an avoidance turn could
delay an urgent correction. Any future filter should apply only to small
`clear`-path trim and let `avoid`, no-clear-corridor stops, stale-scan stops,
and emergency neutralization act immediately. Existing policy checks cover
clear-corridor selection and neutral stops, but they do not substitute for a
candidate engine run.

### Regression and remaining measurement

Added `tests/test_land_steering_evidence.py` to verify the successful recorded
Ramp Bottom → Green Zone engine receipt, its clean stop and untouched fixture,
and the command/wheel correlation in the saved trace. This is an evidence
regression check, not a rerun of Space Engineers and not client confirmation.
The existing obstacle simulation and depth-policy tests still pass.

No steering behavior was changed, no server was started, and no original save
was touched. The next decisive measurement is a bounded disposable run that
logs heading, command, each steering wheel's angle, scan age/status, and
simulation tick together at 1–2 tick intervals, with synchronized client video.
Then compare the current controller against a clear-only smooth deadband while
retaining immediate obstacle avoidance and the Ramp Bottom → Green Zone
arrival criterion.

## Soft steering-ramp engine experiment — 2026-10-18

The follow-up paired the preserved controller with a candidate clear-only
soft ramp on two disposable copies of the same saved world. The candidate
finished the same four-waypoint Ramp Bottom → Green Zone route at 2.265 m from
Green Zone, matching baseline and leaving 0.235 m within the 2.5 m radius. It
used 21 bounded operations versus baseline's 22. Both fixtures had zero edits,
zero self-grid hits, matching staged-plugin hashes, and clean shutdowns.

Instrumented 2-tick server traces in matched deadband windows show clear
steering command total variation falling 98.6%, actual front-wheel angle
variation falling 97.7%, and the largest measured 2-tick wheel-angle change
falling from 0.060 to 0.003 rad. Neutral/active transitions fell from 167 to
zero in the compared attempts. Mean absolute heading error was 0.07999 rad for
baseline and 0.07850 rad for the candidate in those samples; this is close
paired-run evidence, not a statistical accuracy guarantee.

The ramp reduces steering authority around 0.08 rad, but no route-level
responsiveness loss appeared in this run: waypoint operation counts were the
same for the first three targets and one fewer for Green Zone. `avoid` steering
and safe-stop paths remain outside the filter. The engine does not expose an
external collision-contact counter, and there was no synchronized client
video, so zero self-grid hits do not prove zero external contacts and this does
not establish a client-rendered twitch frequency.

Full results, run receipts, source snapshots, and SHA-verified raw trace lines
are in
[`the soft-ramp experiment bundle`](../artifacts/land-steering-soft-ramp-20261018/experiment-manifest.json)
and summarized in
[`the 2026-10-18 report`](land-ramp-green-soft-steering-ramp-experiment-2026-10-18.md).
No controller source was changed in the repository and no server is running.

