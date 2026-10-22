#!/usr/bin/env python3
"""Fuse receipt-validated lab depth frames into a registered 3D point cloud.

This is a software visualization of physics-ray depth observations. It is not
an RGB render, native game frame, or coherent SAR reconstruction.
"""

import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.animation import FuncAnimation, FFMpegWriter


PALETTE = ["#51d6ff", "#ff826e", "#ffd166", "#b8e986", "#c39bff", "#ff91c8", "#f8f9fa"]
DIFFERENCE_RADIUS_M = 5.0


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def find_file(folder, exact, pattern):
    candidate = folder / exact
    if candidate.exists():
        return candidate
    matches = sorted(folder.glob(pattern))
    if len(matches) != 1:
        raise ValueError(f"expected one {pattern} in {folder}, found {len(matches)}")
    return matches[0]


def normalize(vector):
    norm = math.sqrt(sum(value * value for value in vector))
    if not norm:
        raise ValueError("zero-length camera basis vector")
    return [value / norm for value in vector]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]]


def dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


def subtract(a, b):
    return [a[i] - b[i] for i in range(3)]


def display_frame(views):
    """Return a gravity basis, or a clearly labeled camera basis in zero-g."""
    candidates = [view["natural_up"] for view in views if view.get("natural_up")]
    if candidates:
        up = normalize([sum(vector[i] for vector in candidates) for i in range(3)])
        reference = [0.0, 0.0, 1.0]
        if abs(dot(reference, up)) > 0.95:
            reference = [0.0, 1.0, 0.0]
        north = normalize(subtract(reference, [dot(reference, up) * value for value in up]))
        east = normalize(cross(north, up))
        north = normalize(cross(up, east))
        return {"east": east, "north": north, "up": up}, ("East (m)", "North (m)", "Gravity-up (m)"), "gravity"
    else:
        # Natural gravity is zero in the V2R3 chamber. Use the first measured
        # camera pose as a display anchor; do not call this gravity alignment.
        up = normalize(views[0]["up"])
        forward = normalize(views[0]["forward"])
        right = normalize(cross(forward, up))
        forward = normalize(cross(up, right))
        return {"east": right, "north": forward, "up": up}, (
            "Camera-right (m)", "Camera-forward (m)", "Camera-up (m)"), "camera-pose"


def to_local(vector, basis):
    return [dot(vector, basis[axis]) for axis in ("east", "north", "up")]


def spatial_keys(points, radius):
    cells = {}
    for point in points:
        key = tuple(math.floor(value / radius) for value in point)
        cells.setdefault(key, []).append(point)
    return cells


def has_nearby(point, cells, radius):
    key = tuple(math.floor(value / radius) for value in point)
    limit2 = radius * radius
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for candidate in cells.get((key[0] + dx, key[1] + dy, key[2] + dz), ()):
                    if sum((point[i] - candidate[i]) ** 2 for i in range(3)) <= limit2:
                        return True
    return False


