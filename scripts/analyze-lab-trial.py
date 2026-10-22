#!/usr/bin/env python3
"""Compare two receipt-backed sensor frames around one bounded lab drive.

The sensor section contains only camera/depth evidence. The evaluator section
contains actuator telemetry and must not be presented as agent perception.
"""

import argparse
from collections import Counter
import hashlib
import json
import math
import re
import statistics
import struct
from pathlib import Path


VECTOR = re.compile(r"\b(position|forward)=\[([^]]+)\]")
NUMBER = re.compile(r"\b(elapsedTicks|displacement|speed|forwardSpeed|wheels|steeringWheels|avgAbsSteerAngleRad)=([-\d.]+)")


def verified_bytes(path, expected_hash):
    data = path.read_bytes()
    actual = hashlib.sha256(data).hexdigest()
    if expected_hash != actual:
        raise ValueError(f"SHA-256 mismatch: {path}: expected {expected_hash}, got {actual}")
    return data


def angle_degrees(a, b):
    dot = sum(x * y for x, y in zip(a, b))
    norms = math.sqrt(sum(x * x for x in a) * sum(y * y for y in b))
    if not norms:
        raise ValueError("zero-length direction")
    return math.degrees(math.acos(max(-1, min(1, dot / norms))))


def distance(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]]


def unit(vector):
    norm = math.sqrt(sum(value * value for value in vector))
    if not norm:
        raise ValueError("zero-length vector")
    return [value / norm for value in vector]


