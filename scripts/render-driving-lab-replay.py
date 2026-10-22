#!/usr/bin/env python3
"""Render one bounded, read-only Driving Lab JSONL stream as static HTML/SVG."""

import argparse
from datetime import datetime
import html
import json
import math
from pathlib import Path
import sys


MAX_BYTES = 64 * 1024 * 1024
MAX_LINE_BYTES = 1024 * 1024
MAX_RECORDS = 10_000
MAX_ACTUATOR_ITEMS = 512
MAX_SERIES = 64
MAX_OUTPUT_BYTES = 16 * 1024 * 1024
SCHEMA_VERSION = 1


class DuplicateJsonKey(ValueError):
    pass


def reject_duplicate_json_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKey(f"duplicate JSON object key {key!r}")
        result[key] = value
    return result


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def load_capture(path):
    size = path.stat().st_size
    if size > MAX_BYTES:
        raise ValueError(f"input is {size} bytes; viewer limit is {MAX_BYTES}")
    records = []
    identity = None
    last_sequence = 0
    last_dropped = None
    last_simulation_tick = None
    last_simulation_time = None
    with path.open("rb") as source:
        total = 0
        while True:
            line = source.readline(MAX_LINE_BYTES + 2)
            if not line:
                break
            total += len(line)
            if total > MAX_BYTES:
                raise ValueError("input grew beyond the 64 MiB viewer limit")
            if len(line) > MAX_LINE_BYTES + 1 or (len(line) == MAX_LINE_BYTES + 1 and not line.endswith(b"\n")):
                raise ValueError("capture line exceeds the one MiB limit")
            if not line.strip():
                continue
            try:
                record = json.loads(line, object_pairs_hook=reject_duplicate_json_keys)
            except (UnicodeDecodeError, json.JSONDecodeError, DuplicateJsonKey) as exc:
                raise ValueError(f"invalid JSONL record {len(records) + 1}: {exc}") from exc
            validate_record(record, len(records) + 1)
            key = (record["runId"], record["sessionId"], record["streamId"], record.get("gridEntityId"))
            if identity is None:
                identity = key
            elif key != identity:
                raise ValueError(f"identity mismatch at record {len(records) + 1}: expected {identity}, got {key}")
            sequence = record["sequence"]
            if sequence <= last_sequence:
                raise ValueError(f"duplicate or backwards sequence at record {len(records) + 1}")
            simulation_tick = record.get("simulationTick")
            if simulation_tick is not None and last_simulation_tick is not None and simulation_tick < last_simulation_tick:
                raise ValueError(f"simulationTick moved backwards at record {len(records) + 1}")
            simulation_time = record.get("simulationTimeSeconds")
            if simulation_time is not None and last_simulation_time is not None and simulation_time < last_simulation_time:
                raise ValueError(f"simulationTimeSeconds moved backwards at record {len(records) + 1}")
            dropped = record.get("droppedRecordsTotal")
            if dropped is not None and last_dropped is not None and dropped < last_dropped:
                raise ValueError(f"cumulative drop count moved backwards at record {len(records) + 1}")
            record["_missingBefore"] = max(0, sequence - last_sequence - 1)
            record["_dropDelta"] = dropped if last_dropped is None else (dropped - last_dropped if dropped is not None and last_dropped is not None else None)
            last_sequence = sequence
            if simulation_tick is not None:
                last_simulation_tick = simulation_tick
            if simulation_time is not None:
                last_simulation_time = simulation_time
            if dropped is not None:
                last_dropped = dropped
            records.append(record)
            if len(records) > MAX_RECORDS:
                raise ValueError(f"viewer record limit is {MAX_RECORDS}")
    if not records:
        raise ValueError("capture contains no records")
    return records


