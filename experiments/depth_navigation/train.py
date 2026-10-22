"""Bounded offline experiment. No tools, agents, network or vehicle control."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import random
import signal
import time

os.environ.setdefault("CUBLAS_WORKSPACE_CONFIG", ":4096:8")
import numpy as np
import torch
from torch import nn
from synthetic import dataset, frame_dimensions, Scene, geometric_baseline, FAMILIES, sample_scene, encode
from metrics import metrics, calibrate

STOP = False


def cancelled():
    marker=os.environ.get('VOIDWRIGHT_CANCEL_FILE')
    return STOP or bool(marker and Path(marker).exists())


def interrupted(signum, frame):
    global STOP
    STOP = True


def save_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n")
    temporary.replace(path)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class TinyDepth(nn.Module):
    def __init__(self, conditions, resolution=32):
        super().__init__()
        self.visual = nn.Sequential(nn.Conv2d(3, 16, 5, 2, 2), nn.ReLU(),
            nn.Conv2d(16, 24, 3, 2, 1), nn.ReLU(), nn.Conv2d(24, 32, 3, 2, 1), nn.ReLU(),
            nn.Flatten())
        self.context = nn.Sequential(nn.Linear(conditions, 32), nn.ReLU())
        width,height=frame_dimensions(resolution)
        features = 32 * math.ceil((height * 2 + 4) / 8) * math.ceil(width * 2 / 8)
        self.head = nn.Sequential(nn.Linear(features + 32, 64), nn.ReLU(), nn.Linear(64, 1))

    def forward(self, images, conditions):
        return self.head(torch.cat((self.visual(images), self.context(conditions)), 1)).flatten()


class ConditioningOnly(nn.Module):
    def __init__(self, conditions):
        super().__init__()
        self.head=nn.Sequential(nn.Linear(conditions,64),nn.ReLU(),nn.Linear(64,32),nn.ReLU(),nn.Linear(32,1))

    def forward(self,images,conditions):
        return self.head(conditions).flatten()


def predict(model, data, device, batch_size=128):
    result = []
    with torch.inference_mode():
        for start in range(0, len(data["labels"]), batch_size):
            images = torch.from_numpy(data["images"][start:start + batch_size]).to(device)
            context = torch.from_numpy(data["conditions"][start:start + batch_size]).to(device)
            result.extend(torch.sigmoid(model(images, context)).cpu().numpy())
    return np.asarray(result)


def save_dataset(path, data):
    np.savez_compressed(path, **{key: value for key, value in data.items() if key != "records"})
    save_json(path.with_suffix(".scenes.json"), data["records"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    config = json.loads(args.config.read_text())
    if cancelled():return 130
    for key, maximum in [("train_samples", 30000), ("validation_samples", 5000), ("test_samples", 8000), ("epochs", 200), ("batch_size", 256)]:
        if not isinstance(config[key], int) or not 1 <= config[key] <= maximum:
            raise ValueError("Invalid bounded config: " + key)
    seed = int(config["seed"])
    if config.get("encoding") not in ("numeric", "strip") or config.get("loadout") not in ("front", "random"):
        raise ValueError("Invalid encoding/loadout")
    for name in ('maximum_false_safe_rate','maximum_permission_error'):
        if name in config and (not isinstance(config[name],(int,float))or not 0<=config[name]<=1):raise ValueError('Invalid calibration bound')
    if not isinstance(config.get('minimum_permissions',0),int)or not 0<=config.get('minimum_permissions',0)<=5000:raise ValueError('Invalid calibration permission count')
    signal.signal(signal.SIGTERM, interrupted);signal.signal(signal.SIGINT, interrupted)
    started = time.monotonic()
    deadline = started + min(1800, float(config.get("wall_seconds", 900)))
    out = args.out.resolve();out.mkdir(mode=0o700, parents=True, exist_ok=True)
    if (out / "result.json").exists() or (out / "run.json").exists():
        raise ValueError("Run directory already used; never overwrite evidence")
    random.seed(seed);np.random.seed(seed);torch.manual_seed(seed)
    torch.set_num_threads(4)
    torch.use_deterministic_algorithms(True)
    device = config.get("device", "cuda")
    if device != "cuda" or not torch.cuda.is_available():
        raise RuntimeError("This experiment requires the actual CUDA GPU; no CPU fallback")
    torch.cuda.set_per_process_memory_fraction(.4)
    torch.cuda.reset_peak_memory_stats()
    run = dict(config=config, config_sha256=sha(args.config), source={p.name: sha(p) for p in Path(__file__).parent.glob("*.py")},
               torch=torch.__version__, cuda=torch.version.cuda, numpy=np.__version__, gpu=torch.cuda.get_device_name(0),
               pid=os.getpid(), protocol="voidwright.depth-query.v1", synthetic=True, live_control=False,
               label="full-geometry oracle; includes potentially unobservable/occluded hazards",
               depth="radial ray distance in metres / 16; step 0.1m; full-FOV 2x2 frames plus external direction strip",
               initial_seed=seed, max_gpu_fraction=.4)
    run['environment_names']=sorted(os.environ)
    run['credential_like_environment_present']=any(any(term in name.upper()for term in ['API_KEY','TOKEN','SECRET','PASSWORD','CREDENTIAL'])for name in os.environ)
    if run['credential_like_environment_present']:raise RuntimeError('Experiment environment was not cleared as required')
    save_json(out / "run.json", run)
    # Fail unsupported deterministic CUDA operations before generating data.
    resolution = config.get("resolution", 32)
    width,height=frame_dimensions(resolution)
    model_type=config.get("model","tiny_depth")
    if model_type not in ("tiny_depth","conditioning_only","projected_depth"):raise ValueError("Unsupported model")
    if model_type=='projected_depth':
        if config['encoding']!='numeric':raise ValueError('Projected model requires numeric direction; strip experiment remains separate')
        from projected import ProjectedDepth
        model=ProjectedDepth(resolution).to(device)
    else:
        model = (TinyDepth(52, resolution) if model_type=="tiny_depth" else ConditioningOnly(52)).to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=.001, weight_decay=.001)
    example=sample_scene(np.random.default_rng(917),'flat');example.heading=0
    example.cameras[0].update(position=[0,1.8,0],yaw=0,pitch=-.35,roll=0,fov=90,valid=True)
    dummy_image,dummy_condition=encode(example,resolution,config['encoding'])
    dummy=model(torch.from_numpy(dummy_image).to(device)[None].repeat(2,1,1,1),
          torch.from_numpy(dummy_condition).to(device)[None].repeat(2,1))
    if not torch.isfinite(dummy).all():raise RuntimeError('Nonfinite CUDA preflight output')
    dummy.sum().backward()
    gradients=[p.grad for p in model.parameters()if p.grad is not None]
    if not gradients or any(not torch.isfinite(g).all()for g in gradients)or not any((g!=0).any()for g in gradients):
        raise RuntimeError('CUDA preflight did not produce finite nonzero parameter gradients')
    optimizer.zero_grad(set_to_none=True)
    torch.cuda.synchronize()
    print(json.dumps(dict(stage="cuda_backward_preflight", passed=True, gpu=run["gpu"])), flush=True)
    if config.get("cancellation_smoke", False):
        save_json(out / "cancellation-ready.json", dict(pid=os.getpid(), cuda_backward_passed=True))
        while not cancelled() and time.monotonic() < started + 60:
            time.sleep(.1)
        torch.save(dict(model=model.state_dict(), config=config), out / "cancelled.pt")
        save_json(out / "interrupted.json", dict(stage="controlled_cancellation_smoke", cancelled=cancelled()))
        return 130 if cancelled() else 0
    splits = {}
    if config.get("render_backend", "numpy") == "cuda":
        from gpu_renderer import self_test
        print(json.dumps(dict(stage="cuda_renderer_parity", **self_test(resolution))), flush=True)
    common = dict(resolution=resolution, loadout=config["loadout"], encoding=config["encoding"],render_backend=config.get("render_backend", "numpy"),supported_start=config.get('supported_start',False))
    training_families=config.get("training_families",list(FAMILIES))
    if not training_families or len(set(training_families))!=len(training_families) or any(f not in FAMILIES for f in training_families):
        raise ValueError("Invalid training terrain partitions")
    heldout_families=[f for f in FAMILIES if f not in training_families]
    split_definitions=[("train", config["train_samples"], 0, False),
            ("validation", config["validation_samples"], 100000, False), ("test", config["test_samples"], 200000, False),
            ("camera_vehicle_shift", config["test_samples"], 300000, True)]
    if heldout_families:split_definitions.append(("heldout_terrain",config["test_samples"],400000,False))
    for name, count, offset, shift in split_definitions:
        if cancelled() or time.monotonic() >= deadline:
            save_json(out / "interrupted.json", dict(stage=name, reason="cancel/deadline"));return 2
        splits[name] = dataset(count, seed + offset, shift=shift,
            families=heldout_families if name=="heldout_terrain"else training_families, **common)
        save_dataset(out / (name + ".npz"), splits[name])
        print(json.dumps(dict(stage="dataset", split=name, samples=count, hazards=float(splits[name]["labels"].mean()), elapsed=time.monotonic() - started)), flush=True)
    if splits["train"]["conditions"].shape[1] != 52:
        raise ValueError("Condition schema changed")
    train = splits["train"]
    images = torch.from_numpy(train["images"]).to(device)
    conditions = torch.from_numpy(train["conditions"]).to(device)
    labels = torch.from_numpy(train["labels"]).to(device)
    criterion = nn.BCEWithLogitsLoss(pos_weight=torch.tensor(3., device=device))
    history = []
    best_loss = float("inf")
    for epoch in range(config["epochs"]):
        if cancelled() or time.monotonic() >= deadline:
            torch.save(dict(model=model.state_dict(), optimizer=optimizer.state_dict(), epoch=epoch, config=config), out / "interrupted.pt")
            save_json(out / "interrupted.json", dict(stage="train", epoch=epoch, reason="cancel/deadline"));return 2
        model.train();order = torch.randperm(len(labels), device=device);losses=[]
        for batch in order.split(config["batch_size"]):
            optimizer.zero_grad(set_to_none=True)
            loss = criterion(model(images[batch], conditions[batch]), labels[batch])
            loss.backward();optimizer.step();losses.append(float(loss.detach()))
        model.eval();probability = predict(model, splits["validation"], device)
        validation_loss = float(np.mean(-(3 * splits["validation"]["labels"] * np.log(np.clip(probability, 1e-7, 1))
            + (1 - splits["validation"]["labels"]) * np.log(np.clip(1 - probability, 1e-7, 1)))))
        row = dict(epoch=epoch + 1, train_loss=float(np.mean(losses)), validation_loss=validation_loss, elapsed_seconds=time.monotonic() - started)
        history.append(row);print(json.dumps(row), flush=True);save_json(out / "history.json", history)
        if validation_loss < best_loss:
            best_loss = validation_loss
            torch.save(dict(model=model.state_dict(), epoch=epoch + 1, config=config, run=run), out / "best.pt")
    checkpoint = torch.load(out / "best.pt", map_location=device, weights_only=False)
    model.load_state_dict(checkpoint["model"]);model.eval()
    validation_predictions = predict(model, splits["validation"], device)
    threshold = calibrate(splits["validation"]["labels"], validation_predictions,
        maximum_false_safe=config.get('maximum_false_safe_rate',.02),eligible=splits["validation"]["baseline"] != .5,
        maximum_permission_error=config.get('maximum_permission_error'),minimum_permissions=config.get('minimum_permissions',0))
    evaluations = {}
    for name in ["test", "camera_vehicle_shift"]+(["heldout_terrain"]if heldout_families else []):
        data = splits[name];p = predict(model, data, device)
        eligible = data["baseline"] != .5
        evaluations[name] = dict(model=metrics(data["labels"], p, threshold, eligible),
            model_at_half=metrics(data["labels"], p, .5, eligible),
            model_with_geometry_guard=metrics(data["labels"], p, threshold, data["baseline"] == 0),
            geometry=metrics(data["labels"], data["baseline"], .25), always_stop=metrics(data["labels"], np.ones(len(p)), .5))
        np.savez_compressed(out / (name + ".predictions.npz"), probabilities=p, labels=data["labels"], baseline=data["baseline"])
        evaluations[name]["families"] = {family: metrics(data["labels"][selector], p[selector], threshold, eligible[selector])
            for family in sorted({r["family"] for r in data["records"]})
            for selector in [np.asarray([r["family"] == family for r in data["records"]])]}
    # Mid-run loss is a paired counterfactual of identical scenes/labels; never
    # relabel missing cameras as clear space. All-camera loss always abstains.
    data = splits["test"]
    for camera in range(4):
        lost = dict(data);lost["images"] = data["images"].copy();lost["conditions"] = data["conditions"].copy()
        row, col = divmod(camera, 2)
        lost["images"][:, :2, row * height:(row + 1) * height, col * width:(col + 1) * width] = 0
        lost["conditions"][:, 8 + camera * 11 + 10] = 0
        p = predict(model, lost, device)
        lost_baseline=np.asarray([geometric_baseline(Scene(**record),image,resolution)
            for record,image in zip(data["records"],lost["images"])])
        eligible = lost_baseline != .5
        evaluations["loss_" + str(camera)] = metrics(data["labels"], p, threshold, eligible)
    image = torch.from_numpy(data["images"][:1]).to(device);context = torch.from_numpy(data["conditions"][:1]).to(device)
    with torch.inference_mode():
        for _ in range(30):model(image, context)
        torch.cuda.synchronize();latency=[]
        for _ in range(200):
            before=time.perf_counter();model(image, context);torch.cuda.synchronize();latency.append((time.perf_counter() - before) * 1000)
    result = dict(schema="voidwright.depth-experiment-result.v1", config=config, best_epoch=checkpoint["epoch"],
        model_type=model_type,training_families=training_families,heldout_families=heldout_families,
        parameters=sum(p.numel() for p in model.parameters()), elapsed_seconds=time.monotonic() - started,
        peak_cuda_allocated_bytes=torch.cuda.max_memory_allocated(), threshold=threshold,
        calibration=dict(maximum_false_safe_rate=config.get('maximum_false_safe_rate',.02),
            maximum_permission_error=config.get('maximum_permission_error'),minimum_permissions=config.get('minimum_permissions',0),
            scope='Validation only; both hazard-miss and permission-error denominators explicit; no statistical or physical safety guarantee'),
        evaluations=evaluations, latency_ms=dict(p50=float(np.median(latency)), p95=float(np.percentile(latency, 95))),
        checkpoints={"best.pt": sha(out / "best.pt")}, data={p.name:sha(p) for p in out.glob("*.npz")},
        promotion="offline synthetic research only; deployment and live control not authorized by these results")
    save_json(out / "result.json", result);print(json.dumps(dict(stage="complete", result=str(out / "result.json"), **result["latency_ms"])), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
