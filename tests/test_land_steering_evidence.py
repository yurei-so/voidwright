"""Regression checks for the recorded LAND engine traversal and steering trace.

These checks validate archived evidence; they do not run Space Engineers or
claim that a candidate steering policy has been validated in engine.
"""

import hashlib
import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts/land-ramp-green-2026-10-17"
MANIFEST_PATH = ARTIFACTS / "engine-route-manifest.json"
FINAL_RECEIPT_PATH = ARTIFACTS / "green-leg7/terminal-receipt.json"


def field(event, name):
    match = re.search(r"(?:^|\s)" + re.escape(name) + r"=([^\s]+)", event)
    if match is None:
        raise AssertionError(f"missing {name} in evidence event")
    return match.group(1)


def operation_samples(receipt, status="running"):
    return [
        event
        for event in receipt["receipt"]["lab_operation_events"]
        if "type=server-control" in event
        and f"status={status}" in event
        and "elapsedTicks=" in event
    ]


def clear_hysteresis_replay(samples, enter=0.085, exit=0.075):
    """Offline candidate only: leave non-Clear commands immediate; gate Clear trim."""
    active = False
    output = []
    for sample in samples:
        status = field(sample, "depthDecision")
        command = float(field(sample, "steerCommand"))
        if status != "clear":
            output.append(command)
            continue

        error = abs(float(field(sample, "headingError")))
        if active and error <= exit:
            active = False
        elif not active and error >= enter:
            active = True
        output.append(command if active else 0.0)
    return output


def clear_soft_deadband_replay(samples, lower=0.075, upper=0.085):
    """Offline candidate only: ramp Clear trim smoothly across a small band."""
    output = []
    for sample in samples:
        status = field(sample, "depthDecision")
        command = float(field(sample, "steerCommand"))
        if status != "clear":
            output.append(command)
            continue

        heading = float(field(sample, "headingError"))
        scale = max(0.0, min(1.0, (abs(heading) - lower) / (upper - lower)))
        nominal = max(-1.0, min(1.0, heading / (3.141592653589793 / 6.0)))
        output.append(nominal * scale)
    return output


class LandSteeringEvidenceTests(unittest.TestCase):
    def test_recorded_engine_route_remains_a_successful_baseline(self):
        manifest = json.loads(MANIFEST_PATH.read_text())
        receipt_bytes = FINAL_RECEIPT_PATH.read_bytes()
        final = json.loads(receipt_bytes)
        record = final["receipt"]

        self.assertEqual(manifest["schema"], "voidwright.land-route-engine-trial.v1")
        self.assertEqual(manifest["route"]["start_marker"]["name"], "Obstacle Ramp Bottom")
        self.assertEqual(manifest["route"]["destination"]["name"], "Green Zone")
        self.assertEqual(
            manifest["route"]["waypoint_order"],
            [
                "Obstacle Empty Space",
                "North wall bypass A (custom)",
                "North-east corridor B (custom)",
                "Green Zone",
            ],
        )
        self.assertLessEqual(
            manifest["route"]["final_world_distance_to_green_m"],
            manifest["route"]["destination"]["arrival_radius_m"],
        )
        self.assertLess(manifest["route"]["final_speed_mps"], 0.02)
        self.assertEqual(manifest["route"]["final_drive_input"], 0.0)
        self.assertFalse(manifest["execution"]["client_confirmed"])
        self.assertFalse(manifest["safety_evidence"]["collision_contact_counter_available"])

        self.assertEqual(hashlib.sha256(receipt_bytes).hexdigest(),
                         manifest["final_terminal_receipt_sha256"])
        self.assertEqual(record["lab_operation_id"], manifest["final_operation_id"])
        self.assertEqual(record["lab_operation_status"], "complete")
        self.assertTrue(record["passed"])
        self.assertTrue(record["clean_shutdown"])
        self.assertEqual(record["fixture_edit_count"], 0)

        completed = operation_samples(final, status="complete")
        self.assertEqual(len(completed), 1)
        self.assertLessEqual(float(field(completed[0], "targetDistance")), 3.1)
        self.assertLess(float(field(completed[0], "positionDerivedSpeed")), 0.02)
        self.assertEqual(float(field(completed[0], "driveInput")), 0.0)

    def test_saved_clear_trace_links_command_toggles_to_wheel_angles(self):
        receipt = json.loads(FINAL_RECEIPT_PATH.read_text())
        samples = operation_samples(receipt)
        self.assertEqual(len(samples), 21)
        self.assertTrue(all(field(sample, "depthDecision") == "clear" for sample in samples))

        headings = [float(field(sample, "headingError")) for sample in samples]
        self.assertEqual((min(headings), max(headings)), (0.079, 0.081))

        commands = [float(field(sample, "steerCommand")) for sample in samples]
        command_transitions = sum(
            abs(after - before) > 0.0001
            for before, after in zip(commands, commands[1:])
        )
        self.assertEqual(command_transitions, 7)
        self.assertEqual(set(commands), {0.0, 0.153, 0.154})

        for sample, command in zip(samples, commands):
            wheel_angle = float(field(sample, "avgSteerAngle"))
            if command == 0.0:
                self.assertEqual(wheel_angle, 0.0)
            else:
                self.assertAlmostEqual(wheel_angle, -0.698132 * command, delta=0.002)

        # Candidate is an offline replay of the recorded Clear segment only.
        # It suppresses the observed threshold chatter, but is not an engine test.
        candidate = clear_hysteresis_replay(samples)
        self.assertEqual(set(candidate), {0.0})
        self.assertEqual(
            sum(abs(after - before) > 0.0001 for before, after in zip(candidate, candidate[1:])),
            0,
        )

        soft = clear_soft_deadband_replay(samples)
        self.assertAlmostEqual(min(soft), 0.06035, places=4)
        self.assertAlmostEqual(max(soft), 0.09282, places=4)
        self.assertLess(max(abs(after - before) for before, after in zip(soft, soft[1:])), 0.033)

    def test_offline_candidate_does_not_filter_avoid_commands(self):
        sample = (
            "Voidwright evidence: type=server-control status=running "
            "depthDecision=avoid headingError=0.35 steerCommand=0.667"
        )
        self.assertEqual(clear_hysteresis_replay([sample]), [0.667])
        self.assertEqual(clear_soft_deadband_replay([sample]), [0.667])


if __name__ == "__main__":
    unittest.main()
