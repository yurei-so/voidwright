#!/usr/bin/env python3
"""Render an evidence-labeled terrain, route, front-view, and controller recap.

The route panels use evaluator COM telemetry. The terrain panel uses a separate
receipt-validated sensor snapshot. The front panel uses exact HoloView frames
captured during the final arrival recheck; it is not controller input or native
framebuffer capture.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.image as mpimg
import matplotlib.pyplot as plt
from matplotlib.animation import FuncAnimation, FFMpegWriter
from matplotlib.patches import Circle


TARGET_DEFAULT = [7821.479994, 869.924461, 9204.985096]
APPROACH_ID = "1f9cc8a8642edef11ffe7b4303c1f19e"
ARRIVAL_ID = "9105e04da1f303676e314f50b6a33177"
FIXTURE_ID = "bebd44898770362960c21201ebf851bf"
TICKS_PER_SECOND = 60.0


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def vector(value: str | None) -> list[float] | None:
    if not value or not (value.startswith("[") and value.endswith("]")):
        return None
    try:
        return [float(item) for item in value[1:-1].split(",")]
    except ValueError:
        return None


def fields(event: str) -> dict[str, str]:
    return {match.group(1): match.group(2) for match in
            re.finditer(r"(?:^| )(\w+)=((?:\[[^\]]*\])|(?:[^ ]+))", event)}


def load_receipt(path: Path, expected_operation: str) -> dict:
    raw = path.read_bytes()
    wrapper = json.loads(raw)
    receipt = wrapper.get("receipt", {})
    if receipt.get("lab_operation_id") != expected_operation:
        raise ValueError(f"unexpected operation in {path}")
    if receipt.get("lab_operation_status") != "complete":
        raise ValueError(f"operation is not complete in {path}")
    if receipt.get("passed") is not True or receipt.get("clean_shutdown") is not True:
        raise ValueError(f"terminal receipt did not prove a clean pass: {path}")
    return wrapper


def log_motion(path: Path, operation_id: str) -> list[dict]:
    samples = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if f"operation={operation_id}" not in line or "action=motion-telemetry" not in line:
            continue
        parsed = fields(line)
        position = vector(parsed.get("centerOfMassWorld"))
        if position is None or parsed.get("elapsedTicks") is None:
            continue
        try:
            samples.append({
                "ticks": int(parsed["elapsedTicks"]),
                "seconds": float(parsed.get("elapsedSimulationSeconds", 0)),
                "position": position,
                "target_distance": float(parsed.get("targetDistanceComPlanar", "nan")),
                "sample": parsed.get("sample", "running"),
            })
        except ValueError:
            continue
    samples.sort(key=lambda item: item["ticks"])
    deduplicated = {sample["ticks"]: sample for sample in samples}
    result = list(deduplicated.values())
    if len(result) < 2:
        raise ValueError(f"not enough motion samples for {operation_id} in {path}")
    return result


def terminal_position(wrapper: dict) -> list[float]:
    for event in wrapper["receipt"].get("lab_operation_events", []):
        if "type=server-control" not in event or "status=complete" not in event:
            continue
        parsed = fields(event)
        position = vector(parsed.get("comPosition")) or vector(parsed.get("position"))
        if position is not None:
            return position
    raise ValueError("terminal receipt contains no completed COM position")


def arrival_inputs(wrapper: dict) -> dict[str, str]:
    candidates = [event for event in wrapper["receipt"].get("lab_operation_events", [])
                  if "type=server-control" in event and "status=complete" in event]
    if not candidates:
        raise ValueError("arrival receipt has no completed controller event")
    return fields(candidates[-1])


def normalize(v: list[float]) -> list[float]:
    size = math.sqrt(sum(x * x for x in v))
    if size <= 1e-12:
        raise ValueError("cannot normalize zero vector")
    return [x / size for x in v]


def dot(a: list[float], b: list[float]) -> float:
    return sum(a[i] * b[i] for i in range(3))


def cross(a: list[float], b: list[float]) -> list[float]:
    return [a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]]


def gravity_basis(up_value: list[float]) -> dict[str, list[float]]:
    up = normalize(up_value)
    reference = [0.0, 0.0, 1.0]
    if abs(dot(reference, up)) > 0.95:
        reference = [0.0, 1.0, 0.0]
    north = normalize([reference[i] - dot(reference, up) * up[i] for i in range(3)])
    east = normalize(cross(north, up))
    north = normalize(cross(up, east))
    return {"east": east, "north": north, "up": up}


def load_depth_points(folder: Path) -> tuple[list[list[float]], dict, str]:
    receipt_wrapper = json.loads((folder / "terminal-receipt.json").read_text())
    receipt = receipt_wrapper.get("receipt", {})
    artifact = receipt.get("sensor_artifact")
    if receipt.get("passed") is not True or receipt.get("sensor_artifact_valid") is not True or not artifact:
        raise ValueError("terrain snapshot lacks a passing artifact receipt")
    metadata_path = folder / Path(artifact["metadata_path"]).name
    depth_path = folder / Path(artifact["depth_path"]).name
    metadata_bytes, depth_bytes = metadata_path.read_bytes(), depth_path.read_bytes()
    if sha256(metadata_bytes) != artifact["metadata_sha256"] or sha256(depth_bytes) != artifact["depth_sha256"]:
        raise ValueError("depth snapshot SHA-256 does not match receipt")
    metadata = json.loads(metadata_bytes)
    run = json.loads((folder / "run.json").read_text())
    if run.get("fixture_id") != FIXTURE_ID or metadata.get("schema") != "vragecage.sensor-frame.v1":
        raise ValueError("terrain snapshot belongs to a different fixture or schema")
    width, height = int(metadata["width"]), int(metadata["height"])
    header = f"P5\n{width} {height}\n65535\n".encode("ascii")
    if not depth_bytes.startswith(header) or len(depth_bytes) != len(header) + width * height * 2:
        raise ValueError("unexpected terrain depth raster encoding")
    depths = struct.unpack(f">{width * height}H", depth_bytes[len(header):])
    forward, camera_up = normalize(metadata["forward"]), normalize(metadata["up"])
    right = normalize(cross(forward, camera_up))
    hfov = math.radians(float(metadata["horizontal_fov_degrees"]))
    hscale = math.tan(hfov / 2)
    vscale = hscale * height / width
    origin = metadata["origin"]
    points = []
    for index, encoded in enumerate(depths):
        if encoded == 0:
            continue
        x, y = index % width, index // width
        horizontal = 2 * (x + 0.5) / width - 1
        vertical = 1 - 2 * (y + 0.5) / height
        ray = normalize([forward[i] + right[i] * horizontal * hscale +
                         camera_up[i] * vertical * vscale for i in range(3)])
        distance = encoded * float(metadata["range_meters"]) / 65535
        points.append([origin[i] + ray[i] * distance for i in range(3)])
    if len(points) != int(metadata["hit_pixels"]):
        raise ValueError("terrain hit count does not match receipt metadata")
    return points, metadata, run["started_at"]


def project(points: list[list[float]], origin: list[float], basis: dict) -> list[list[float]]:
    return [[dot([point[i] - origin[i] for i in range(3)], basis[axis])
             for axis in ("east", "north", "up")] for point in points]


def setup_axes(ax, title: str) -> None:
    ax.set_facecolor("#14222c")
    ax.set_title(title, color="white", fontsize=11, weight="bold", pad=9)
    ax.tick_params(colors="#b9c9d4", labelsize=8)
    for spine in ax.spines.values():
        spine.set_color("#536571")
    ax.grid(color="#40515c", alpha=0.42, linewidth=0.6)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence-dir", type=Path, required=True)
    parser.add_argument("--front-frames", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    evidence = args.evidence_dir.expanduser().resolve(strict=True)
    front_dir = args.front_frames.expanduser().resolve(strict=True)
    output = args.output.expanduser().resolve()
    if output.exists():
        raise SystemExit(f"refusing to overwrite output: {output}")
    manifest_path = front_dir / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    if manifest.get("schema") != "vragecage.holoview-software-video.v1" or manifest.get("frame_count") != 3:
        raise ValueError("expected three validated front-camera HoloView frames")
    front_frames = []
    front_ticks = []
    for row in manifest["frames"]:
        frame_path = front_dir / row["png_file"]
        image_bytes = frame_path.read_bytes()
        if sha256(image_bytes) != row["png_sha256"]:
            raise ValueError(f"front frame hash mismatch: {frame_path}")
        front_frames.append(mpimg.imread(frame_path))
        front_ticks.append(int(row["snapshot_tick"]))
    if front_ticks != sorted(front_ticks) or len(set(front_ticks)) != 3:
        raise ValueError("front HoloView ticks are not strictly increasing")

    approach = load_receipt(evidence / "approach-terminal-receipt.json", APPROACH_ID)
    arrival = load_receipt(evidence / "arrival-terminal-receipt.json", ARRIVAL_ID)
    approach_samples = log_motion(evidence / "raw/server-logs/approach.log", APPROACH_ID)
    arrival_samples = log_motion(evidence / "raw/server-logs/arrival-recheck.log", ARRIVAL_ID)
    arrival_inputs_fields = arrival_inputs(arrival)
    accepted_target = vector(fields(next(event for event in arrival["receipt"]["lab_operation_events"]
                                          if "status=accepted" in event))["target"])
    if accepted_target is None or math.dist(accepted_target, TARGET_DEFAULT) > 0.01:
        raise ValueError("arrival target is missing")
    # The acceptance event rounds world coordinates to millimeters. Use the
    # exact fixed waypoint from the signed trial note for 3D distance checks.
    target = TARGET_DEFAULT
    terminal = arrival_samples[-1]["position"]
    terminal_fields = arrival_inputs_fields

    depth_points, depth_metadata, depth_started = load_depth_points(evidence / "raw/pre-run-depth")
    up_value = vector(fields(next(event for event in approach["receipt"]["lab_operation_events"]
                                  if "type=server-control" in event and "status=complete" in event)
                            )["navigationUp"])
    if up_value is None:
        up_value = depth_metadata.get("natural_up_world") or [0, 1, 0]
    basis = gravity_basis(up_value)
    depth_basis = gravity_basis(depth_metadata.get("natural_up_world") or up_value)
    depth_local = project(depth_points, depth_metadata["origin"], depth_basis)
    approach_path = project([sample["position"] for sample in approach_samples], target, basis)
    arrival_path = project([sample["position"] for sample in arrival_samples], target, basis)
    approach_terminal = approach_path[-1]
    target_xy = [0.0, 0.0]

    minimum_ticks = int(arrival_samples[0]["ticks"])
    maximum_ticks = int(arrival_samples[-1]["ticks"])
    elapsed_ticks = int(terminal_fields.get("elapsedTicks", maximum_ticks))
    run_duration = float(arrival_samples[-1]["seconds"])
    approach_duration = float(approach_samples[-1]["seconds"])
    compressed_restart_gap = 1.5
    arrival_timeline_start = approach_duration + compressed_restart_gap
    seconds_per_tick = run_duration / max(1, elapsed_ticks)
    estimated_run_start_tick = front_ticks[-1] - elapsed_ticks
    front_offsets = [(tick - estimated_run_start_tick) * seconds_per_tick
                     for tick in front_ticks]
    # The final HoloView tick is the arrival terminal tick; snap floating-point
    # reconstruction to the receipt's exact elapsed-simulation endpoint.
    front_offsets[-1] = run_duration
    duration = arrival_timeline_start + max(run_duration, front_offsets[-1])
    total_frames = int(math.ceil((duration + 0.5) * 20))
    arrival_radius = float(terminal_fields.get("worldArrivalRadius", "0.5"))
    final_distance = math.dist(terminal, target)
    start_distance = math.dist(arrival_samples[0]["position"], target)
    min_clearance = float(terminal_fields.get("depthMinimumClearance", "8"))
    chosen_heading = float(terminal_fields.get("depthHeading", "0"))
    target_bearing = float(terminal_fields.get("headingError", "0"))
    scan_age = terminal_fields.get("depthScanAgeTicks", "unknown")
    self_hits = terminal_fields.get("depthSelfGridHits", "unknown")
    terminal_speed = terminal_fields.get("speed", "unknown")
    reported_speed = terminal_fields.get("reportedLinearSpeed", "unknown")
    operation_run = arrival["receipt"].get("lab_run_id", "unknown")

    fig = plt.figure(figsize=(16, 9), dpi=100, facecolor="#0d151d")
    grid = fig.add_gridspec(2, 2, left=0.055, right=0.98, bottom=0.07, top=0.88,
                            hspace=0.28, wspace=0.2, width_ratios=[1.0, 1.04],
                            height_ratios=[1.0, 0.83])
    terrain_ax = fig.add_subplot(grid[0, 0])
    route_ax = fig.add_subplot(grid[1, 0])
    front_ax = fig.add_subplot(grid[0, 1])
    control_ax = fig.add_subplot(grid[1, 1])
    fig.suptitle("VOIDWRIGHT · DEPTH-GUIDED WAYPOINT ARRIVAL", color="white",
                 fontsize=19, weight="bold", y=0.96)
    fig.text(0.5, 0.915,
             "Full 18.7 s of controller motion · front HoloView snapshots bracketing the final recheck",
             color="#bdd0dc", fontsize=10, ha="center")

    # Independent pre-run terrain-return view. This is not the controller's 13-ray fan.
    setup_axes(terrain_ax, "EARLIER FRONT-DEPTH RETURNS · GRAVITY-ALIGNED LOCAL MAP")
    terrain_scatter = terrain_ax.scatter([p[0] for p in depth_local], [p[1] for p in depth_local],
                                         c=[p[2] for p in depth_local], s=12,
                                         cmap="terrain", edgecolors="none", alpha=0.9)
    terrain_ax.scatter([0], [0], marker="^", s=45, c="#64d6ff", edgecolors="white",
                       linewidths=0.5, label="camera origin")
    camera_forward = normalize(depth_metadata["forward"])
    horizontal_forward = [dot(camera_forward, depth_basis["east"]),
                          dot(camera_forward, depth_basis["north"])]
    forward_length = max(3.0, min(20.0, math.sqrt(max(
        max(p[0] for p in depth_local) - min(p[0] for p in depth_local),
        max(p[1] for p in depth_local) - min(p[1] for p in depth_local), 1.0)) * 0.2))
    terrain_ax.arrow(0, 0, horizontal_forward[0] * forward_length,
                     horizontal_forward[1] * forward_length, color="#64d6ff",
                     width=0.035, head_width=0.5, length_includes_head=True)
    terrain_ax.set_xlabel("Gravity East from camera (m)", color="#c6d3dc", fontsize=8)
    terrain_ax.set_ylabel("Gravity North from camera (m)", color="#c6d3dc", fontsize=8)
    terrain_ax.legend(loc="upper left", fontsize=7, facecolor="#14222c", edgecolor="#536571",
                      labelcolor="white")
    terrain_ax.text(0.02, 0.02,
                    f"{len(depth_local)} voxel ray returns · 32×18 raster · 80 m cap\n"
                    f"capture {depth_started[:16].replace('T', ' ')} UTC · NOT used as drive input",
                    transform=terrain_ax.transAxes, color="#f1e7cf", fontsize=7.2, va="bottom",
                    bbox=dict(boxstyle="round,pad=0.35", facecolor="#101820", alpha=0.92,
                              edgecolor="#62717a"))
    colorbar = fig.colorbar(terrain_scatter, ax=terrain_ax, fraction=0.035, pad=0.02)
    colorbar.ax.tick_params(labelsize=7, colors="#c6d3dc")
    colorbar.set_label("gravity-up (m)", color="#c6d3dc", fontsize=7)

    # Route reconstructed solely from evaluator COM telemetry; no terrain mesh is drawn here.
    setup_axes(route_ax, "ROUTE MAP · WORLD COM POSITIONS (NOT A TERRAIN HEIGHTMAP)")
    route_x = [p[0] for p in approach_path]
    route_y = [p[1] for p in approach_path]
    arrival_x = [p[0] for p in arrival_path]
    arrival_y = [p[1] for p in arrival_path]
    route_ax.plot(route_x, route_y, color="#58d6ff", linewidth=2.0, alpha=0.86,
                  label="approach · 1,062 ticks")
    route_ax.plot(arrival_x, arrival_y, color="#ffba6b", linewidth=1.6, alpha=0.95,
                  label="3D-radius recheck · 109 ticks")
    route_ax.scatter([route_x[0]], [route_y[0]], marker="o", s=55, c="#58d6ff",
                     edgecolors="white", linewidths=0.5, zorder=4, label="approach start")
    route_ax.scatter([approach_terminal[0]], [approach_terminal[1]], marker="s", s=42,
                     c="#58d6ff", edgecolors="white", linewidths=0.5, zorder=4,
                     label="session 1 end")
    route_ax.scatter([arrival_x[0]], [arrival_y[0]], marker="o", s=30, c="#ffba6b",
                     edgecolors="white", linewidths=0.4, zorder=5, label="session 2 start")
    route_ax.add_patch(Circle(target_xy, arrival_radius, color="#ffd166", alpha=0.19,
                              linewidth=1.2, linestyle="--", fill=True, zorder=1))
    route_ax.scatter([0], [0], marker="*", s=180, c="#ffd166", edgecolors="white",
                     linewidths=0.6, zorder=6, label="fixed 3D waypoint")
    cursor, = route_ax.plot([], [], marker="o", markersize=8, color="#ff705d",
                            markeredgecolor="white", linestyle="None", zorder=7)
    route_ax.set_aspect("equal", adjustable="datalim")
    route_ax.set_xlabel("Gravity-East from waypoint (m)", color="#c6d3dc", fontsize=8)
    route_ax.set_ylabel("Gravity-North from waypoint (m)", color="#c6d3dc", fontsize=8)
    route_ax.legend(loc="upper left", fontsize=6.7, ncol=2, facecolor="#14222c",
                    edgecolor="#536571", labelcolor="white")
    all_xy = route_x + route_y + arrival_x + arrival_y + [0]
    # Coordinates are in an arbitrary gravity-aligned horizontal basis; use a tight map extent.
    x_values = route_x + arrival_x + [0]
    y_values = route_y + arrival_y + [0]
    x_pad = max(0.35, (max(x_values) - min(x_values)) * 0.18)
    y_pad = max(0.35, (max(y_values) - min(y_values)) * 0.18)
    route_ax.set_xlim(min(x_values) - x_pad, max(x_values) + x_pad)
    route_ax.set_ylim(min(y_values) - y_pad, max(y_values) + y_pad)
    route_ax.text(0.99, 0.03,
                  "Approach: 2.020 → 0.522 m (3D arrival rejected)\n"
                  "Recheck: 0.527 → 0.499 m (inside 0.500 m sphere)\n"
                  "Server was stopped/restarted between runs",
                  transform=route_ax.transAxes, ha="right", va="bottom", color="#d6e2eb",
                  fontsize=7.2, bbox=dict(boxstyle="round,pad=0.35", facecolor="#101820",
                                          alpha=0.92, edgecolor="#62717a"))

    # These are discrete software-rendered snapshots, not a continuous RGB stream.
    setup_axes(front_ax, "FRONT-VIEW SNAPSHOTS · HOLOVIEW SOFTWARE RENDER · NOT CONTROLLER INPUT")
    front_ax.grid(False)
    front_ax.set_xticks([])
    front_ax.set_yticks([])
    image_artist = front_ax.imshow(front_frames[0], interpolation="nearest")
    placeholder = front_ax.text(0.5, 0.52, "FRONT VIEW NOT CAPTURED\nfor the 1,062-tick approach\n\n"
                               "Timeline break: server stopped / restarted\n"
                               "Snapshots below bracket the final recheck",
                               transform=front_ax.transAxes, color="#d9e5ec", fontsize=11,
                               family="DejaVu Sans Mono", ha="center", va="center",
                               bbox=dict(boxstyle="round,pad=0.8", facecolor="#101820",
                                         alpha=0.96, edgecolor="#62717a"))
    placeholder.set_visible(False)
    frame_label = front_ax.text(0.02, 0.03, "", transform=front_ax.transAxes, color="white",
                                fontsize=8, family="monospace", va="bottom",
                                bbox=dict(boxstyle="round,pad=0.35", facecolor="#101820",
                                          alpha=0.93, edgecolor="#62717a"))

    # Controller input card: report only durable facts and explicitly expose missing per-ray values.
    control_ax.set_facecolor("#14222c")
    control_ax.set_xticks([])
    control_ax.set_yticks([])
    for spine in control_ax.spines.values():
        spine.set_color("#536571")
    control_ax.set_title("WHAT VOIDWRIGHT ACTUALLY RECEIVED", color="white", fontsize=11,
                         weight="bold", pad=9)
    controller_text = (
        "NOT RGB: no HoloView pixels or image model entered the control loop.\n\n"
        "FRONT DEPTH SENSOR  80564599189339180\n"
        "13 physics-ray beams · -60°…+60° in 10° steps · 8 m max range\n"
        "3.5 m clearance threshold · rescan interval: 6 ticks\n"
        "At completion: CLEAR · target bearing = chosen heading = "
        f"{math.degrees(chosen_heading):+.1f}° · center 3 rays reached {min_clearance:.1f} m cap\n"
        f"Self-grid hits: {self_hits} · scan age: {scan_age} ticks\n"
        "Per-beam ranges were not persisted; the receipt stores this summary only.\n\n"
        "ALSO PROVIDED  fixed world waypoint + gravity-local frame + COM motion telemetry\n"
        f"Target: [{target[0]:.3f}, {target[1]:.3f}, {target[2]:.3f}]\n"
        "Actuation envelope: 4 wheels · 2 steering wheels · max 0.50 m/s · power cap 0.15\n\n"
        f"ARRIVAL CHECK  {elapsed_ticks} ticks / {run_duration:.3f} sim s\n"
        f"COM-to-target: {start_distance:.3f} m → {final_distance:.6f} m; radius = {arrival_radius:.3f} m\n"
        f"Cycle-averaged COM speed: {terminal_speed} m/s · physics readback: {reported_speed} m/s\n"
        "The two speed readings still disagree; the position/radius check is the arrival proof."
    )
    control_ax.text(0.025, 0.94, controller_text, transform=control_ax.transAxes,
                    va="top", ha="left", color="#d9e5ec", fontsize=8.2,
                    family="DejaVu Sans Mono", linespacing=1.35)

    fig.text(0.5, 0.025,
             "Evaluator telemetry ≠ agent perception · terrain scan is a separate earlier observation · "
             "HoloView is mod-rendered imagery, not native framebuffer capture",
             ha="center", color="#b8c8d2", fontsize=8)

    def interpolate(samples: list[dict], elapsed_seconds: float) -> list[float]:
        tick_rate = samples[-1]["ticks"] / max(samples[-1]["seconds"], 1e-9)
        tick = max(samples[0]["ticks"], min(samples[-1]["ticks"],
                                             elapsed_seconds * tick_rate))
        for left, right in zip(samples, samples[1:]):
            if left["ticks"] <= tick <= right["ticks"]:
                ratio = ((tick - left["ticks"]) / (right["ticks"] - left["ticks"])
                         if right["ticks"] != left["ticks"] else 0)
                return [left["position"][axis] + ratio *
                        (right["position"][axis] - left["position"][axis]) for axis in range(3)]
        return samples[-1]["position"]

    def update(frame_number: int):
        elapsed = min(duration, frame_number / 20.0)
        if elapsed < approach_duration:
            current_world = interpolate(approach_samples, elapsed)
            image_artist.set_visible(False)
            placeholder.set_visible(True)
            frame_label.set_text(f"approach session · t+{elapsed:.2f}s\n"
                                 f"COM range {math.dist(current_world, target):.3f} m\n"
                                 "No synchronized front frames saved")
        elif elapsed < arrival_timeline_start:
            current_world = approach_samples[-1]["position"]
            image_artist.set_visible(False)
            placeholder.set_visible(True)
            frame_label.set_text("SERVER STOPPED / RESTARTED\n"
                                 "Two independent controller sessions; gap compressed")
        else:
            recheck_time = min(run_duration, elapsed - arrival_timeline_start)
            eligible_frames = [index for index, offset in enumerate(front_offsets)
                               if offset <= recheck_time + 1e-6]
            nearest_frame = eligible_frames[-1] if eligible_frames else 0
            image_artist.set_data(front_frames[nearest_frame])
            image_artist.set_visible(True)
            placeholder.set_visible(False)
            frame_time = front_offsets[nearest_frame]
            is_arrived = elapsed >= arrival_timeline_start + run_duration - 1e-6
            frame_label.set_text(
                f"snapshot tick {front_ticks[nearest_frame]} · recheck t{frame_time:+.3f}s\n"
                f"controller {('COMPLETE' if is_arrived else 'recheck running')} · "
                f"COM distance {final_distance:.3f} m at terminal")
            current_world = interpolate(arrival_samples, recheck_time)
        current_map = project([current_world], target, basis)[0]
        cursor.set_data([current_map[0]], [current_map[1]])
        return image_artist, placeholder, frame_label, cursor

    animation = FuncAnimation(fig, update, frames=total_frames, interval=50, blit=False)
    output.parent.mkdir(parents=True, exist_ok=True)
    animation.save(output, writer=FFMpegWriter(fps=20, bitrate=2600,
                                                metadata={"title": "Voidwright depth-guided waypoint arrival recap"}))
    plt.close(fig)
    summary = {
        "schema": "voidwright.depth-waypoint-arrival-recap.v1",
        "video": str(output),
        "video_sha256": sha256(output.read_bytes()),
        "approach_receipt_sha256": sha256((evidence / "approach-terminal-receipt.json").read_bytes()),
        "arrival_receipt_sha256": sha256((evidence / "arrival-terminal-receipt.json").read_bytes()),
        "front_manifest_sha256": sha256(manifest_path.read_bytes()),
        "terrain_receipt_sha256": sha256((evidence / "raw/pre-run-depth/terminal-receipt.json").read_bytes()),
        "route_samples": {"approach": len(approach_samples), "arrival": len(arrival_samples)},
        "front_holoview_ticks": front_ticks,
        "front_provenance": manifest.get("provenance"),
        "presentation_timeline": {
            "approach_sim_seconds": approach_duration,
            "compressed_stop_restart_gap_seconds": compressed_restart_gap,
            "arrival_recheck_sim_seconds": run_duration,
            "front_snapshot_offsets_from_recheck_seconds": front_offsets,
        },
        "terrain_snapshot_started_at": depth_started,
        "terrain_camera_id": depth_metadata["camera_id"],
        "terrain_hit_pixels": depth_metadata["hit_pixels"],
        "controller_inputs": {
            "beam_angles_degrees": list(range(-60, 61, 10)),
            "max_range_m": 8,
            "required_clearance_m": 3.5,
            "scan_interval_ticks": 6,
            "terminal_decision": terminal_fields.get("depthDecision"),
            "terminal_heading_rad": chosen_heading,
            "terminal_corridor_clearance_m": min_clearance,
            "per_beam_ranges_persisted": False,
            "rgb_used_by_controller": False,
        },
        "arrival": {
            "starting_3d_distance_m": start_distance,
            "terminal_3d_distance_m": final_distance,
            "arrival_radius_m": arrival_radius,
            "elapsed_ticks": elapsed_ticks,
            "elapsed_sim_seconds": run_duration,
            "net_com_displacement_m": terminal_fields.get("comDisplacementMeters"),
            "path_length_m": terminal_fields.get("pathLengthMeters"),
            "terminal_position_world": terminal,
            "target_world": target,
        },
        "limitations": [
            "The earlier 1,062-tick approach is plotted from COM telemetry; its full front-render sequence was not retained after restart.",
            "Three front HoloView snapshots bracket the final 109-tick arrival recheck window (first at estimated t-0.176 s); run-relative timing is derived from the final snapshot tick and controller duration.",
            "The terrain depth map is a separate earlier same-fixture observation and was not the controller's 13-ray input.",
            "The controller's individual beam ranges were not saved; only the decision summary is available.",
            "HoloView frames are mod-rendered and are not native framebuffer capture or controller input.",
            "The relative alignment between HoloView global ticks and controller elapsed ticks is estimated from the final snapshot tick and run duration.",
        ],
    }
    summary_path = output.with_suffix(".summary.json")
    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"video": str(output), "video_sha256": summary["video_sha256"],
                      "summary": str(summary_path), "front_ticks": front_ticks,
                      "terrain_hits": len(depth_local),
                      "approach_samples": len(approach_samples),
                      "arrival_samples": len(arrival_samples)}, indent=2))


if __name__ == "__main__":
    main()
