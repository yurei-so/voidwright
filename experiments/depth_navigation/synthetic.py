"""Synthetic land-vehicle depth queries. Metres; world axes X right, Y up, Z front.

Depth is ray distance, not camera-Z. Every quadrant is one complete pinhole FOV.
Oracle labels use geometry independently of the depth renderer. They include
hidden hazards: a single depth image is not assumed to resolve occlusion.
"""
from dataclasses import dataclass, asdict
import math
import numpy as np

CAMERAS = ("front", "rear", "left", "right")
YAW = (0., math.pi, -math.pi / 2, math.pi / 2)
FAMILIES = ("flat", "slope", "step", "trench", "boulder", "wall", "overhang", "rough")


@dataclass
class Scene:
    family: str
    slope: float
    height: float
    distance: float
    lateral: float
    width: float
    length: float
    ceiling: float
    phase: float
    vehicle_width: float
    vehicle_height: float
    vehicle_length: float
    step_limit: float
    slope_limit: float
    heading: float
    reverse: bool
    geometry_yaw: float
    cameras: list


def sample_scene(rng, family=None, shift=False, loadout="front"):
    family = family or FAMILIES[int(rng.integers(len(FAMILIES)))]
    if family not in FAMILIES:
        raise ValueError("Unknown terrain family")
    heading = float(rng.uniform(-.85, .85)) if loadout == "front" else float(rng.uniform(-math.pi, math.pi))
    cameras = []
    for index, yaw in enumerate(YAW):
        active = index == 0 if loadout == "front" else bool(rng.random() > .35)
        cameras.append(dict(position=[float(rng.uniform(-.3, .3)), float(rng.uniform(1.1, 3.2) if not shift else rng.uniform(.7, 4.2)), 0.],
                            yaw=float(yaw + rng.uniform(-.15, .15)), pitch=float(rng.uniform(-.5, -.12)),
                            roll=float(rng.uniform(-.25, .25) if not shift else rng.uniform(-.7, .7)),
                            fov=float(rng.uniform(65, 95) if not shift else rng.uniform(55, 110)), valid=active))
    # Geometry can lie in any travel direction for the multi-camera condition.
    return Scene(family, float(rng.uniform(-.35, .35)), float(rng.uniform(.2, 2.8)),
                 float(rng.uniform(1.2, 7.5)), float(rng.uniform(-3.5, 3.5)),
                 float(rng.uniform(.6, 4)), float(rng.uniform(.5, 2.5)), float(rng.uniform(.8, 3.8)),
                 float(rng.uniform(0, 2 * math.pi)), float(rng.uniform(1.3, 3) if not shift else rng.uniform(3, 4.3)),
                 float(rng.uniform(1.2, 3)), float(rng.uniform(2.5, 5.5)), float(rng.uniform(.2, .5)), float(rng.uniform(.25, .5)),
                 heading, bool(rng.random() < .25) if loadout != "front" else False,
                 float(rng.uniform(-math.pi, math.pi)) if loadout != "front" else float(rng.uniform(-.25, .25)), cameras)


def geometry_coordinates(scene, x, z):
    angle = scene.geometry_yaw
    return x * math.cos(angle) - z * math.sin(angle), x * math.sin(angle) + z * math.cos(angle)


def terrain(scene, x, z):
    """Ground surface only; analytic boxes/ellipsoids are handled separately."""
    x, z = geometry_coordinates(scene, x, z)
    base = scene.slope * z if scene.family == "slope" else np.zeros_like(x + z)
    if scene.family == "step":
        base = np.where(z > scene.distance, scene.height, 0.)
    elif scene.family == "trench":
        base = np.where((z > scene.distance) & (z < scene.distance + scene.length), -scene.height, 0.)
    elif scene.family == "rough":
        base = .22 * np.sin(x * 1.7 + scene.phase) * np.sin(z * 1.4)
    return base


def solid(scene, x, y, z):
    x, z = geometry_coordinates(scene, x, z)
    if scene.family in ("wall", "overhang"):
        bottom = scene.ceiling if scene.family == "overhang" else 0.
        top = bottom + (1. if scene.family == "overhang" else scene.height)
        return ((np.abs(x - scene.lateral) <= scene.width / 2) &
                (np.abs(z - scene.distance) <= scene.length / 2) & (y >= bottom) & (y <= top))
    if scene.family == "boulder":
        return ((x - scene.lateral) / (scene.width / 2)) ** 2 + ((y - scene.height / 2) / (scene.height / 2)) ** 2 + ((z - scene.distance) / (scene.length / 2)) ** 2 <= 1
    return np.zeros_like(x + y + z, dtype=bool)