def validate_record(record, number):
    if not isinstance(record, dict) or record.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError(f"record {number} is not capture schema v{SCHEMA_VERSION}")
    for field in ("runId", "sessionId", "streamId", "source", "clockDomain"):
        if not isinstance(record.get(field), str) or not record[field] or len(record[field]) > 128:
            raise ValueError(f"record {number} has invalid {field}")
    for field in ("runId", "sessionId", "streamId"):
        if any(not (c.isalnum() or c in "-_.") for c in record[field]):
            raise ValueError(f"record {number} has unsafe {field}")
    if not isinstance(record.get("sequence"), int) or isinstance(record["sequence"], bool) or record["sequence"] <= 0:
        raise ValueError(f"record {number} has invalid sequence")
    captured = record.get("capturedAtUtc")
    if not isinstance(captured, str) or not captured.endswith("Z"):
        raise ValueError(f"record {number} capturedAtUtc must be UTC")
    try:
        datetime.fromisoformat(captured[:-1] + "+00:00")
    except ValueError as exc:
        raise ValueError(f"record {number} has invalid capturedAtUtc") from exc
    grid_id = record.get("gridEntityId")
    if grid_id is not None and (not isinstance(grid_id, int) or isinstance(grid_id, bool) or grid_id <= 0):
        raise ValueError(f"record {number} has invalid gridEntityId")
    for field in ("simulationTick", "droppedRecordsTotal"):
        value = record.get(field)
        if value is not None and (not isinstance(value, int) or isinstance(value, bool) or value < 0):
            raise ValueError(f"record {number} has invalid {field}")
    for field in ("simulationTimeSeconds",):
        value = record.get(field)
        if value is not None and (not finite(value) or value < 0):
            raise ValueError(f"record {number} has invalid {field}")
    uncertainty = record.get("simulationTimeUncertaintySeconds")
    if uncertainty is not None and (not finite(uncertainty) or uncertainty < 0):
        raise ValueError(f"record {number} has invalid simulationTimeUncertaintySeconds")
    section_names = ("humanInput", "actuators", "physics", "pose", "gridState", "softwareGeometry", "visualFrame")
    for name in section_names:
        section = record.get(name)
        if not isinstance(section, dict) or section.get("status") not in ("observed", "partial", "unavailable"):
            raise ValueError(f"record {number} has invalid {name} section")
        status, value, reason = section["status"], section.get("value"), section.get("reason")
        if status == "unavailable" and (value is not None or not reason):
            raise ValueError(f"record {number} has malformed unavailable {name}")
        if status == "observed" and (value is None or reason is not None):
            raise ValueError(f"record {number} has malformed observed {name}")
        if status == "partial" and (value is None or not reason):
            raise ValueError(f"record {number} has malformed partial {name}")
        if value is not None and not isinstance(value, dict):
            raise ValueError(f"record {number} has invalid {name} value")
    physics = record["physics"].get("value") or {}
    for field in ("velocityX", "velocityY", "velocityZ", "speedMetersPerSecond", "simulationRatio"):
        value = physics.get(field)
        if value is not None and (not finite(value) or field in ("speedMetersPerSecond", "simulationRatio") and value < 0):
            raise ValueError(f"record {number} has invalid physics field {field}")
    if physics.get("physicsStepCount") is not None and (
            not isinstance(physics["physicsStepCount"], int) or isinstance(physics["physicsStepCount"], bool) or
            physics["physicsStepCount"] < 0):
        raise ValueError(f"record {number} has invalid physicsStepCount")
    human_input = record["humanInput"].get("value") or {}
    input_source = human_input.get("source")
    if input_source is not None and (not isinstance(input_source, str) or not input_source or len(input_source) > 128):
        raise ValueError(f"record {number} has invalid humanInput source")
    for field in ("throttle", "steering", "brake"):
        value = human_input.get(field)
        if value is not None and not finite(value):
            raise ValueError(f"record {number} has invalid humanInput field {field}")
    pose = record["pose"].get("value") or {}
    for field in ("positionX", "positionY", "positionZ", "rightX", "rightY", "rightZ",
                  "upX", "upY", "upZ", "forwardX", "forwardY", "forwardZ"):
        value = pose.get(field)
        if value is not None and not finite(value):
            raise ValueError(f"record {number} has invalid pose field {field}")
    actuators = record["actuators"].get("value") or {}
    items = actuators.get("items", [])
    if not isinstance(items, list) or len(items) > MAX_ACTUATOR_ITEMS:
        raise ValueError(f"record {number} exceeds the {MAX_ACTUATOR_ITEMS} actuator item limit")
    if record["actuators"]["status"] != "unavailable" and actuators.get("ownership") != "observed_only_no_control":
        raise ValueError(f"record {number} actuator telemetry is not explicitly observation-only")
    actuator_keys = set()
    for item in items:
        if (not isinstance(item, dict) or not isinstance(item.get("entityId"), int) or
                isinstance(item.get("entityId"), bool) or item["entityId"] <= 0 or
                not isinstance(item.get("kind"), str) or not item["kind"] or len(item["kind"]) > 80 or
                item.get("overrideValue") is not None and not finite(item["overrideValue"])):
            raise ValueError(f"record {number} has invalid actuator observation")
        key = (item["entityId"], item["kind"])
        if key in actuator_keys:
            raise ValueError(f"record {number} has duplicate actuator observation identity {key}")
        actuator_keys.add(key)


def section_values(record, name):
    section = record.get(name) or {}
    return section.get("value") if section.get("status") in ("observed", "partial") else None


def segments(records, getter):
    result, current = [], []
    for record in records:
        value = getter(record)
        if record["_missingBefore"] or value is None or not finite(value):
            if current:
                result.append(current)
                current = []
        if value is not None and finite(value):
            current.append((record["sequence"], value))
    if current:
        result.append(current)
    return result


def svg_series(records, definitions, title, unit, width=900, height=220):
    series = [(name, color, segments(records, getter)) for name, color, getter in definitions]
    values = [value for _, _, groups in series for group in groups for _, value in group]
    if not values:
        return f"<section><h3>{html.escape(title)}</h3><p>No observed values.</p></section>"
    low, high = min(values), max(values)
    if low == high:
        padding = max(abs(low) * 0.1, 0.1)
        low, high = low - padding, high + padding
    left, right, top, bottom = 58, width - 18, 14, height - 35
    times = {record["sequence"]: datetime.fromisoformat(record["capturedAtUtc"][:-1] + "+00:00").timestamp()
             for record in records}
    capture_times = {record["sequence"]: record["capturedAtUtc"] for record in records}
    first_time, last_time = min(times.values()), max(times.values())
    time_span = last_time - first_time
    y = lambda value: bottom - (value - low) / (high - low) * (bottom - top)
    x = lambda sequence: left + ((times[sequence] - first_time) / time_span if time_span > 0 else 0.5) * (right - left)
    out = [f'<section><h3>{html.escape(title)}</h3><svg role="img" aria-label="{html.escape(title)}" viewBox="0 0 {width} {height}">',
           f'<path d="M{left},{top}V{bottom}H{right}" fill="none" stroke="#64748b"/>',
           f'<text x="4" y="15">{high:.4g} {html.escape(unit)}</text>',
           f'<text x="4" y="{bottom}">{low:.4g} {html.escape(unit)}</text>',
           f'<text x="{left}" y="{height - 8}">UTC elapsed 0 s</text>',
           f'<text x="{right - 140}" y="{height - 8}">UTC elapsed {time_span:.3f} s</text>']
    for record in records:
        if record["_missingBefore"]:
            px = x(record["sequence"])
            out.append(f'<path d="M{px:.1f},{top}V{bottom}" stroke="#dc2626" stroke-dasharray="4 3" opacity=".55"/>')
    for name, color, groups in series:
        for group in groups:
            points = " ".join(f"{x(seq):.1f},{y(value):.1f}" for seq, value in group)
            if len(group) > 1:
                out.append(f'<polyline points="{points}" fill="none" stroke="{color}" stroke-width="2"/>')
            for seq, value in group:
                out.append(f'<circle cx="{x(seq):.1f}" cy="{y(value):.1f}" r="2.4" fill="{color}"><title>{html.escape(name)} · seq {seq} · UTC {html.escape(capture_times[seq])}: {value:.6g}</title></circle>')
    out.append("</svg><ul class=legend>")
    for name, color, groups in series:
        if groups:
            out.append(f'<li><span style="color:{color}">●</span> {html.escape(name)}</li>')
    out.append("</ul></section>")
    return "".join(out)


