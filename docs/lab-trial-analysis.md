# Receipt-backed lab trial analysis

`scripts/analyze-lab-trial.py` is a local inspection tool for copied VRageCage quarantine runs. It does not start a server or control a vehicle.

## Modes

From the repository root:

```sh
python3 scripts/analyze-lab-trial.py BEFORE_SENSOR_RUN DRIVE_RUN AFTER_SENSOR_RUN
python3 scripts/analyze-lab-trial.py --drive-only DRIVE_RUN
python3 scripts/analyze-lab-trial.py --compare-drives DRIVE_RUN_A DRIVE_RUN_B [DRIVE_RUN_C ...]
```

Run arguments are directories containing `run.json`, `command.json`, and `terminal-receipt.json`. Sensor runs also need their `.pgm`, `.n16le`, and sensor metadata `.json` files. The report is JSON on stdout; redirect it to a file when a durable derived artifact is useful.

The three-run mode checks one instance/fixture, sensor → drive-distance → sensor order, the same camera, and consistent run/operation IDs. It verifies the receipt-recorded SHA-256 hashes for the depth, normal, and sensor metadata files. It rejects mismatches instead of producing a plausible-looking comparison. The two drive-only modes use terminal actuator events and retain the requested command as a separate field.

The drive-comparison mode reports whether the requested distance/speed/tick ceilings and staged plugin builds match. A mismatch is surfaced, not silently normalized away; comparing different builds can be useful, but it is a different experiment.

## Evidence boundary

- `sensor_evidence` is camera/raycast-derived: pose, depth hit counts, lower-center depths, dominant visible normal, and a ray/normal-projected surface offset. It is **not** a native screenshot or simulator ground truth. A miss does not prove empty space, and the dominant normal does not identify a floor by itself.
- `privileged_evaluator_telemetry` is server-side actuator/physics reporting: requested command, elapsed ticks, displacement, speed, steering angle samples, and heading/translation deltas. It can evaluate a trial but must not be described as something the camera perceived.
- A completed distance command says the measured distance threshold was reached. It does not establish arrival at a waypoint, obstacle avoidance, or that the vehicle maintained heading.
- A cross-body comparison shows repeatability or differences, not their cause. Different hardware, starting poses, and surface contacts remain confounders even when requested command settings match.

The V2R3 [after-action receipt](../artifacts/v2r3-demo/AFTER-ACTION.md) is the worked example. Its three matched 0.5 m trials show yaw with measured steering angle zero; the cause remains open. Tests: `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests -p 'test_lab_trial_analysis.py'`.