def frame_dimensions(resolution):
    width, height = (resolution, resolution) if isinstance(resolution, int) else resolution
    if not all(isinstance(value, int) and 8 <= value <= 96 for value in (width, height)):
        raise ValueError("Frame width/height must be integers 8..96")
    return width, height


def camera_rays(camera, resolution):
    width, height = frame_dimensions(resolution)
    if not 30 <= camera["fov"] <= 130:
        raise ValueError("FOV must be 30..130 degrees")
    horizontal = (np.arange(width) + .5) / width * 2 - 1
    vertical = 1 - (np.arange(height) + .5) / height * 2
    x, y = np.meshgrid(horizontal, vertical)
    tangent = math.tan(math.radians(camera["fov"]) / 2)
    local = np.stack((x * tangent, y * tangent * height / width, np.ones_like(x)), axis=-1)
    local /= np.linalg.norm(local, axis=-1, keepdims=True)
    yaw, pitch = camera["yaw"], camera["pitch"]
    forward = np.array([math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch)])
    right = np.array([math.cos(yaw), 0., -math.sin(yaw)])
    up = np.cross(forward, right)
    roll = camera.get("roll", 0.)
    right, up = right * math.cos(roll) + up * math.sin(roll), up * math.cos(roll) - right * math.sin(roll)
    return (local[..., :1] * right + local[..., 1:2] * up + local[..., 2:] * forward).astype(np.float32)


def render(scene, camera, resolution=32, max_range=16., step=.10):
    width, height = frame_dimensions(resolution)
    depth = np.ones((height, width), np.float32) * max_range
    validity = np.zeros_like(depth)
    if not camera["valid"]:
        return depth * 0, validity
    rays = camera_rays(camera, resolution)
    origin = np.asarray(camera["position"], np.float32)
    # Ray marching conservatively samples solid volume/ground. Finite depth
    # discretization and thin-geometry misses are recorded model limitations.
    pending = np.ones_like(depth, dtype=bool)
    for distance in np.arange(.2, max_range + step / 2, step):
        if not pending.any():
            break
        points = origin + rays * distance
        x, y, z = points.transpose(2, 0, 1)
        hit = pending & ((y <= terrain(scene, x, z)) | solid(scene, x, y, z))
        depth[hit] = distance
        pending[hit] = False
    # A valid max-range return is distinct from an unavailable pixel.
    validity[:] = 1
    return depth, validity


def oracle(scene, distance=4.):
    """Sample swept ground/support and vehicle body, not rendered depth."""
    along = np.linspace(-scene.vehicle_length / 2, distance + scene.vehicle_length / 2, 161)
    across = np.linspace(-scene.vehicle_width / 2, scene.vehicle_width / 2, 17)
    a, c = np.meshgrid(along, across, indexing="ij")
    angle = scene.heading
    x = a * math.sin(angle) + c * math.cos(angle)
    z = a * math.cos(angle) - c * math.sin(angle)
    heights = terrain(scene, x, z)
    # Smooth ground grade and discontinuous curb/drop height are different
    # limits. Dividing every curb by the sampling interval labels even a
    # climbable small step as an impossibly steep continuous slope.
    lx, lz = geometry_coordinates(scene, x, z)
    if scene.family == "slope":
        grade = np.full_like(heights, abs(scene.slope))
    elif scene.family == "rough":
        dx = .22 * 1.7 * np.cos(lx * 1.7 + scene.phase) * np.sin(lz * 1.4)
        dz = .22 * 1.4 * np.sin(lx * 1.7 + scene.phase) * np.cos(lz * 1.4)
        grade = np.hypot(dx, dz)
    else:
        grade = np.zeros_like(heights)
    unsafe = bool((np.abs(np.diff(heights, axis=0)) > scene.step_limit).any()
                  or (np.abs(np.diff(heights, axis=1)) > scene.step_limit).any()
                  or (grade > scene.slope_limit).any())
    # Ground-relative body occupancy allows low obstacles beneath the nominal
    # step limit and rejects ceilings lower than the vehicle's height.
    for fraction in np.linspace(0, 1, 13):
        y = heights + scene.step_limit + fraction * max(.01, scene.vehicle_height - scene.step_limit)
        if solid(scene, x, y, z).any():
            unsafe = True
    return float(unsafe)


