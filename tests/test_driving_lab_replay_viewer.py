import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "render-driving-lab-replay.py"
SPEC = importlib.util.spec_from_file_location("driving_lab_replay_viewer", SCRIPT)
VIEWER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VIEWER)
DEFAULT = object()


def section(status="unavailable", value=None, reason="not_sampled"):
    if status == "observed":
        return {"status": status, "reason": None, "value": value}
    if status == "partial":
        return {"status": status, "reason": reason, "value": value}
    return {"status": "unavailable", "reason": reason, "value": None}


def record(sequence, *, run="run-a", session="session-a", stream="grid-42", grid=42,
           pose="observed", dropped=0, timestamp=None, tick_step=60,
           speed=1.1, input_source=None, input_values=None, time_uncertainty=None,
           simtime=DEFAULT):
    physics = {"velocityX": 1.0, "velocityY": None, "velocityZ": 0.2,
               "speedMetersPerSecond": speed, "simulationRatio": None,
               "physicsStepCount": sequence}
    if speed is None:
        physics.pop("speedMetersPerSecond")
    return {
        "schemaVersion": 1, "runId": run, "sessionId": session, "streamId": stream,
        "source": "offline-test", "sequence": sequence,
        "capturedAtUtc": timestamp or f"2026-10-19T12:00:{sequence:02d}Z",
        "clockDomain": "server-simulation", "simulationTick": sequence * tick_step,
        "simulationTimeSeconds": sequence if simtime is DEFAULT else simtime, "gridEntityId": grid,
        "droppedRecordsTotal": dropped,
        **({"simulationTimeUncertaintySeconds": time_uncertainty} if time_uncertainty is not None else {}),
        "humanInput": section("observed", {"source": input_source, **(input_values or {})}) if input_values else section(),
        "actuators": section("observed", {"ownership": "observed_only_no_control", "items": [
            {"entityId": 4201, "kind": "wheel", "enabled": True,
             "overrideActive": True, "overrideValue": 0.1 * sequence, "brakeEnabled": False}]}),
        "physics": section("partial", physics, "partial_physics"),
        "pose": (section("observed", {"positionX": float(sequence), "positionY": 0.0,
                                       "positionZ": float(sequence) * 2, "rightX": None, "rightY": None,
                                       "rightZ": None, "upX": None, "upY": None, "upZ": None,
                                       "forwardX": 1.0, "forwardY": 0.0, "forwardZ": 0.0})
                 if pose == "observed" else section("unavailable", reason="pose_not_available")),
        "gridState": section(), "softwareGeometry": section("partial", {
            "coverage": "none", "gridCount": None, "blockCount": None, "terrainIncluded": False,
            "unrelatedEntitiesIncluded": False, "uncertainty": "geometry_not_sampled"}, "geometry_partial"),
        "visualFrame": section(reason="framebuffer_unavailable"),
    }