def load_sensor(run_dir):
    receipt = json.loads((run_dir / "terminal-receipt.json").read_text())["receipt"]
    artifact = receipt.get("sensor_artifact") or {}
    if not receipt.get("clean_shutdown") or not receipt.get("sensor_artifact_valid"):
        raise ValueError(f"missing valid terminal sensor receipt: {run_dir}")
    metadata = json.loads(verified_bytes(run_dir / Path(artifact["metadata_path"]).name,
                                         artifact["metadata_sha256"]))
    if metadata.get("operation_id") != receipt.get("lab_operation_id"):
        raise ValueError(f"sensor operation mismatch: {run_dir}")
    width, height = metadata["width"], metadata["height"]
    image = verified_bytes(run_dir / Path(artifact["depth_path"]).name,
                           artifact["depth_sha256"])
    header = f"P5\n{width} {height}\n65535\n".encode()
    if not image.startswith(header) or len(image) != len(header) + 2 * width * height:
        raise ValueError(f"unexpected PGM encoding: {run_dir}")
    values = struct.unpack(f">{width * height}H", image[len(header):])
    center = [values[y * width + width // 2] for y in range(height // 2, height)]
    normal_bytes = verified_bytes(run_dir / Path(artifact["surface_normal_path"]).name,
                                  artifact["surface_normal_sha256"])
    if len(normal_bytes) != 6 * width * height:
        raise ValueError(f"unexpected normal encoding: {run_dir}")
    components = struct.unpack(f"<{3 * width * height}h", normal_bytes)
    normals = [tuple(round(components[i + axis] / 32767, 2) for axis in range(3))
               for i in range(0, len(components), 3) if values[i // 3]]
    dominant_normal, dominant_hits = Counter(normals).most_common(1)[0] if normals else (None, 0)
    projected_distances = []
    if dominant_normal:
        normal = unit(dominant_normal)
        forward, up = metadata["forward"], metadata["up"]
        right = unit(cross(forward, up))
        horizontal_scale = math.tan(math.radians(metadata["horizontal_fov_degrees"]) / 2)
        vertical_scale = horizontal_scale * height / width
        for index, encoded_depth in enumerate(values):
            if not encoded_depth:
                continue
            hit_normal = tuple(round(components[3 * index + axis] / 32767, 2) for axis in range(3))
            if hit_normal != dominant_normal:
                continue
            x, y = index % width, index // width
            horizontal = 2 * (x + 0.5) / width - 1
            vertical = 1 - 2 * (y + 0.5) / height
            ray = unit([forward[axis] + right[axis] * horizontal * horizontal_scale +
                        up[axis] * vertical * vertical_scale for axis in range(3)])
            depth = encoded_depth * metadata["range_meters"] / 65535
            projected_distances.append(abs(depth * sum(a * b for a, b in zip(normal, ray))))
    plane_offset = statistics.median(projected_distances) if projected_distances else None
    plane_mad = (statistics.median(abs(value - plane_offset) for value in projected_distances)
                 if projected_distances else None)
    return {
        "run_id": receipt["lab_run_id"], "operation_id": receipt["lab_operation_id"],
        "camera_id": metadata["camera_id"], "origin_world": metadata["origin"],
        "forward_world": metadata["forward"], "up_world": metadata["up"],
        "dimensions": [width, height],
        "fov_degrees": metadata["horizontal_fov_degrees"],
        "range_meters": metadata["range_meters"],
        "hit_pixels": sum(value != 0 for value in values),
        "dominant_visible_normal_world": dominant_normal,
        "dominant_visible_normal_hits": dominant_hits,
        "dominant_normal_to_camera_up_degrees": round(angle_degrees(dominant_normal, metadata["up"]), 3)
        if dominant_normal else None,
        "dominant_normal_projected_offset_meters": round(plane_offset, 3) if plane_offset is not None else None,
        "dominant_normal_offset_mad_meters": round(plane_mad, 3) if plane_mad is not None else None,
        "center_lower_depth_meters": [round(value * metadata["range_meters"] / 65535, 3)
                                      if value else None for value in center],
        "depth_sha256": artifact["depth_sha256"],
        "metadata_sha256": artifact["metadata_sha256"],
    }


def load_drive(run_dir):
    run_identity(run_dir)
    receipt = json.loads((run_dir / "terminal-receipt.json").read_text())["receipt"]
    command = json.loads((run_dir / "command.json").read_text())
    if not receipt.get("clean_shutdown") or not receipt.get("lab_operation_terminal"):
        raise ValueError(f"missing terminal drive receipt: {run_dir}")
    if command.get("operation_id") != receipt.get("lab_operation_id") or command.get("action") != "drive-distance":
        raise ValueError(f"drive command/receipt mismatch: {run_dir}")
    events = receipt.get("lab_operation_events") or []
    samples = []
    for event in events:
        if "type=server-control" not in event or "elapsedTicks=" not in event:
            continue
        sample = {key: float(value) for key, value in NUMBER.findall(event)}
        sample.update({key: [float(v) for v in value.split(",")]
                       for key, value in VECTOR.findall(event)})
        sample["status"] = re.search(r"\bstatus=(\w+)", event).group(1)
        samples.append(sample)
    if len(samples) < 2 or "forward" not in samples[0] or "forward" not in samples[-1]:
        raise ValueError(f"insufficient drive telemetry: {run_dir}")
    delta = [b - a for a, b in zip(samples[0]["position"], samples[-1]["position"])]
    initial_forward = samples[0]["forward"]
    forward_norm = math.sqrt(sum(value * value for value in initial_forward))
    along = sum(value * direction for value, direction in zip(delta, initial_forward)) / forward_norm
    lateral = math.sqrt(max(0, sum(value * value for value in delta) - along * along))
    timeline = []
    for sample in samples:
        relative = [value - start for value, start in zip(sample["position"], samples[0]["position"])]
        longitudinal = sum(value * direction for value, direction in zip(relative, initial_forward)) / forward_norm
        cross_track = math.sqrt(max(0, sum(value * value for value in relative) - longitudinal * longitudinal))
        timeline.append({
            "tick": int(sample["elapsedTicks"]), "status": sample["status"],
            "heading_change_degrees": round(angle_degrees(initial_forward, sample["forward"]), 3),
            "along_initial_heading_meters": round(longitudinal, 3),
            "off_initial_heading_meters": round(cross_track, 3),
            "mean_absolute_steer_angle_radians": sample.get("avgAbsSteerAngleRad"),
            "speed_mps": sample.get("speed"),
        })
    return {
        "run_id": receipt["lab_run_id"], "operation_id": receipt["lab_operation_id"],
        "requested_command": command,
        "status": receipt["lab_operation_status"], "first": samples[0], "last": samples[-1],
        "sample_count": len(samples), "timeline": timeline,
        "heading_change_degrees": round(angle_degrees(samples[0]["forward"], samples[-1]["forward"]), 3),
        "translation_meters": round(distance(samples[0]["position"], samples[-1]["position"]), 3),
        "along_initial_heading_meters": round(along, 3),
        "off_initial_heading_meters": round(lateral, 3),
    }


def run_identity(run_dir):
    run = json.loads((run_dir / "run.json").read_text())
    command = json.loads((run_dir / "command.json").read_text())
    receipt = json.loads((run_dir / "terminal-receipt.json").read_text())["receipt"]
    if (run.get("run_id") != receipt.get("lab_run_id") or
            run.get("command", {}).get("operation_id") != receipt.get("lab_operation_id") or
            command.get("operation_id") != receipt.get("lab_operation_id") or
            run.get("fixture_id") != command.get("fixture_id")):
        raise ValueError(f"run/command/receipt identity mismatch: {run_dir}")
    return run


def analyze(before_dir, drive_dir, after_dir):
    runs = [run_identity(path) for path in (before_dir, drive_dir, after_dir)]
    if len({(run["instance"], run["fixture_id"]) for run in runs}) != 1:
        raise ValueError("runs belong to different instances or fixtures")
    if not (runs[0]["started_at"] < runs[1]["started_at"] < runs[2]["started_at"]):
        raise ValueError("runs are not ordered before/drive/after")
    if [run["command"]["action"] for run in runs] != ["sensor-frame", "drive-distance", "sensor-frame"]:
        raise ValueError("expected sensor/drive/sensor run sequence")
    before, after = load_sensor(before_dir), load_sensor(after_dir)
    drive = load_drive(drive_dir)
    if before["camera_id"] != after["camera_id"]:
        raise ValueError("cannot compare different cameras")
    return {
        "schema": "voidwright.lab-trial-analysis.v1",
        "sensor_evidence": {
            "before": before, "after": after,
            "camera_translation_meters": round(distance(before["origin_world"], after["origin_world"]), 3),
            "camera_heading_change_degrees": round(angle_degrees(before["forward_world"], after["forward_world"]), 3),
            "camera_up_change_degrees": round(angle_degrees(before["up_world"], after["up_world"]), 3),
            "hit_pixel_delta": after["hit_pixels"] - before["hit_pixels"],
        },
        "privileged_evaluator_telemetry": drive,
        "limits": ["Depth returns do not prove free space where rays missed.",
                   "Actuator completion is not target arrival or obstacle avoidance.",
                   "The staged command file is matched by operation ID but is not independent proof of in-engine execution.",
                   "The most frequent visible normal is not a verified floor label.",
                   "Dominant-normal offset is a ray/normal projection, not a collision or vehicle-clearance measurement.",
                   "Heading deviation alone does not establish its mechanical cause."],
    }


def compare_drives(run_dirs):
    runs = [run_identity(path) for path in run_dirs]
    if len({(run["instance"], run["fixture_id"]) for run in runs}) != 1:
        raise ValueError("drive runs belong to different instances or fixtures")
    drives = [load_drive(path) for path in run_dirs]
    receipts = [json.loads((path / "terminal-receipt.json").read_text())["receipt"]
                for path in run_dirs]
    requests = [drive["requested_command"] for drive in drives]
    if any(request.get("action") != "drive-distance" for request in requests):
        raise ValueError("compare-drives accepts drive-distance runs only")
    settings = {(request["distance_meters"], request["max_speed"], request["timeout_ticks"])
                for request in requests}
    return {
        "schema": "voidwright.drive-comparison.v1",
        "instance": runs[0]["instance"], "fixture_id": runs[0]["fixture_id"],
        "matched_requested_settings": len(settings) == 1,
        "matched_plugin_build": len({receipt.get("staged_plugin_tree_sha256") for receipt in receipts}) == 1,
        "drives": [{
            "run_id": drive["run_id"], "controller_id": request["controller_id"],
            "plugin_tree_sha256": receipt.get("staged_plugin_tree_sha256"),
            "requested_distance_meters": request["distance_meters"],
            "requested_max_speed_mps": request["max_speed"],
            "status": drive["status"], "elapsed_ticks": int(drive["last"]["elapsedTicks"]),
            "actual_displacement_meters": drive["last"]["displacement"],
            "heading_change_degrees": drive["heading_change_degrees"],
            "off_initial_heading_meters": drive["off_initial_heading_meters"],
            "steering_wheels": drive["last"].get("steeringWheels"),
            "last_mean_absolute_steer_angle_radians": drive["last"].get("avgAbsSteerAngleRad"),
        } for drive, request, receipt in zip(drives, requests, receipts)],
        "limits": ["Different vehicles or positions do not isolate a causal mechanism.",
                   "Sparse terminal telemetry does not measure wheel contact or suspension forces."],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("runs", type=Path, nargs="+",
                        help="one drive run with --drive-only, otherwise before/drive/after runs")
    parser.add_argument("--drive-only", action="store_true",
                        help="report one drive receipt without claiming a sensor comparison")
    parser.add_argument("--compare-drives", action="store_true",
                        help="compare at least two drive receipts from one disposable fixture")
    args = parser.parse_args()
    if args.drive_only and args.compare_drives:
        parser.error("choose one report mode")
    if args.compare_drives:
        if len(args.runs) < 2:
            parser.error("--compare-drives requires at least two drive runs")
        report = compare_drives(args.runs)
    elif args.drive_only:
        if len(args.runs) != 1:
            parser.error("--drive-only accepts exactly one drive run path")
        report = {"schema": "voidwright.drive-analysis.v1",
                  "privileged_evaluator_telemetry": load_drive(args.runs[0])}
    else:
        if len(args.runs) != 3:
            parser.error("provide before, drive, and after runs")
        report = analyze(*args.runs)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
