#!/usr/bin/env python3
"""Render a labeled animation from a Voidwright terminal receipt.

This is a visualization of sparse evaluator/server telemetry, not captured
Space Engineers footage. Positions between recorded samples are interpolated
only for display.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.animation import FuncAnimation, FFMpegWriter


def parse_events(receipt):
    points = []
    for event in receipt["receipt"].get("lab_operation_events", []):
        if "type=server-control" not in event or "elapsedTicks=" not in event:
            continue
        fields = {}
        for key in ("elapsedTicks", "targetDistance", "bestTargetDistance", "driveInput", "speed"):
            marker = key + "="
            if marker in event:
                raw = event.split(marker, 1)[1].split(" ", 1)[0]
                try:
                    fields[key] = float(raw)
                except ValueError:
                    pass
        for key in ("position", "vehicleForward", "navigationUp"):
            marker = key + "=["
            if marker in event:
                raw = event.split(marker, 1)[1].split("]", 1)[0]
                fields[key] = [float(value) for value in raw.split(",")]
        if "position" in fields and "elapsedTicks" in fields:
            points.append(fields)
    points.sort(key=lambda item: item["elapsedTicks"])
    # Multiple server-control events may share a tick; keep the last complete sample.
    deduped = {}
    for point in points:
        deduped[point["elapsedTicks"]] = point
    return list(deduped.values())


def interpolate(points, value):
    for left, right in zip(points, points[1:]):
        if left["elapsedTicks"] <= value <= right["elapsedTicks"]:
            span = right["elapsedTicks"] - left["elapsedTicks"]
            alpha = (value - left["elapsedTicks"]) / span if span else 0
            pos = [a + (b - a) * alpha for a, b in zip(left["position"], right["position"])]
            forward = [a + (b - a) * alpha for a, b in zip(left.get("vehicleForward", [0, 0, 0]),
                                                              right.get("vehicleForward", [0, 0, 0]))]
            norm = math.sqrt(sum(value * value for value in forward)) or 1
            return pos, [value / norm for value in forward], left, right
    point = points[0] if value < points[0]["elapsedTicks"] else points[-1]
    forward = point.get("vehicleForward", [1, 0, 0])
    norm = math.sqrt(sum(value * value for value in forward)) or 1
    return point["position"], [value / norm for value in forward], point, point


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("receipt", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--target", nargs=3, type=float, default=[8181.29, 942.59, 9686.16],
                        metavar=("X", "Y", "Z"))
    args = parser.parse_args()

    raw = args.receipt.read_bytes()
    wrapper = json.loads(raw)
    receipt = wrapper["receipt"]
    points = parse_events(wrapper)
    if len(points) < 2:
        raise SystemExit("terminal receipt has fewer than two positioned control samples")
    target = args.target
    start_tick, end_tick = points[0]["elapsedTicks"], points[-1]["elapsedTicks"]
    final = points[-1]
    if receipt.get("lab_operation_status") != "complete":
        raise SystemExit("refusing to render a non-complete drive as a successful arrival")

    up = points[0].get("navigationUp", [0, 1, 0])
    up_norm = math.sqrt(sum(value * value for value in up)) or 1
    up = [value / up_norm for value in up]
    reference = [1, 0, 0]
    projection = sum(reference[i] * up[i] for i in range(3))
    axis_a = [reference[i] - up[i] * projection for i in range(3)]
    axis_norm = math.sqrt(sum(value * value for value in axis_a)) or 1
    axis_a = [value / axis_norm for value in axis_a]
    axis_b = [up[1] * axis_a[2] - up[2] * axis_a[1],
              up[2] * axis_a[0] - up[0] * axis_a[2],
              up[0] * axis_a[1] - up[1] * axis_a[0]]

    def project(position):
        delta = [position[i] - target[i] for i in range(3)]
        return [sum(delta[i] * axis_a[i] for i in range(3)),
                sum(delta[i] * axis_b[i] for i in range(3))]

    projected = [project(p["position"]) for p in points]
    xs = [point[0] for point in projected] + [0]
    zs = [point[1] for point in projected] + [0]
    pad = max(1.5, max(max(xs) - min(xs), max(zs) - min(zs), 1) * 0.25)
    fig, ax = plt.subplots(figsize=(12.8, 7.2), dpi=100)
    fig.patch.set_facecolor("#101820")
    ax.set_facecolor("#14222c")
    ax.set_xlim(min(xs) - pad, max(xs) + pad)
    ax.set_ylim(min(zs) - pad, max(zs) + pad)
    ax.set_aspect("equal", adjustable="box")
    ax.grid(color="#40515c", linewidth=0.7, alpha=0.6)
    ax.tick_params(colors="#c6d3dc", labelsize=9)
    for spine in ax.spines.values():
        spine.set_color("#536571")
    ax.set_xlabel("Ground-plane axis A (m from target)", color="#d5e0e7")
    ax.set_ylabel("Ground-plane axis B (m from target)", color="#d5e0e7")
    ax.set_title("VOIDWRIGHT · BLUE TRUCK GPS APPROACH", color="white", fontsize=17, weight="bold", pad=14)

    # Arrival radius is 3 m, matching the completed operation receipt.
    arrival = plt.Circle((0, 0), 3.0, color="#ffd166", alpha=0.18, zorder=1)
    ax.add_patch(arrival)
    ax.scatter([0], [0], marker="*", s=260, color="#ffd166", edgecolor="white",
               linewidth=0.8, zorder=5, label="Starting Area target")
    ax.text(0, pad * 0.11, "TARGET · 3 m ground-plane arrival radius", color="#ffe29a",
            fontsize=10, ha="center", weight="bold")
    line, = ax.plot([], [], color="#53d8fb", linewidth=2.6, alpha=0.9, zorder=3)
    dots = ax.scatter([], [], s=18, color="#8be9fd", alpha=0.65, zorder=3)
    truck, = ax.plot([], [], marker=(3, 0, 0), markersize=19, color="#ff725e",
                     markeredgecolor="white", markeredgewidth=0.7, linestyle="None", zorder=6)
    status = ax.text(0.02, 0.97, "", transform=ax.transAxes, va="top", color="white",
                     fontsize=11, family="monospace",
                     bbox=dict(boxstyle="round,pad=0.45", facecolor="#0b1116", alpha=0.9,
                               edgecolor="#52636d"))
    fig.text(0.5, 0.025,
             "TELEMETRY VISUALIZATION · sparse server samples; path between samples interpolated for display · NOT game footage",
             ha="center", color="#c6d3dc", fontsize=9)
    ax.legend(loc="lower right", facecolor="#14222c", edgecolor="#536571", labelcolor="white")
    timeline = [start_tick + (end_tick - start_tick) * index / 239 for index in range(240)]

    def update(frame):
        tick = timeline[frame]
        pos, forward, _, right = interpolate(points, tick)
        plotted = project(pos)
        past = [p for p in points if p["elapsedTicks"] <= tick]
        partial = past[:]
        if not partial or partial[-1]["elapsedTicks"] < tick:
            partial.append({"elapsedTicks": tick, "position": pos})
        path_points = [project(p["position"]) for p in partial]
        px = [point[0] for point in path_points]
        pz = [point[1] for point in path_points]
        line.set_data(px, pz)
        dots.set_offsets(list(zip(px, pz)))
        # Project the truck's forward direction onto the navigation ground plane.
        forward3 = forward
        projected_forward = [sum(forward3[i] * axis_a[i] for i in range(3)),
                             sum(forward3[i] * axis_b[i] for i in range(3))]
        angle = math.degrees(math.atan2(projected_forward[1], projected_forward[0])) - 90
        truck.set_data([plotted[0]], [plotted[1]])
        truck.set_marker((3, 0, angle))
        delta = [target[i] - pos[i] for i in range(3)]
        vertical_error = sum(delta[i] * up[i] for i in range(3))
        ground_delta = [delta[i] - up[i] * vertical_error for i in range(3)]
        distance = math.sqrt(sum(value * value for value in ground_delta))
        status.set_text(
            f"recorded tick {int(tick)} / {int(end_tick)}\n"
            f"ground distance  {distance:5.2f} m\n"
            f"vertical error   {vertical_error:+5.2f} m\n"
            f"samples          {len(points)} sparse points\n"
            f"operation        COMPLETE")
        return line, dots, truck, status

    animation = FuncAnimation(fig, update, frames=len(timeline), interval=50, blit=False)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    animation.save(args.output, writer=FFMpegWriter(fps=20, bitrate=1800,
                                                    metadata={"title": "Voidwright GPS drive telemetry visualization"}))
    plt.close(fig)
    print(json.dumps({
        "video": str(args.output), "video_sha256": hashlib.sha256(args.output.read_bytes()).hexdigest(),
        "receipt_sha256": hashlib.sha256(raw).hexdigest(), "samples": len(points),
        "sampled_tick_range": [int(start_tick), int(end_tick)],
        "recorded_start_position": points[0]["position"], "terminal_position": final["position"],
        "target": target, "terminal_ground_distance_m": final.get("targetDistance"),
        "arrival_radius_m": 3.0,
        "visualization_note": "Sparse evaluator telemetry; interpolated between recorded samples; not game footage."
    }, indent=2))


if __name__ == "__main__":
    main()