class DrivingLabReplayViewerTests(unittest.TestCase):
    def capture_file(self, records):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        path = Path(temp.name) / "capture.jsonl"
        path.write_text("".join(json.dumps(item) + "\n" for item in records), encoding="utf-8")
        return path

    def test_sequence_gaps_break_trajectory_and_are_reported(self):
        records = VIEWER.load_capture(self.capture_file([record(1), record(3), record(4)]))
        self.assertEqual([item["_missingBefore"] for item in records], [0, 1, 0])
        document = VIEWER.render(records, "capture.jsonl")
        self.assertIn("1 missing sequence numbers", document)
        self.assertIn("gap 1", document)
        self.assertIn("paths are broken at gaps", document)

    def test_missing_pose_breaks_path_without_fabrication(self):
        records = VIEWER.load_capture(self.capture_file([record(1), record(2, pose="unavailable"), record(3)]))
        document = VIEWER.render(records, "capture.jsonl")
        self.assertIn("pose_not_available", document)
        self.assertIn("Both stop at missing pose samples", document)
        top_down = document.split("Observed top-down chassis trajectory", 1)[1].split("Observed 3D chassis trajectory", 1)[0]
        self.assertNotIn("<polyline", top_down)

    def test_partial_channels_and_drop_counts_are_visible(self):
        records = VIEWER.load_capture(self.capture_file([record(1, dropped=0), record(2, dropped=2), record(3, dropped=2)]))
        document = VIEWER.render(records, "capture.jsonl")
        self.assertIn("Partial records", document)
        self.assertIn("partial_physics", document)
        self.assertIn("cumulative maximum 2", document)
        self.assertIn("Drop delta", document)
        self.assertIn("channel meaning unspecified", document)
        self.assertIn("Recorded clock domain(s)", document)

    def test_missing_drop_counter_is_unknown_not_zero_in_single_and_comparison_views(self):
        source = VIEWER.load_capture(self.capture_file([record(1, dropped=None), record(2, dropped=None)]))
        single = VIEWER.render(source, "capture.jsonl")
        self.assertIn("unavailable (producer counter not reported)", single)
        self.assertIn("No sequence gaps observed; producer drop counter unavailable.", single)
        attempt = VIEWER.load_capture(self.capture_file([
            record(1, run="attempt", dropped=None), record(2, run="attempt", dropped=None)]))
        comparison = VIEWER.render_comparison(source, attempt, "demo.jsonl", "attempt.jsonl",
                                              "relative-simulation-time")
        self.assertIn("No sequence gaps observed; producer drop counter unavailable.", comparison)
        self.assertIn("unavailable (producer counter not reported)", comparison)

    def test_identity_mismatch_is_rejected(self):
        path = self.capture_file([record(1), record(2, grid=43)])
        with self.assertRaisesRegex(ValueError, "identity mismatch"):
            VIEWER.load_capture(path)

    def test_duplicate_actuator_identity_is_rejected_instead_of_merged(self):
        duplicate = record(1)
        duplicate["actuators"]["value"]["items"].append(
            dict(duplicate["actuators"]["value"]["items"][0], overrideValue=-0.7))
        with self.assertRaisesRegex(ValueError, "duplicate actuator observation identity"):
            VIEWER.load_capture(self.capture_file([duplicate]))

    def test_duplicate_json_keys_are_rejected_at_top_level_and_nested(self):
        encoded = json.dumps(record(1))
        top_level = encoded.replace('"sequence": 1', '"sequence": 1, "sequence": 2', 1)
        nested = encoded.replace('"speedMetersPerSecond": 1.1',
                                  '"speedMetersPerSecond": 1.1, "speedMetersPerSecond": 0.0', 1)
        for line in (top_level, nested):
            with self.subTest(line=line[:80]):
                temp = tempfile.TemporaryDirectory()
                self.addCleanup(temp.cleanup)
                path = Path(temp.name) / "ambiguous.jsonl"
                path.write_text(line + "\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "duplicate JSON object key"):
                    VIEWER.load_capture(path)

    def test_comparison_keeps_distinct_identities_and_control_sources(self):
        human = VIEWER.load_capture(self.capture_file([
            record(1, run="demo", session="human-session", stream="input", grid=42,
                   input_source="device-observation", input_values={"throttle": 0.4}),
            record(2, run="demo", session="human-session", stream="input", grid=42,
                   input_source="device-observation", input_values={"throttle": 0.5}),
        ]))
        attempt = VIEWER.load_capture(self.capture_file([
            record(1, run="attempt", session="server-session", stream="vehicle", grid=84),
            record(2, run="attempt", session="server-session", stream="vehicle", grid=84),
        ]))
        document = VIEWER.render_comparison(human, attempt, "demo.jsonl", "attempt.jsonl",
                                            "relative-simulation-time")
        for identity in ("human-session", "server-session", "device-observation", "Human demonstration", "Voidwright attempt"):
            self.assertIn(identity, document)
        self.assertIn("Explicitly reported input channels", document)
        self.assertIn("reported source=device-observation", document)
        self.assertIn("Human demonstration: observed actuator values, not aligned across streams", document)
        self.assertIn("Voidwright attempt: observed actuator values, not aligned across streams", document)
        self.assertIn("overrideValue (channel meaning unspecified)", document)
        self.assertIn("Sequence gaps and producer drops", document)

    def test_explicit_alignment_offsets_and_uncertainty_are_reported(self):
        human = VIEWER.load_capture(self.capture_file([
            record(1, simtime=10.0, time_uncertainty=0.01),
            record(2, simtime=11.0, time_uncertainty=0.01),
        ]))
        attempt = VIEWER.load_capture(self.capture_file([
            record(1, run="attempt", simtime=4.0, time_uncertainty=0.02),
            record(2, run="attempt", simtime=5.0, time_uncertainty=0.02),
        ]))
        document = VIEWER.render_comparison(human, attempt, "human.jsonl", "attempt.jsonl",
            "explicit-start-offset", -3.0, 2.5, 0.1, 0.2)
        self.assertIn("Human start offset -3 s", document)
        self.assertIn("attempt start offset 2.5 s", document)
        self.assertIn("at most 0.12 s including start-offset uncertainty", document)
        self.assertIn("at most 0.24 s including start-offset uncertainty", document)
        self.assertNotIn("captured UTC", document)

    def test_comparison_api_rejects_same_stream_and_invalid_alignment_metadata(self):
        stream = VIEWER.load_capture(self.capture_file([record(1), record(2)]))
        other = VIEWER.load_capture(self.capture_file([
            record(1, run="attempt"), record(2, run="attempt")]))
        with self.assertRaisesRegex(ValueError, "same run/session/stream/grid identity"):
            VIEWER.render_comparison(stream, stream, "same.jsonl", "same.jsonl", "relative-simulation-time")
        with self.assertRaisesRegex(ValueError, "finite offsets and uncertainty"):
            VIEWER.render_comparison(stream, other, "demo.jsonl", "attempt.jsonl", "explicit-start-offset",
                                     float("nan"), 0.0, 0.1, 0.1)
        with self.assertRaisesRegex(ValueError, "nonnegative"):
            VIEWER.render_comparison(stream, other, "demo.jsonl", "attempt.jsonl", "explicit-start-offset",
                                     0.0, 0.0, -0.1, 0.1)
        with self.assertRaisesRegex(ValueError, "require explicit-start-offset"):
            VIEWER.render_comparison(stream, other, "demo.jsonl", "attempt.jsonl",
                                     "relative-simulation-time", 1.0)

    def test_relative_simulation_time_handles_different_tick_rates(self):
        slow_ticks = VIEWER.load_capture(self.capture_file([
            record(1, simtime=7.0, tick_step=120), record(2, simtime=8.0, tick_step=120),
        ]))
        fast_ticks = VIEWER.load_capture(self.capture_file([
            record(1, run="attempt", simtime=100.0, tick_step=30),
            record(2, run="attempt", simtime=101.0, tick_step=30),
        ]))
        human_time = VIEWER.aligned_times(slow_ticks, "relative-simulation-time")
        attempt_time = VIEWER.aligned_times(fast_ticks, "relative-simulation-time")
        self.assertEqual(human_time, [0.0, 1.0])
        self.assertEqual(attempt_time, [0.0, 1.0])

    def test_capture_rejects_backward_simulation_clock_or_tick(self):
        with self.assertRaisesRegex(ValueError, "simulationTick moved backwards"):
            VIEWER.load_capture(self.capture_file([
                record(1, simtime=1.0, tick_step=60), record(2, simtime=2.0, tick_step=20)]))
        with self.assertRaisesRegex(ValueError, "simulationTimeSeconds moved backwards"):
            VIEWER.load_capture(self.capture_file([
                record(1, simtime=2.0), record(2, simtime=1.0)]))

    def test_input_source_changes_are_not_joined_into_one_series(self):
        human = VIEWER.load_capture(self.capture_file([
            record(1, input_source="device", input_values={"steering": 0.2}),
            record(2, input_source="autopilot", input_values={"steering": -0.2}),
        ]))
        attempt = VIEWER.load_capture(self.capture_file([
            record(1, run="attempt"), record(2, run="attempt"),
        ]))
        document = VIEWER.render_comparison(human, attempt, "demo.jsonl", "attempt.jsonl",
                                            "relative-simulation-time")
        self.assertIn("reported source=device · humanInput.steering", document)
        self.assertIn("reported source=autopilot · humanInput.steering", document)

    def test_comparison_gaps_and_missing_values_are_not_interpolated(self):
        records = VIEWER.load_capture(self.capture_file([
            record(1, simtime=0.0), record(3, simtime=2.0),
            record(4, simtime=None), record(5, simtime=4.0, speed=None),
        ]))
        times = VIEWER.aligned_times(records, "relative-simulation-time")
        chart = VIEWER.comparison_svg("speed", [("demo", records, times, "#000000",
            lambda r: (VIEWER.section_values(r, "physics") or {}).get("speedMetersPerSecond"))], "m/s")
        gap_table = VIEWER.comparison_gap_table("demo", records)
        self.assertIn("sequence gaps", chart)
        self.assertIn("<td>3</td><td>1</td>", gap_table)
        self.assertEqual(chart.count("<circle"), 2)
        self.assertNotIn("<polyline", chart)
        self.assertEqual(times, [0.0, 2.0, None, 4.0])

    def test_comparison_rejects_missing_alignment_and_unaligned_start(self):
        records = VIEWER.load_capture(self.capture_file([
            record(1, simtime=None), record(2, simtime=2.0),
        ]))
        with self.assertRaisesRegex(ValueError, "first record.*simulationTimeSeconds"):
            VIEWER.aligned_times(records, "relative-simulation-time")
        with self.assertRaisesRegex(ValueError, "alignment policy"):
            VIEWER.aligned_times(records, "wall-clock-guessed")

    def test_cli_writes_a_bounded_static_export(self):
        source = self.capture_file([record(1), record(2)])
        output = source.with_suffix(".html")
        original = sys.argv
        self.addCleanup(setattr, sys, "argv", original)
        sys.argv = [str(SCRIPT), str(source), str(output)]
        self.assertEqual(VIEWER.main(), 0)
        self.assertLessEqual(output.stat().st_size, VIEWER.MAX_OUTPUT_BYTES)
        content = output.read_text(encoding="utf-8")
        self.assertTrue(content.startswith("<!doctype html>"))
        self.assertIn("no game connection, execution, interpolation, or control path", content)

    def test_cli_comparison_requires_and_uses_explicit_alignment(self):
        human = self.capture_file([record(1), record(2)])
        attempt = self.capture_file([record(1, run="attempt"), record(2, run="attempt")])
        output = human.with_name("comparison.html")
        original = sys.argv
        self.addCleanup(setattr, sys, "argv", original)
        sys.argv = [str(SCRIPT), str(output), "--compare-human", str(human),
                    "--compare-attempt", str(attempt), "--alignment", "relative-simulation-time"]
        self.assertEqual(VIEWER.main(), 0)
        content = output.read_text(encoding="utf-8")
        self.assertIn("relative-simulation-time", content)
        self.assertIn("timing uncertainty: unknown", content)
        self.assertLessEqual(output.stat().st_size, VIEWER.MAX_OUTPUT_BYTES)


if __name__ == "__main__":
    unittest.main()
