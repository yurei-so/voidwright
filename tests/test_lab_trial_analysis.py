"""Contract tests for the receipt-backed lab trial analyzer."""

import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("lab_trial", ROOT / "scripts/analyze-lab-trial.py")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
FIXTURE = ROOT / "artifacts/v2r3-demo"
BEFORE = FIXTURE / "2ce02799d713a36f7845d142a4335149"
DRIVE = FIXTURE / "8adee2822b4dc17e99866e12aec08f90"
AFTER = FIXTURE / "1f10276852bbae11dbdf583371bc5116"
STEER_CHECK = FIXTURE / "c003737880700f0d675e202c1d8a9eae"
STEER_REPRO = FIXTURE / "28923bc6f009d56ccfcc4191d463de4b"
BLUE_SCAN = FIXTURE / "50a1a7bee9fa505511e55cfc903c3dd9"
BLUE_DRIVE = FIXTURE / "24fda32f15689f43a91e1532fd977fc5"
BLUE_AFTER = FIXTURE / "ec4bb7e3bd67087935e03b2797906385"


class LabTrialTest(unittest.TestCase):
    def test_v2r3_closed_loop(self):
        report = MODULE.analyze(BEFORE, DRIVE, AFTER)
        sensor = report["sensor_evidence"]
        evaluator = report["privileged_evaluator_telemetry"]
        self.assertEqual(sensor["before"]["hit_pixels"], 363)
        self.assertEqual(sensor["after"]["hit_pixels"], 427)
        self.assertEqual(sensor["before"]["dominant_visible_normal_hits"], 191)
        self.assertLess(sensor["before"]["dominant_normal_to_camera_up_degrees"], 2)
        self.assertAlmostEqual(sensor["before"]["dominant_normal_projected_offset_meters"], 2.246, places=2)
        self.assertAlmostEqual(sensor["after"]["dominant_normal_projected_offset_meters"], 2.239, places=2)
        self.assertLess(sensor["before"]["dominant_normal_offset_mad_meters"], 0.01)
        self.assertAlmostEqual(sensor["camera_heading_change_degrees"], 33.542, places=2)
        self.assertLess(sensor["camera_up_change_degrees"], 1)
        self.assertAlmostEqual(evaluator["heading_change_degrees"], 33.551, places=2)
        self.assertEqual(evaluator["last"]["elapsedTicks"], 314)
        self.assertAlmostEqual(evaluator["off_initial_heading_meters"], 0.2, places=2)

    def test_tampered_depth_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            cloned = Path(temporary) / "before"
            shutil.copytree(BEFORE, cloned)
            image = next(cloned.glob("*.pgm"))
            with image.open("ab") as handle:
                handle.write(b"bad")
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                MODULE.analyze(cloned, DRIVE, AFTER)

    def test_tampered_normals_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            cloned = Path(temporary) / "before"
            shutil.copytree(BEFORE, cloned)
            normals = next(cloned.glob("*.n16le"))
            with normals.open("ab") as handle:
                handle.write(b"bad")
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                MODULE.analyze(cloned, DRIVE, AFTER)

    def test_mismatched_camera_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            cloned = Path(temporary) / "after"
            shutil.copytree(AFTER, cloned)
            metadata = next(cloned.glob("sensor-*.json"))
            document = json.loads(metadata.read_text())
            document["camera_id"] = -1
            metadata.write_text(json.dumps(document))
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                MODULE.analyze(BEFORE, DRIVE, cloned)

    def test_mismatched_command_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            cloned = Path(temporary) / "drive"
            shutil.copytree(DRIVE, cloned)
            command = cloned / "command.json"
            document = json.loads(command.read_text())
            document["operation_id"] = "another-operation"
            command.write_text(json.dumps(document))
            with self.assertRaisesRegex(ValueError, "run/command/receipt identity mismatch"):
                MODULE.analyze(BEFORE, cloned, AFTER)

    def test_mismatched_fixture_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            cloned = Path(temporary) / "after"
            shutil.copytree(AFTER, cloned)
            run_file = cloned / "run.json"
            run = json.loads(run_file.read_text())
            run["fixture_id"] = "other-fixture"
            run_file.write_text(json.dumps(run))
            with self.assertRaisesRegex(ValueError, "run/command/receipt identity mismatch"):
                MODULE.analyze(BEFORE, DRIVE, cloned)

    def test_reversed_order_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "not ordered"):
            MODULE.analyze(AFTER, DRIVE, BEFORE)

    def test_zero_steer_does_not_imply_fixed_heading(self):
        drive = MODULE.load_drive(STEER_CHECK)
        self.assertEqual(drive["first"]["steeringWheels"], 2)
        self.assertEqual(drive["first"]["avgAbsSteerAngleRad"], 0)
        self.assertEqual(drive["last"]["avgAbsSteerAngleRad"], 0)
        self.assertAlmostEqual(drive["heading_change_degrees"], 8.346, places=2)
        self.assertEqual([sample["tick"] for sample in drive["timeline"]], [1, 60, 97])
        self.assertEqual([sample["mean_absolute_steer_angle_radians"] for sample in drive["timeline"]], [0, 0, 0])
        self.assertGreater(drive["timeline"][1]["heading_change_degrees"], 0)

    def test_zero_steer_yaw_reproduces(self):
        drive = MODULE.load_drive(STEER_REPRO)
        self.assertEqual([sample["mean_absolute_steer_angle_radians"] for sample in drive["timeline"]], [0, 0, 0])
        self.assertAlmostEqual(drive["heading_change_degrees"], 9.545, places=2)
        self.assertAlmostEqual(drive["translation_meters"], 0.502, places=2)

    def test_cross_body_comparison(self):
        sensor = MODULE.load_sensor(BLUE_SCAN)
        drive = MODULE.load_drive(BLUE_DRIVE)
        self.assertEqual(sensor["dominant_visible_normal_world"], (0.33, 0.69, -0.64))
        self.assertAlmostEqual(sensor["dominant_normal_projected_offset_meters"], 2.276, places=2)
        self.assertEqual([sample["mean_absolute_steer_angle_radians"] for sample in drive["timeline"]], [0, 0, 0])
        self.assertAlmostEqual(drive["heading_change_degrees"], 4.449, places=2)

    def test_blue_sensor_action_sensor_loop(self):
        report = MODULE.analyze(BLUE_SCAN, BLUE_DRIVE, BLUE_AFTER)
        sensor = report["sensor_evidence"]
        self.assertAlmostEqual(sensor["camera_heading_change_degrees"], 4.458, places=2)
        self.assertLess(sensor["camera_up_change_degrees"], 0.1)
        self.assertAlmostEqual(sensor["camera_translation_meters"], 0.566, places=2)
        self.assertEqual(sensor["before"]["dominant_normal_projected_offset_meters"],
                         sensor["after"]["dominant_normal_projected_offset_meters"])

    def test_matched_drive_comparison(self):
        report = MODULE.compare_drives([STEER_CHECK, STEER_REPRO, BLUE_DRIVE])
        self.assertTrue(report["matched_requested_settings"])
        self.assertTrue(report["matched_plugin_build"])
        self.assertEqual(len(report["drives"]), 3)
        self.assertEqual([drive["heading_change_degrees"] for drive in report["drives"]],
                         [8.346, 9.545, 4.449])


if __name__ == "__main__":
    unittest.main()
