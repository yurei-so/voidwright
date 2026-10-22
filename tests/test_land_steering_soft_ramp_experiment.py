"""Checks the archived paired engine evidence for the LAND steering experiment."""

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "artifacts/land-steering-soft-ramp-20261018"


class LandSoftRampExperimentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((EVIDENCE / "experiment-manifest.json").read_text())
        cls.comparison = json.loads(
            (EVIDENCE / "matched-clear-deadband-comparison.json").read_text()
        )

    def test_baseline_and_candidate_complete_same_route_safely(self):
        results = self.manifest["route_results"]
        baseline = results["baseline"]
        candidate = results["soft-ramp"]
        self.assertTrue(baseline["route_complete"])
        self.assertTrue(candidate["route_complete"])
        self.assertEqual(baseline["completed_waypoints"], 4)
        self.assertEqual(candidate["completed_waypoints"], 4)
        self.assertEqual(self.manifest["arrival_radius_m"], 2.5)
        self.assertEqual(baseline["operation_counts_by_waypoint"], {
            "Obstacle Empty Space": 6,
            "North wall bypass A": 3,
            "North-east corridor B": 4,
            "Green Zone": 9,
        })
        self.assertEqual(candidate["operation_counts_by_waypoint"], {
            "Obstacle Empty Space": 6,
            "North wall bypass A": 3,
            "North-east corridor B": 4,
            "Green Zone": 8,
        })

        for result in (baseline, candidate):
            self.assertTrue(result["all_fixture_edits_zero"])
            self.assertTrue(result["all_clean_shutdowns"])
            self.assertTrue(result["all_plugin_hashes_match"])
            self.assertEqual(result["max_self_grid_hits"], 0)
            self.assertLessEqual(result["max_reported_speed_mps"], self.manifest["speed_cap_mps"])
            self.assertEqual(len(result["waypoint_results"]), 4)
            for waypoint in result["waypoint_results"]:
                self.assertLessEqual(waypoint["completion_distance_m"], waypoint["radius_m"])

        baseline_green = baseline["waypoint_results"][-1]["completion_distance_m"]
        candidate_green = candidate["waypoint_results"][-1]["completion_distance_m"]
        self.assertAlmostEqual(baseline_green, 2.265, places=3)
        self.assertAlmostEqual(candidate_green, baseline_green, places=3)

    def test_candidate_reduces_measured_clear_deadband_steering_variation(self):
        groups = self.comparison["groups"]
        self.assertEqual(set(groups), {
            "baseline_attempt4",
            "baseline_attempt5",
            "soft-ramp_attempt4",
            "soft-ramp_attempt5",
        })
        for attempt in (4, 5):
            baseline = groups[f"baseline_attempt{attempt}"]
            candidate = groups[f"soft-ramp_attempt{attempt}"]
            self.assertGreater(baseline["neutral_to_active_edges"], 0)
            self.assertEqual(candidate["neutral_to_active_edges"], 0)
            self.assertLess(candidate["command_total_variation"], baseline["command_total_variation"] * 0.05)
            self.assertLess(candidate["wheel_total_variation"], baseline["wheel_total_variation"] * 0.06)
            self.assertLess(candidate["wheel_max_2tick_step_rad"], baseline["wheel_max_2tick_step_rad"] * 0.06)
            self.assertLess(
                abs(candidate["mean_abs_heading_rad"] - baseline["mean_abs_heading_rad"]),
                0.003,
            )

    def test_candidate_source_is_limited_to_the_plugin_file(self):
        sources = self.manifest["plugin_sources"]
        baseline_files = sources["baseline"]["files"]
        candidate_files = sources["soft-ramp"]["files"]
        self.assertEqual(set(baseline_files), set(candidate_files))
        changed = {name for name in baseline_files if baseline_files[name] != candidate_files[name]}
        self.assertEqual(changed, {"Plugin.cs"})


if __name__ == "__main__":
    unittest.main()