def trajectory_svg(records, three_d=False, width=640, height=420):
    title = "Observed 3D chassis trajectory (oblique projection)" if three_d else "Observed top-down chassis trajectory (world X–Z)"
    positions = []
    for record in records:
        pose = section_values(record, "pose") or {}
        coords = [pose.get("positionX"), pose.get("positionY"), pose.get("positionZ")]
        positions.append(coords if all(finite(value) for value in coords) else None)
    projected = []
    for point in positions:
        if point is None:
            projected.append(None)
        elif three_d:
            x, y, z = point
            projected.append((x - z * 0.55, y + z * 0.35))
        else:
            projected.append((point[0], point[2]))
    observed = [p for p in projected if p is not None]
    if not observed:
        return f"<section><h3>{html.escape(title)}</h3><p>Pose unavailable: no complete observed position vectors.</p></section>"
    xs, ys = [p[0] for p in observed], [p[1] for p in observed]
    xmin, xmax, ymin, ymax = min(xs), max(xs), min(ys), max(ys)
    dx, dy = max(xmax - xmin, 0.1), max(ymax - ymin, 0.1)
    margin = 35
    tx = lambda value: margin + (value - xmin) / dx * (width - 2 * margin)
    ty = lambda value: height - margin - (value - ymin) / dy * (height - 2 * margin)
    out = [f'<section><h3>{html.escape(title)}</h3><svg role="img" aria-label="{html.escape(title)}" viewBox="0 0 {width} {height}">',
           f'<rect x="1" y="1" width="{width - 2}" height="{height - 2}" fill="url(#unknownHatch)" stroke="#cbd5e1"/>',
           '<defs><pattern id="unknownHatch" width="8" height="8" patternTransform="rotate(45)" patternUnits="userSpaceOnUse"><line x1="0" y1="0" x2="0" y2="8" stroke="#e2e8f0" stroke-width="2"/></pattern><marker id="headingArrow" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto"><path d="M0,0 L6,3 L0,6 z" fill="#0f766e"/></marker></defs>',
           f'<text x="12" y="20" font-size="12">{html.escape("X / Z, meters" if not three_d else "X−0.55Z / Y+0.35Z, meters")}</text>']
    group = []
    arrow_stride = max(1, len(records) // 30)
    for index, point in enumerate(projected):
        record = records[index]
        if record["_missingBefore"] or point is None:
            if len(group) > 1:
                pts = " ".join(f"{tx(px):.1f},{ty(py):.1f}" for px, py in group)
                out.append(f'<polyline points="{pts}" fill="none" stroke="#2563eb" stroke-width="2.5"/>')
            group = []
        if point is not None:
            group.append(point)
            out.append(f'<circle cx="{tx(point[0]):.1f}" cy="{ty(point[1]):.1f}" r="3" fill="#1d4ed8"><title>sequence {record["sequence"]}; UTC {html.escape(record["capturedAtUtc"])}</title></circle>')
            pose = section_values(record, "pose") or {}
            fx, fz = pose.get("forwardX"), pose.get("forwardZ")
            fy = pose.get("forwardY")
            heading_complete = finite(fx) and finite(fz) and (not three_d or finite(fy))
            if index % arrow_stride == 0 and heading_complete:
                heading_x, heading_y = (fx - 0.55 * fz, fy + 0.35 * fz) if three_d else (fx, fz)
                norm = math.hypot(heading_x, heading_y)
                if norm > 1e-9:
                    sx, sy = tx(point[0]), ty(point[1])
                    ex, ey = sx + 13 * heading_x / norm, sy - 13 * heading_y / norm
                    out.append(f'<line x1="{sx:.1f}" y1="{sy:.1f}" x2="{ex:.1f}" y2="{ey:.1f}" stroke="#0f766e" stroke-width="1.6" marker-end="url(#headingArrow)"><title>observed forward vector at sequence {record["sequence"]}</title></line>')
            if record["_missingBefore"]:
                out.append(f'<text x="{tx(point[0]) + 5:.1f}" y="{ty(point[1]) - 6:.1f}" fill="#b91c1c">gap {record["_missingBefore"]}</text>')
    if len(group) > 1:
        pts = " ".join(f"{tx(px):.1f},{ty(py):.1f}" for px, py in group)
        out.append(f'<polyline points="{pts}" fill="none" stroke="#2563eb" stroke-width="2.5"/>')
    out.append("</svg><p>Only observed chassis positions are plotted. Hatched background means geometry is unknown; no terrain, voxels, obstacles, or camera imagery are inferred.</p></section>")
    return "".join(out)


def render(records, input_name):
    first = records[0]
    run_id, session_id, stream_id, grid_id = first["runId"], first["sessionId"], first["streamId"], first.get("gridEntityId")
    gaps = sum(item["_missingBefore"] for item in records)
    reported_drops = [item["droppedRecordsTotal"] for item in records
                      if item.get("droppedRecordsTotal") is not None]
    drops = (f"cumulative maximum {max(reported_drops)}" if reported_drops
             else "unavailable (producer counter not reported)")
    times = [datetime.fromisoformat(item["capturedAtUtc"][:-1] + "+00:00").timestamp() for item in records]
    intervals = [b - a for a, b in zip(times, times[1:])]
    span = f"{times[-1] - times[0]:.3f} s UTC span" if len(times) > 1 else "single UTC sample"
    cadence = f"observed UTC intervals {min(intervals):.3f}–{max(intervals):.3f} s" if intervals else "no interval estimate"
    clock_domains = ", ".join(sorted({item["clockDomain"] for item in records}))
    sections = ("humanInput", "actuators", "physics", "pose", "gridState", "softwareGeometry", "visualFrame")
    expected_fields = {
        "humanInput": ("throttle", "steering", "brake", "source"),
        "actuators": ("items", "ownership"),
        "physics": ("velocityX", "velocityY", "velocityZ", "speedMetersPerSecond", "simulationRatio", "physicsStepCount"),
        "pose": ("positionX", "positionY", "positionZ", "rightX", "rightY", "rightZ", "upX", "upY", "upZ", "forwardX", "forwardY", "forwardZ"),
        "gridState": ("isStatic", "gridSizeClass", "blockCount", "blockCoverageComplete"),
        "softwareGeometry": ("coverage", "gridCount", "blockCount", "terrainIncluded", "unrelatedEntitiesIncluded", "uncertainty"),
        "visualFrame": ("renderer", "frameStatus", "imageSha256", "width", "height", "rendererClockDomain"),
    }
    status_rows = []
    for name in sections:
        counts = {state: sum(record[name]["status"] == state for record in records) for state in ("observed", "partial", "unavailable")}
        reasons = sorted({record[name].get("reason") for record in records if record[name].get("reason")})
        missing = set()
        for record in records:
            value = (record[name].get("value") or {}) if record[name]["status"] != "unavailable" else {}
            missing.update(field for field in expected_fields[name] if value.get(field) is None)
        missing_text = ", ".join(sorted(missing)) or "none in inspected fields"
        status_rows.append(f"<tr><th>{html.escape(name)}</th><td>{counts['observed']}</td><td>{counts['partial']}</td><td>{counts['unavailable']}</td><td>{html.escape(missing_text)}</td><td>{html.escape('; '.join(reasons) or '—')}</td></tr>")

    definitions = [
        ("speed m/s", "#2563eb", lambda r: (section_values(r, "physics") or {}).get("speedMetersPerSecond")),
        ("velocity X m/s", "#16a34a", lambda r: (section_values(r, "physics") or {}).get("velocityX")),
        ("velocity Y m/s", "#9333ea", lambda r: (section_values(r, "physics") or {}).get("velocityY")),
        ("velocity Z m/s", "#ea580c", lambda r: (section_values(r, "physics") or {}).get("velocityZ")),
    ]
    parts = [trajectory_svg(records), trajectory_svg(records, three_d=True), svg_series(records, definitions, "Observed physics telemetry", "m/s")]
    actuator_keys = sorted({(item["entityId"], item["kind"]) for record in records
                            for item in ((section_values(record, "actuators") or {}).get("items") or [])})
    if len(actuator_keys) > MAX_SERIES:
        raise ValueError(f"capture has {len(actuator_keys)} actuator series; viewer limit is {MAX_SERIES}")
    actuator_definitions = []
    palette = ("#0f766e", "#c2410c", "#7e22ce", "#0369a1", "#a16207", "#be123c")
    for index, (entity_id, kind) in enumerate(actuator_keys):
        label = f"entity {entity_id} · {kind} · overrideValue (channel meaning unspecified)"
        actuator_definitions.append((label, palette[index % len(palette)],
            lambda r, eid=entity_id, k=kind: next((item.get("overrideValue") for item in
                ((section_values(r, "actuators") or {}).get("items") or [])
                if item.get("entityId") == eid and item.get("kind") == k), None)))
    if actuator_definitions:
        parts.append(svg_series(records, actuator_definitions,
            "Observed actuator override values (schema does not identify steering vs propulsion)", "reported value"))
    for channel, color in (("throttle", "#2563eb"), ("steering", "#16a34a"), ("brake", "#dc2626")):
        parts.append(svg_series(records, [(f"reported input {channel}", color,
            lambda r, ch=channel: ((section_values(r, "humanInput") or {}).get(ch)))],
            f"Reported input telemetry: {channel}", "reported value"))

    event_rows = []
    timing_rows = []
    for record in records:
        timing_rows.append(f"<tr><td>{record['sequence']}</td><td>{html.escape(record['capturedAtUtc'])}</td><td>{html.escape(record['clockDomain'])}</td><td>{record.get('simulationTick') if record.get('simulationTick') is not None else 'unavailable'}</td><td>{record.get('simulationTimeSeconds') if record.get('simulationTimeSeconds') is not None else 'unavailable'}</td><td>{record['_missingBefore']}</td><td>{record.get('droppedRecordsTotal') if record.get('droppedRecordsTotal') is not None else 'unavailable'}</td></tr>")
        if record["_missingBefore"] or record.get("_dropDelta"):
            event_rows.append(f"<tr><td>{record['sequence']}</td><td>{html.escape(record['capturedAtUtc'])}</td><td>{record['_missingBefore']}</td><td>{record.get('_dropDelta') if record.get('_dropDelta') is not None else 'unknown'}</td><td>{record.get('droppedRecordsTotal') if record.get('droppedRecordsTotal') is not None else 'unavailable'}</td></tr>")
    if not event_rows:
        if reported_drops:
            event_rows.append("<tr><td colspan=5>No sequence gaps or drop-count increases reported; producer counter was present.</td></tr>")
        else:
            event_rows.append("<tr><td colspan=5>No sequence gaps observed; producer drop counter unavailable.</td></tr>")
    grid_text = str(grid_id) if grid_id is not None else "unavailable"
    return f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Driving Lab replay · {html.escape(run_id)}</title>
<style>body{{font:15px system-ui,sans-serif;max-width:1050px;margin:2rem auto;padding:0 1rem;color:#172033}}h1,h2,h3{{line-height:1.2}}section{{border:1px solid #cbd5e1;border-radius:8px;padding:1rem;margin:1rem 0;overflow:auto}}svg{{width:100%;max-height:480px;background:#fff}}table{{border-collapse:collapse;width:100%;font-size:.9rem}}th,td{{border:1px solid #cbd5e1;padding:.4rem;text-align:left}}.legend{{display:flex;gap:1.2rem;flex-wrap:wrap;list-style:none;padding:0}}.warning{{background:#fff7ed;border-left:4px solid #ea580c;padding:.8rem}}</style>
<h1>Driving Lab read-only replay</h1><p>Source: <code>{html.escape(input_name)}</code>. Schema v{SCHEMA_VERSION}. Static export; no game connection, execution, interpolation, or control path.</p>
<section><h2>Capture identity</h2><table><tr><th>Run</th><td>{html.escape(run_id)}</td></tr><tr><th>Session</th><td>{html.escape(session_id)}</td></tr><tr><th>Stream</th><td>{html.escape(stream_id)}</td></tr><tr><th>Grid entity</th><td>{html.escape(grid_text)}</td></tr><tr><th>Records</th><td>{len(records)}</td></tr><tr><th>Sequence gaps</th><td>{gaps} missing sequence numbers; paths are broken at gaps</td></tr><tr><th>Dropped records</th><td>{drops}; per-record counters appear below</td></tr></table></section>
<p class="warning"><b>Timing boundary:</b> Recorded clock domain(s): <code>{html.escape(clock_domains)}</code>. {html.escape(span)}; {html.escape(cadence)}. Physics/control charts use recorded UTC timestamps; simulation ticks and seconds are listed separately and are not synchronized here. The schema provides no calibrated timestamp error bound, so cross-source alignment and sub-sample timing are unknown.</p>
{''.join(parts)}
<section><h2>Channel availability</h2><table><tr><th>Section</th><th>Observed records</th><th>Partial records</th><th>Unavailable records</th><th>Fields missing or null in inspected records</th><th>Reasons</th></tr>{''.join(status_rows)}</table><p>Missing-field lists summarize fields absent in at least one record, including whole unavailable sections.</p></section>
<section><h2>Recorded sample times</h2><details><summary>Show {len(records)} per-record timing entries</summary><table><tr><th>Sequence</th><th>Captured UTC</th><th>Clock domain</th><th>Simulation tick</th><th>Simulation seconds</th><th>Missing before</th><th>Cumulative dropped</th></tr>{''.join(timing_rows)}</table></details></section>
<section><h2>Gaps and drop counters</h2><table><tr><th>Sequence</th><th>UTC</th><th>Missing before</th><th>Drop delta</th><th>Cumulative dropped</th></tr>{''.join(event_rows)}</table></section>
<section><h2>What these views represent</h2><p>Top-down uses observed world X/Z position. The oblique view projects observed X/Y/Z position; it is a geometric pose trace, not a rendered world. Both stop at missing pose samples and sequence gaps. Background geometry, terrain, voxels, obstacles, collisions, camera view, and human intent are unknown unless explicitly present as observed channels; nothing is filled in or inferred.</p></section></html>'''


def aligned_times(records, mode, offset_seconds=0.0):
    if mode not in ("relative-simulation-time", "explicit-start-offset"):
        raise ValueError("comparison requires an explicit supported alignment policy")
    first = records[0].get("simulationTimeSeconds")
    if first is None:
        raise ValueError("first record in each comparison stream needs simulationTimeSeconds for alignment")
    result, previous = [], None
    for record in records:
        value = record.get("simulationTimeSeconds")
        if value is None:
            result.append(None)
            continue
        if previous is not None and value < previous:
            raise ValueError("simulationTimeSeconds moves backwards within a comparison stream")
        previous = value
        result.append(value - first + offset_seconds)
    return result


def timing_uncertainty(records, offset_uncertainty=None):
    uncertainties = [record.get("simulationTimeUncertaintySeconds") for record in records]
    if any(value is None for value in uncertainties):
        message = "unknown (capture has no declared simulation-time error bound)"
        if offset_uncertainty is not None:
            message += f"; declared offset uncertainty {offset_uncertainty:.6g} s"
        return message
    bound = 2 * max(uncertainties, default=0.0)
    if offset_uncertainty is not None:
        bound += offset_uncertainty
        return f"at most {bound:.6g} s including start-offset uncertainty"
    return f"at most {bound:.6g} s from sample timestamps"


def comparison_svg(title, series, unit, width=900, height=240):
    # series: [(label, records, aligned_times, color, accessor)]
    plotted, values, x_values = [], [], []
    for label, records, times, color, accessor in series:
        groups, group = [], []
        for record, x_value in zip(records, times):
            value = accessor(record)
            if record["_missingBefore"] or x_value is None or not finite(value):
                if group:
                    groups.append(group)
                    group = []
            if x_value is not None and finite(value):
                group.append((x_value, value, record))
        if group:
            groups.append(group)
        plotted.append((label, color, groups))
        values.extend(point[1] for points in groups for point in points)
        x_values.extend(point[0] for points in groups for point in points)
    if not values:
        return f"<section><h3>{html.escape(title)}</h3><p>No aligned observed values.</p></section>"
    low, high = min(values), max(values)
    if low == high:
        padding = max(abs(low) * 0.1, 0.1)
        low, high = low - padding, high + padding
    x_min, x_max = min(x_values), max(x_values)
    if x_min == x_max:
        x_max += 1.0
    left, right, top, bottom = 64, width - 18, 16, height - 36
    x = lambda value: left + (value - x_min) / (x_max - x_min) * (right - left)
    y = lambda value: bottom - (value - low) / (high - low) * (bottom - top)
    out = [f'<section><h3>{html.escape(title)}</h3><svg role="img" aria-label="{html.escape(title)}" viewBox="0 0 {width} {height}">',
           f'<path d="M{left},{top}V{bottom}H{right}" fill="none" stroke="#64748b"/>',
           f'<text x="4" y="16">{high:.4g} {html.escape(unit)}</text>',
           f'<text x="4" y="{bottom}">{low:.4g} {html.escape(unit)}</text>',
           f'<text x="{left}" y="{height - 8}">{x_min:.4g} aligned seconds</text>',
           f'<text x="{right - 110}" y="{height - 8}">{x_max:.4g} aligned seconds</text>']
    for label, color, groups in plotted:
        for group in groups:
            points = " ".join(f"{x(t):.1f},{y(v):.1f}" for t, v, _ in group)
            if len(group) > 1:
                out.append(f'<polyline points="{points}" fill="none" stroke="{color}" stroke-width="2"/>')
            for t, value, record in group:
                out.append(f'<circle cx="{x(t):.1f}" cy="{y(value):.1f}" r="2.8" fill="{color}"><title>{html.escape(label)} · sequence {record["sequence"]} · aligned {t:.6g} s · value {value:.6g}</title></circle>')
    out.append("</svg><ul class=legend>")
    for label, color, groups in plotted:
        if groups:
            out.append(f'<li><span style="color:{color}">●</span> {html.escape(label)}</li>')
    out.append("</ul><p>Observed samples only. Lines break at missing values and sequence gaps; no interpolation is performed.</p></section>")
    return "".join(out)


def comparison_identity(role, records, filename):
    first = records[0]
    input_sources = sorted({(section_values(record, "humanInput") or {}).get("source")
                            for record in records
                            if (section_values(record, "humanInput") or {}).get("source")})
    source_text = ", ".join(input_sources) if input_sources else "not reported"
    reported_drops = [r["droppedRecordsTotal"] for r in records if r.get("droppedRecordsTotal") is not None]
    drop_summary = str(max(reported_drops)) if reported_drops else "unavailable (producer counter not reported)"
    return (f"<h3>{html.escape(role)}</h3><table><tr><th>File</th><td>{html.escape(filename)}</td></tr>"
            f"<tr><th>Run</th><td>{html.escape(first['runId'])}</td></tr>"
            f"<tr><th>Session</th><td>{html.escape(first['sessionId'])}</td></tr>"
            f"<tr><th>Stream</th><td>{html.escape(first['streamId'])}</td></tr>"
            f"<tr><th>Grid</th><td>{first.get('gridEntityId') if first.get('gridEntityId') is not None else 'unavailable'}</td></tr>"
            f"<tr><th>Producer source</th><td>{html.escape(first['source'])}</td></tr>"
            f"<tr><th>Reported humanInput source</th><td>{html.escape(source_text)}</td></tr>"
            f"<tr><th>Records / gaps / drops</th><td>{len(records)} / {sum(r['_missingBefore'] for r in records)} / {drop_summary}</td></tr></table>")


def comparison_gap_table(role, records):
    rows = []
    previous_drops = None
    for record in records:
        dropped = record.get("droppedRecordsTotal")
        drop_delta = ("unknown" if dropped is None else
                      (str(dropped) if previous_drops is None else str(dropped - previous_drops)))
        if record["_missingBefore"] or drop_delta not in ("unknown", "0"):
            rows.append(f"<tr><td>{html.escape(role)}</td><td>{record['sequence']}</td><td>{record['_missingBefore']}</td><td>{drop_delta}</td><td>{dropped if dropped is not None else 'unavailable'}</td><td>{record.get('simulationTick') if record.get('simulationTick') is not None else 'unavailable'}</td></tr>")
        if dropped is not None:
            previous_drops = dropped
    if not rows:
        any_counter = any(record.get("droppedRecordsTotal") is not None for record in records)
        message = ("No sequence gaps or drop-count increases reported; producer counter was present."
                   if any_counter else "No sequence gaps observed; producer drop counter unavailable.")
        rows.append(f"<tr><td>{html.escape(role)}</td><td colspan=5>{message}</td></tr>")
    return "".join(rows)


def actuator_observation_panel(role, records):
    keys = sorted({(item["entityId"], item["kind"]) for record in records
                   for item in ((section_values(record, "actuators") or {}).get("items") or [])})
    if len(keys) > MAX_SERIES:
        raise ValueError(f"{role} stream has {len(keys)} actuator series; viewer limit is {MAX_SERIES}")
    palette = ("#0f766e", "#c2410c", "#7e22ce", "#0369a1", "#a16207", "#be123c")
    definitions = []
    for index, (entity_id, kind) in enumerate(keys):
        label = f"entity {entity_id} · {kind} · overrideValue (channel meaning unspecified)"
        definitions.append((label, palette[index % len(palette)], lambda r, eid=entity_id, k=kind:
            next((item.get("overrideValue") for item in ((section_values(r, "actuators") or {}).get("items") or [])
                  if item.get("entityId") == eid and item.get("kind") == k), None)))
    return svg_series(records, definitions,
        f"{role}: observed actuator values, not aligned across streams", "reported value")


def render_comparison(human_records, attempt_records, human_name, attempt_name,
                      alignment, human_offset=0.0, attempt_offset=0.0,
                      human_offset_uncertainty=None, attempt_offset_uncertainty=None):
    if not human_records or not attempt_records:
        raise ValueError("comparison requires two nonempty streams")
    human_identity = (human_records[0]["runId"], human_records[0]["sessionId"],
                      human_records[0]["streamId"], human_records[0].get("gridEntityId"))
    attempt_identity = (attempt_records[0]["runId"], attempt_records[0]["sessionId"],
                        attempt_records[0]["streamId"], attempt_records[0].get("gridEntityId"))
    if human_identity == attempt_identity:
        raise ValueError("comparison inputs have the same run/session/stream/grid identity")
    if alignment == "explicit-start-offset":
        values = (human_offset, attempt_offset, human_offset_uncertainty, attempt_offset_uncertainty)
        if any(value is None or not finite(value) for value in values):
            raise ValueError("explicit-start-offset requires finite offsets and uncertainty values for both streams")
        if human_offset_uncertainty < 0 or attempt_offset_uncertainty < 0:
            raise ValueError("start-offset uncertainty values must be nonnegative")
    elif any(value is not None for value in (human_offset_uncertainty, attempt_offset_uncertainty)) or \
            human_offset != 0.0 or attempt_offset != 0.0:
        raise ValueError("start offsets and uncertainty values require explicit-start-offset alignment")
    human_time = aligned_times(human_records, alignment, human_offset)
    attempt_time = aligned_times(attempt_records, alignment, attempt_offset)
    speeds = [
        ("Human demonstration · observed speed", human_records, human_time, "#2563eb",
         lambda r: (section_values(r, "physics") or {}).get("speedMetersPerSecond")),
        ("Voidwright attempt · observed speed", attempt_records, attempt_time, "#c2410c",
         lambda r: (section_values(r, "physics") or {}).get("speedMetersPerSecond")),
    ]
    inputs = []
    colors = {"throttle": "#2563eb", "steering": "#16a34a", "brake": "#dc2626"}
    for role, records, times in (("Human demonstration", human_records, human_time),
                                 ("Voidwright attempt", attempt_records, attempt_time)):
        for channel in ("throttle", "steering", "brake"):
            sources = sorted({(section_values(r, "humanInput") or {}).get("source") or "source not reported"
                              for r in records if finite((section_values(r, "humanInput") or {}).get(channel))})
            for input_source in sources:
                inputs.append((f"{role} · reported source={input_source} · humanInput.{channel}", records, times,
                    colors[channel], lambda r, ch=channel, source=input_source:
                        (section_values(r, "humanInput") or {}).get(ch)
                        if ((section_values(r, "humanInput") or {}).get("source") or "source not reported") == source else None))
    if len(inputs) > MAX_SERIES:
        raise ValueError(f"comparison has {len(inputs)} input series; viewer limit is {MAX_SERIES}")
    parts = [comparison_svg("Observed speed on the selected alignment", speeds, "m/s")]
    if inputs:
        parts.append(comparison_svg("Explicitly reported input channels", inputs, "reported value"))
    else:
        parts.append("<section><h2>Input comparison</h2><p>No throttle, steering, or brake values were explicitly recorded in either humanInput section. Control source is not inferred from actuator telemetry.</p></section>")
    parts.extend(("<section><h2>Human demonstration trajectory</h2>" + trajectory_svg(human_records) + "</section>",
                  "<section><h2>Voidwright attempt trajectory</h2>" + trajectory_svg(attempt_records) + "</section>"))
    parts.append("<section><h2>Sequence gaps and producer drops</h2><table><tr><th>Stream</th><th>Sequence</th><th>Missing before</th><th>Drop delta</th><th>Cumulative dropped</th><th>Simulation tick</th></tr>" +
        comparison_gap_table("Human demonstration", human_records) + comparison_gap_table("Voidwright attempt", attempt_records) + "</table></section>")
    parts.append(actuator_observation_panel("Human demonstration", human_records))
    parts.append(actuator_observation_panel("Voidwright attempt", attempt_records))
    human_uncertainty = timing_uncertainty(human_records, human_offset_uncertainty if alignment == "explicit-start-offset" else None)
    attempt_uncertainty = timing_uncertainty(attempt_records, attempt_offset_uncertainty if alignment == "explicit-start-offset" else None)
    offset_text = (f"Human start offset {human_offset:.6g} s; attempt start offset {attempt_offset:.6g} s."
                   if alignment == "explicit-start-offset" else "Each stream starts at its first recorded simulation-time sample.")
    return f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Driving Lab comparison</title>
<style>body{{font:15px system-ui,sans-serif;max-width:1100px;margin:2rem auto;padding:0 1rem;color:#172033}}h1,h2,h3{{line-height:1.2}}section{{border:1px solid #cbd5e1;border-radius:8px;padding:1rem;margin:1rem 0;overflow:auto}}svg{{width:100%;max-height:480px;background:#fff}}table{{border-collapse:collapse;width:100%;font-size:.9rem}}th,td{{border:1px solid #cbd5e1;padding:.4rem;text-align:left}}.streams{{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:1rem}}.legend{{display:flex;gap:1.2rem;flex-wrap:wrap;list-style:none;padding:0}}</style>
<h1>Driving Lab read-only comparison</h1><p>Static export; no game connection, execution, interpolation, or control path. Stream identities and producer/control-source labels remain separate.</p>
<section><h2>Stream identities</h2><div class="streams">{comparison_identity('Human demonstration', human_records, human_name)}{comparison_identity('Voidwright attempt', attempt_records, attempt_name)}</div></section>
<section><h2>Explicit alignment</h2><p>Policy: <code>{html.escape(alignment)}</code>. Simulation time is aligned independently; UTC timestamps are not used to synchronize captures. {offset_text}</p><p>Human timing uncertainty: {html.escape(human_uncertainty)}. Attempt timing uncertainty: {html.escape(attempt_uncertainty)}. Unknown is not treated as zero.</p><p>Separate run/session/grid identities are expected. Trajectories are shown separately because a shared world-coordinate frame has not been established.</p></section>
{''.join(parts)}</html>'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path, nargs="?", help="one versioned Driving Lab JSONL stream")
    parser.add_argument("output", type=Path, help="static HTML output path")
    parser.add_argument("--compare-human", type=Path, help="human demonstration JSONL stream")
    parser.add_argument("--compare-attempt", type=Path, help="later Voidwright attempt JSONL stream")
    parser.add_argument("--alignment", choices=("relative-simulation-time", "explicit-start-offset"),
                        help="required in comparison mode; UTC synchronization is never inferred")
    parser.add_argument("--human-start-offset-seconds", type=float)
    parser.add_argument("--attempt-start-offset-seconds", type=float)
    parser.add_argument("--human-offset-uncertainty-seconds", type=float)
    parser.add_argument("--attempt-offset-uncertainty-seconds", type=float)
    args = parser.parse_args()
    try:
        comparison = args.compare_human is not None or args.compare_attempt is not None
        if comparison:
            if args.capture is not None or args.compare_human is None or args.compare_attempt is None or not args.alignment:
                raise ValueError("comparison mode requires exactly --compare-human, --compare-attempt, and --alignment")
            offset_values = (args.human_start_offset_seconds, args.attempt_start_offset_seconds,
                             args.human_offset_uncertainty_seconds, args.attempt_offset_uncertainty_seconds)
            if args.alignment == "explicit-start-offset":
                if any(value is None or not finite(value) for value in offset_values):
                    raise ValueError("explicit-start-offset requires finite offsets and uncertainty values for both streams")
                if args.human_offset_uncertainty_seconds < 0 or args.attempt_offset_uncertainty_seconds < 0:
                    raise ValueError("start-offset uncertainty values must be nonnegative")
            elif any(value is not None for value in offset_values):
                raise ValueError("start offsets may be supplied only with --alignment explicit-start-offset")
            human_records = load_capture(args.compare_human)
            attempt_records = load_capture(args.compare_attempt)
            if len(human_records) + len(attempt_records) > MAX_RECORDS:
                raise ValueError(f"combined comparison exceeds the {MAX_RECORDS} record limit")
            document = render_comparison(human_records, attempt_records,
                args.compare_human.name, args.compare_attempt.name, args.alignment,
                args.human_start_offset_seconds or 0.0, args.attempt_start_offset_seconds or 0.0,
                args.human_offset_uncertainty_seconds, args.attempt_offset_uncertainty_seconds)
            record_count = len(human_records) + len(attempt_records)
        else:
            if args.capture is None or args.alignment or any(value is not None for value in (
                    args.human_start_offset_seconds, args.attempt_start_offset_seconds,
                    args.human_offset_uncertainty_seconds, args.attempt_offset_uncertainty_seconds)):
                raise ValueError("single-stream mode requires one capture and no comparison alignment options")
            records = load_capture(args.capture)
            document = render(records, args.capture.name)
            record_count = len(records)
        payload = document.encode("utf-8")
        if len(payload) > MAX_OUTPUT_BYTES:
            raise ValueError(f"rendered HTML exceeds {MAX_OUTPUT_BYTES} bytes")
        args.output.write_bytes(payload)
    except (OSError, ValueError) as exc:
        print(f"replay render failed: {exc}", file=sys.stderr)
        return 2
    print(f"wrote {args.output} ({len(payload)} bytes; {record_count} records)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