def encode(scene, resolution=32, encoding="numeric", camera_loss=None, rendered=None):
    if encoding not in ("numeric", "strip"):
        raise ValueError("Unknown direction encoding")
    width, height = frame_dimensions(resolution)
    depth = np.zeros((height * 2 + 4, width * 2), np.float32)
    valid = np.zeros_like(depth)
    for index, camera in enumerate(scene.cameras):
        row, col = divmod(index, 2)
        d, m = render(scene, camera, resolution) if rendered is None else rendered[index].copy()
        if camera_loss == index:
            d[:], m[:] = 0, 0
        depth[row * height:(row + 1) * height, col * width:(col + 1) * width] = d / 16.
        valid[row * height:(row + 1) * height, col * width:(col + 1) * width] = m
    # The marker is outside all four depth frames, with a separate channel.
    marker = np.zeros_like(depth)
    if encoding == "strip":
        location = int(np.clip((scene.heading / math.pi + 1) / 2 * (width * 2 - 1), 0, width * 2 - 1))
        marker[-4:, location] = 1
    condition = [scene.vehicle_width / 4.3, scene.vehicle_height / 4, scene.vehicle_length / 6, scene.step_limit,
                 scene.slope_limit, float(scene.reverse)]
    condition += [math.sin(scene.heading), math.cos(scene.heading)] if encoding == "numeric" else [0., 0.]
    for index, camera in enumerate(scene.cameras):
        condition += [*(np.asarray(camera["position"]) / 4).tolist(), math.sin(camera["yaw"]), math.cos(camera["yaw"]),
                      math.sin(camera["pitch"]), math.cos(camera["pitch"]), math.sin(camera.get("roll", 0.)), math.cos(camera.get("roll", 0.)), camera["fov"] / 130,
                      float(camera["valid"] and camera_loss != index)]
    return np.stack((depth, valid, marker)), np.asarray(condition, np.float32)


def geometric_baseline(scene, image, resolution=32):
    """Conservative height/clearance check on observed points; unknown abstains.

    This baseline has no access to ground-truth terrain or primitive geometry.
    It assumes the vehicle starts on a locally level support plane at Y=0.
    """
    covered = False
    hazardous = False
    support_points = 0
    width, height = frame_dimensions(resolution)
    for index, camera in enumerate(scene.cameras):
        row, col = divmod(index, 2)
        d = image[0, row * height:(row + 1) * height, col * width:(col + 1) * width] * 16
        mask = image[1, row * height:(row + 1) * height, col * width:(col + 1) * width] > .5
        if not mask.any():
            continue
        relative = math.atan2(math.sin(scene.heading - camera["yaw"]), math.cos(scene.heading - camera["yaw"]))
        if abs(relative) + math.atan2(scene.vehicle_width / 2, 2) < math.radians(camera["fov"]) / 2:
            covered = True
        points = camera_rays(camera, resolution) * d[..., None] + np.asarray(camera["position"])
        x, y, z = points.transpose(2, 0, 1)
        along = x * math.sin(scene.heading) + z * math.cos(scene.heading)
        across = x * math.cos(scene.heading) - z * math.sin(scene.heading)
        corridor = mask & (d < 15.95) & (along > -scene.vehicle_length / 2) & (along < 4 + scene.vehicle_length / 2) & (abs(across) < scene.vehicle_width / 2)
        hazardous |= bool((corridor & (((y > scene.step_limit) & (y < scene.vehicle_height)) | (y < -scene.step_limit))).any())
        support_points += int((corridor & (abs(y) <= scene.step_limit)).sum())
    # A clear angular frustum with no observed supporting surface is not a
    # drivable floor. Max-range rays must not manufacture ground support.
    return 1. if hazardous else 0. if covered and support_points >= 3 else .5


def dataset(count, seed, resolution=32, shift=False, loadout="front", encoding="numeric", camera_loss=None, families=None, render_backend="numpy", supported_start=False):
    rng = np.random.default_rng(seed)
    images, conditions, labels, baselines, records = [], [], [], [], []
    scenes=[]
    for index in range(count):
        family = (families or FAMILIES)[index % len(families or FAMILIES)]
        for attempt in range(1000):
            scene=sample_scene(rng, family, shift, loadout)
            if not supported_start or oracle(scene,distance=0)==0:
                scenes.append(scene)
                break
        else:
            raise ValueError("Could not sample a supported query-aligned starting footprint")
    if render_backend == "cuda":
        from gpu_renderer import render_batch
        rendered=render_batch(scenes,resolution)
    elif render_backend == "numpy":
        rendered=None
    else:
        raise ValueError("Unknown renderer backend")
    for index, scene in enumerate(scenes):
        image, condition = encode(scene, resolution, encoding, camera_loss, None if rendered is None else rendered[index])
        images.append(image);conditions.append(condition);labels.append(oracle(scene))
        baselines.append(geometric_baseline(scene, image, resolution));records.append(asdict(scene))
    return dict(images=np.asarray(images), conditions=np.asarray(conditions), labels=np.asarray(labels, np.float32),
                baseline=np.asarray(baselines, np.float32), records=records)