def load_frame(folder, label, fixture_id):
    folder = Path(folder)
    wrapper = json.loads((folder / "terminal-receipt.json").read_text())
    receipt = wrapper.get("receipt", {})
    if receipt.get("passed") is not True or receipt.get("sensor_artifact_valid") is not True:
        raise ValueError(f"receipt does not validate the sensor frame: {folder}")
    metadata_path = find_file(folder, "sensor.json", "sensor-*.json")
    depth_path = find_file(folder, "depth.pgm", "sensor-*.pgm")
    entity_path = find_file(folder, "entities.i64le", "sensor-*.i64le")
    normal_path = find_file(folder, "normals.n16le", "sensor-*.n16le")
    artifact = receipt["sensor_artifact"]
    metadata_bytes = metadata_path.read_bytes()
    depth_bytes = depth_path.read_bytes()
    entity_bytes = entity_path.read_bytes()
    normal_bytes = normal_path.read_bytes()
    expected = {
        "metadata_sha256": sha256(metadata_bytes),
        "depth_sha256": sha256(depth_bytes),
        "entity_id_sha256": sha256(entity_bytes),
        "surface_normal_sha256": sha256(normal_bytes),
    }
    receipt_expected = {
        "metadata_sha256": artifact["metadata_sha256"],
        "depth_sha256": artifact["depth_sha256"],
        "entity_id_sha256": artifact["entity_id_sha256"],
        "surface_normal_sha256": artifact["surface_normal_sha256"],
    }
    if expected != receipt_expected:
        raise ValueError(f"artifact SHA-256 mismatch in {folder}: {expected} != {receipt_expected}")
    metadata = json.loads(metadata_bytes)
    if metadata.get("operation_id") != receipt.get("lab_operation_id"):
        raise ValueError(f"operation ID mismatch in {folder}")
    if metadata.get("schema") != "vragecage.sensor-frame.v1":
        raise ValueError(f"unexpected sensor schema in {folder}")
    run_path = folder / "run.json"
    started_at = None
    if run_path.exists():
        run = json.loads(run_path.read_text())
        if run.get("fixture_id") != fixture_id:
            raise ValueError(f"fixture mismatch in {folder}")
        started_at = run.get("started_at")
    width, height = metadata["width"], metadata["height"]
    header = f"P5\n{width} {height}\n65535\n".encode("ascii")
    if not depth_bytes.startswith(header) or len(depth_bytes) != len(header) + width * height * 2:
        raise ValueError(f"unexpected PGM shape/encoding in {folder}")
    depth = struct.unpack(f">{width * height}H", depth_bytes[len(header):])
    entities = struct.unpack(f"<{width * height}q", entity_bytes)
    normals = struct.unpack(f"<{3 * width * height}h", normal_bytes)
    if len(entity_bytes) != width * height * 8 or len(normal_bytes) != width * height * 6:
        raise ValueError(f"unexpected entity/normal raster size in {folder}")

    forward = normalize(metadata["forward"])
    up = normalize(metadata["up"])
    right = normalize(cross(forward, up))
    hscale = math.tan(math.radians(metadata["horizontal_fov_degrees"]) / 2)
    vscale = hscale * height / width
    origin = metadata["origin"]
    range_m = metadata["range_meters"]
    points, point_entities, point_normals, point_ranges = [], [], [], []
    for index, encoded_depth in enumerate(depth):
        if not encoded_depth:
            continue
        x, y = index % width, index // width
        horizontal = 2 * (x + 0.5) / width - 1
        vertical = 1 - 2 * (y + 0.5) / height
        ray = normalize([forward[i] + right[i] * horizontal * hscale +
                         up[i] * vertical * vscale for i in range(3)])
        distance = encoded_depth * range_m / 65535
        points.append([origin[i] + ray[i] * distance for i in range(3)])
        point_ranges.append(distance)
        point_entities.append(entities[index])
        normal = [normals[index * 3 + axis] / 32767 for axis in range(3)]
        point_normals.append(normal)
    if len(points) != metadata["hit_pixels"]:
        raise ValueError(f"depth hit count mismatch in {folder}")

    return {
        "label": label, "camera_id": metadata["camera_id"], "grid_id": metadata["grid_id"],
        "origin": origin, "forward": forward, "up": up, "points": points,
        "natural_up": (normalize(metadata["natural_up_world"])
                       if metadata.get("natural_up_world") and
                       math.sqrt(sum(value * value for value in metadata["natural_up_world"])) > 1e-6
                       else None),
        "entity_ids": point_entities, "normals": point_normals, "point_ranges": point_ranges,
        "operation_id": metadata["operation_id"], "run_id": receipt.get("lab_run_id"),
        "started_at": started_at,
        "depth_sha256": expected["depth_sha256"], "metadata_sha256": expected["metadata_sha256"],
        "hit_pixels": metadata["hit_pixels"], "entity_summaries": metadata.get("entity_summaries", []),
        "folder": str(folder),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--max-range-m", type=float,
                        help="keep only ray returns at or nearer than this camera-relative range")
    parser.add_argument("--highlight-unmatched", action="store_true",
                        help="white-highlight view-specific samples; this is not temporal scene-change detection")
    parser.add_argument("--view", action="append", nargs=2, metavar=("LABEL", "RUN_DIR"), required=True)
    args = parser.parse_args()
    views = [load_frame(folder, label, args.fixture) for label, folder in args.view]
    if args.max_range_m is not None:
        if args.max_range_m <= 0:
            raise SystemExit("--max-range-m must be positive")
        for view in views:
            original_count = len(view["points"])
            keep = [distance <= args.max_range_m for distance in view["point_ranges"]]
            for key in ("points", "entity_ids", "normals", "point_ranges"):
                view[key] = [value for value, include in zip(view[key], keep) if include]
            view["rendered_point_count"] = len(view["points"])
            view["range_filter_m"] = args.max_range_m
            view["input_point_count"] = original_count
    else:
        for view in views:
            view["rendered_point_count"] = len(view["points"])
            view["range_filter_m"] = None
            view["input_point_count"] = len(view["points"])
    if any(not view["points"] for view in views):
        raise SystemExit("range filter removed all points from at least one view")
    if len(views) > 1 and len({view["camera_id"] for view in views}) == 1:
        distinct_origins = {tuple(view["origin"]) for view in views}
        if len(distinct_origins) < 2:
            raise SystemExit("repeated-camera fusion requires captures from different camera positions")

    colors, all_points = [], []
    for index, view in enumerate(views):
        view["color"] = PALETTE[index % len(PALETTE)]
        colors.extend([view["color"]] * len(view["points"]))
        all_points.extend(view["points"])
    center = [sum(point[axis] for point in all_points) / len(all_points) for axis in range(3)]
    basis, axis_labels, frame_kind = display_frame(views)
    local_points_by_view = [
        [to_local(subtract(point, center), basis) for point in view["points"]]
        for view in views
    ]
    relative = [point for points in local_points_by_view for point in points]
    extents = [[point[axis] for point in relative] for axis in range(3)]
    ranges = [max(axis) - min(axis) for axis in extents]
    max_range = max(ranges) or 1
    unmatched_masks = []
    for index, points in enumerate(local_points_by_view):
        other_points = [point for other_index, samples in enumerate(local_points_by_view)
                        if other_index != index for point in samples]
        other_cells = spatial_keys(other_points, DIFFERENCE_RADIUS_M)
        unmatched_masks.append([not has_nearby(point, other_cells, DIFFERENCE_RADIUS_M)
                                for point in points] if other_points else [True] * len(points))

    fig = plt.figure(figsize=(12.8, 7.2), dpi=100, facecolor="#0d151d")
    ax = fig.add_subplot(111, projection="3d", facecolor="#0d151d")
    title = ("REGISTERED MULTI-POSE DEPTH CLOUD" if len(views) > 1
             else f"V3 · 3D DEPTH RENDER · {views[0]['label'].upper()}")
    ax.set_title(title, color="white", fontsize=16,
                 weight="bold", pad=18)
    ax.set_xlabel(axis_labels[0], color="#d6e2eb", labelpad=8)
    ax.set_ylabel(axis_labels[1], color="#d6e2eb", labelpad=8)
    ax.set_zlabel(axis_labels[2], color="#d6e2eb", labelpad=7)
    ax.tick_params(colors="#c6d3dc", labelsize=8)
    for axis in (ax.xaxis, ax.yaxis, ax.zaxis):
        axis.pane.set_facecolor((0.06, 0.1, 0.14, 0.9))
        axis.pane.set_edgecolor("#465765")
    ax.grid(True, color="#34434e", alpha=0.55)
    ax.set_xlim(min(extents[0]), max(extents[0]))
    ax.set_ylim(min(extents[1]), max(extents[1]))
    ax.set_zlim(min(extents[2]), max(extents[2]))
    ax.set_box_aspect([max(ranges[0], 1), max(ranges[1], 1), max(ranges[2], 1)])

    shared_legend_added = False
    unique_legend_added = False
    for view_index, view in enumerate(views):
        points = local_points_by_view[view_index]
        unmatched = unmatched_masks[view_index]
        categories = (
            (([not value for value in unmatched], view["color"], 4, 0.35,
              f"Matched samples · {view['label']}" if not shared_legend_added else "_nolegend_"),
             (unmatched, "#ffffff", 9, 0.98,
              f"No other-view sample within {DIFFERENCE_RADIUS_M:g} m · {view['label']}"
              if not unique_legend_added else "_nolegend_"))
            if args.highlight_unmatched else
            (([True] * len(points), view["color"], 6, 0.86,
              f"{view['label']} · {len(points):,} returns"),)
        )
        for mask, color, size, alpha, label in categories:
            selected = [point for point, include in zip(points, mask) if include]
            if selected:
                ax.scatter([p[0] for p in selected], [p[1] for p in selected], [p[2] for p in selected],
                           s=size, c=color, alpha=alpha, depthshade=False, label=label)
                if label.startswith("Shared samples"):
                    shared_legend_added = True
                if label.startswith("No match"):
                    unique_legend_added = True
        origin = to_local(subtract(view["origin"], center), basis)
        ax.scatter(*origin, marker="^", s=42, c=view["color"], edgecolors="white", linewidths=0.4)
        # Direction arrows are scaled for legibility and are not geometry.
        direction_length = max_range * 0.035
        local_forward = to_local(view["forward"], basis)
        ax.quiver(*origin, *(value * direction_length for value in local_forward),
                  color=view["color"], linewidth=1.1, arrow_length_ratio=0.18)

    camera_tracks = {}
    for view in views:
        camera_tracks.setdefault(view["camera_id"], []).append(view)
    for track in camera_tracks.values():
        if len(track) < 2:
            continue
        track.sort(key=lambda view: view.get("started_at") or "")
        track_origins = [to_local(subtract(view["origin"], center), basis) for view in track]
        ax.plot([point[0] for point in track_origins],
                [point[1] for point in track_origins],
                [point[2] for point in track_origins],
                color="#f5f5f5", linestyle="--", linewidth=1.5, alpha=0.85,
                label="Observed camera-position displacement (not path)")

    if len(views) > 1:
        unmatched_counts = [sum(mask) for mask in unmatched_masks]
        frame_note = ("gravity-aligned East/North/Up" if frame_kind == "gravity"
                      else "camera-pose display frame · zero gravity reported")
        difference_note = (f"white = no other-view sample within {DIFFERENCE_RADIUS_M:g} m; NOT scene change"
                           if args.highlight_unmatched else
                           "source colors distinguish captures; no scene-change mask computed")
        info = (f"{len(views)} receipt-validated poses · {len(all_points):,} depth hits · "
                f"{len({view['camera_id'] for view in views})} camera blocks\n"
                f"{frame_note} · {difference_note}\n"
                "points = ray-hit surfaces only · registered software depth render\n"
                "NOT coherent SAR or RGB capture")
    else:
        info = (f"1 receipt-validated pose · {len(all_points):,} depth hits\n"
                f"{('gravity-aligned' if frame_kind == 'gravity' else 'camera-pose')} display axes\n"
                "no cross-view difference computed; no simulator mesh used\n"
                "software depth render; NOT RGB or game footage")
    fig.text(0.02, 0.03, info, color="#d1dde5", fontsize=9, family="monospace",
             bbox=dict(boxstyle="round,pad=0.45", facecolor="#101c25", edgecolor="#536671", alpha=0.95))
    ax.legend(loc="upper left", fontsize=8, facecolor="#101c25", edgecolor="#536671", labelcolor="white")
    fig.tight_layout(rect=(0, 0.12, 1, 0.96))

    args.output_dir.mkdir(parents=True, exist_ok=True)
    stem = "registered-depth-cloud" if len(views) > 1 else "depth-cloud"
    still_path = args.output_dir / f"{stem}.png"
    video_path = args.output_dir / f"{stem}.mp4"
    fig.savefig(still_path, facecolor=fig.get_facecolor(), dpi=100)
    animation = FuncAnimation(fig, lambda frame: ax.view_init(elev=23, azim=25 + frame * 2.25),
                              frames=160, interval=50, blit=False)
    animation.save(video_path, writer=FFMpegWriter(fps=20, bitrate=2200,
                                                   metadata={"title": "V3 registered multi-view depth cloud"}))
    plt.close(fig)

    summary = {
        "schema": ("voidwright.registered-multiview-depth.v1" if len(views) > 1
                   else "voidwright.software-depth-render.v1"),
        "fixture_id": args.fixture,
        "method": "world-pose registration of depth-ray endpoints",
        "range_filter_m": args.max_range_m,
        "difference_display": ("white highlights unmatched samples across views; not temporal scene-change detection"
                               if args.highlight_unmatched else "source-colored captures; no scene-change mask"),
        "sar_status": ("SAR-inspired only; no coherent phase, radar waveform, or moving-aperture acquisition"
                        if len(views) > 1 else "not applicable: single depth view"),
        "render_status": "software-rendered point cloud from depth rays; no native RGB/game framebuffer",
        "point_count": len(all_points), "view_count": len(views),
        "camera_count": len({view["camera_id"] for view in views}),
        "world_origin_for_display": center,
        "display_frame": {"name": ("gravity-aligned East/North/Up" if frame_kind == "gravity"
                                    else "first-camera-pose anchored axes"), "basis_world": basis,
                          "alignment_source": ("mean natural_up_world from sensor metadata"
                                               if frame_kind == "gravity" else
                                               "first camera forward/up; natural gravity was zero"),
                          "difference_radius_m": DIFFERENCE_RADIUS_M,
                          "unmatched_point_counts_by_view": {
                              view["label"]: count for view, count in zip(views, unmatched_counts)
                          } if len(views) > 1 else None,
                          "difference_warning": ("No match within radius is a sampling/visibility difference, "
                                                 "not proof of a changed or novel object.")},
        "views": [{key: view[key] for key in ("label", "camera_id", "grid_id", "origin", "forward", "started_at",
                                                     "hit_pixels", "rendered_point_count", "range_filter_m",
                                                     "run_id", "operation_id", "depth_sha256",
                                                     "metadata_sha256", "entity_summaries", "folder")}
                  for view in views],
        "artifacts": {
            "png_sha256": sha256(still_path.read_bytes()),
            "mp4_sha256": sha256(video_path.read_bytes()),
        },
        "limitations": [
            "Each point is an endpoint of a physics ray; unobserved space is unknown, not empty.",
            "Camera poses are registered in the fixture's world frame; captures are sequential, not time-synchronized.",
            "Only surfaces hit by rays appear; this does not reconstruct complete grids or occluded geometry.",
            "This is multi-view depth fusion, not coherent synthetic-aperture radar.",
            "The renderer uses no privileged terrain mesh or simulator geometry."
        ],
    }
    summary_path = args.output_dir / "summary.json"
    summary_path.write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps({"summary": str(summary_path), "png": str(still_path), "mp4": str(video_path),
                      "point_count": len(all_points), "view_count": len(views),
                      "sha256": summary["artifacts"]}, indent=2))


if __name__ == "__main__":
    main()
