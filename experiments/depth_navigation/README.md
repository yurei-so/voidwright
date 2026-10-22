# Offline depth navigation laboratory

A private, reproducible research toolkit for Blake's Voidwright / VRageCage
work. Synthetic depth scenes train a small **tensor model** on mpai's RTX 4070.
The model has no Roost identity, tools, independent agent loop or vehicle API.
This work does not deploy a driving policy. Paid routing stays disabled.

Read [the results](RESULTS-2026-10-15.md) and [operations](OPERATIONS.md) before
using a checkpoint. The mutable checkout is a review copy; installed experiments
use separate frozen source directories and exact manifest hashes.

## Input contract

| Quadrant | Complete camera frame |
|---|---|
| Top left | FRONT |
| Top right | REAR |
| Bottom left | LEFT |
| Bottom right | RIGHT |

Current experiments use **32 × 18** pixels per camera, preserving the full 16:9
field of view. The three-channel mosaic is `[3, 40, 64]`: depth, validity and an
optional direction marker. A four-row external strip below the quadrants carries
the marker in the independent strip experiment. It never replaces depth pixels.

Depth is radial ray distance divided by 16 metres. A valid ray miss is depth 1,
mask 1; an unavailable camera is depth 0, mask 0. These states are distinct.
Numeric mode leaves the third channel and external strip empty. Initial r1/r2
pilots used square frames; their results are preserved as superseded pilots.

The 52 conditioning values are:

- Width / 4.3, height / 4, length / 6, step/drop limit, smooth-slope limit,
  reverse state, sine heading, cosine heading (8 values).
- For each camera: position / 4 (xyz), sine/cosine yaw, pitch and roll,
  horizontal FOV / 130, and an explicit availability bit (4 × 11 values).

Coordinates are X right, Y up, Z front in a simplified upright, gravity-aligned
local frame. Heading is the absolute desired direction in that frame; the
reverse bit does not silently invert it. This is a four-metre **geometric query**
including body length, not a steering arc, braking-distance estimate, tilted-body
simulation or wheel/traction model. r4 samples only query-aligned supported starts.
That condition cannot be inferred from an unsynchronized historical camera file.

Camera pose, FOV, vehicle dimensions and loadout vary by scene. Front-only models
train on headings within ±0.85 radians; randomized-camera models cover all
headings and missing-camera combinations. Validation code rejects out-of-domain
body/camera/heading inputs for its declared profile. It cannot establish live
freshness, controller identity or physical support from tensor shape alone.

## Labels and models

`synthetic.py` independently samples swept width, height and length against
analytic terrain and solids. Smooth grade and discontinuous curb/drop have
separate limits. Labels include hidden hazards. A regression counterexample
produces identical front-camera input for clear and blocked rear travel: the
missing information cannot be learned from identical observations.

`gpu_renderer.py` renders depth on CUDA, with an actual 32-frame CPU/GPU parity
preflight retained from the runs (maximum difference 0 metres). Ray marching is
quantized at 0.1 metres and body/terrain sampling is finite. Thin geometry and
real game physics remain outside this oracle's guarantees.

| Method | Inputs | Parameters |
|---|---|---:|
| Geometry baseline | Reconstructed observed points + conditioning | 0 |
| Raw image CNN | Mosaic + conditioning | 97,433 |
| Shared point model | Depth-derived points/normals + conditioning | 2,321 |
| Metadata control | Conditioning only | Recorded in r3 results |

`projected.py` reconstructs points and finite-difference normals from the same
input tensors. No scene object, oracle normal, terrain identity, world file or
privileged geometry enters its forward pass. Point weights are shared across
camera roles. No observed query points yields an unsafe/unknown score.

The geometry baseline requires observed supporting points; a no-hit frustum
cannot manufacture a floor. It remains a simplified baseline, with measurable
false-safe errors and false stops on slopes.

## Experiment design and decision policies

r3 compares numeric direction against the external visual strip, with a metadata
negative control, three seeds and independent scene splits. Neither encoding
consistently wins; the CNNs fail the intended permission-error goal.

The preregistered [r4 plan](R4-PLAN.md) introduces a **new supported-start cohort**
and compares architectures on identical inputs. Seeds 48001–48003 each use
20,000 training, 2,500 validation and 2,500 cases per evaluation split. The three
evaluation splits are new scenes, changed cameras/vehicle dimensions, and unseen
boulder/rough terrain families. Front-only and random-camera campaigns are
separate. Cross-version improvement is not a model-only causal comparison.

Training uses deterministic Torch operations, AdamW, weighted BCE (hazard weight
3), and matching weighted validation loss for checkpoint selection. Thresholds
are selected on validation only: hazard-miss fraction ≤2%, hazardous fraction
among permissions ≤2%, and ≥50 permissions. No qualifying threshold gives zero
permissions. These are empirical criteria, not confidence or physical guarantees.

Reports keep two different decisions separate:

1. **Learned decision with observation eligibility:** score below the validation
   threshold and geometry result known (0 or 1). It can disagree with geometry.
2. **Learned decision with geometry veto:** additionally require geometry 0.
   This is the stricter offline advisory policy in `advisory.py`.

The original calibration uses policy 1. Policy 2's smaller selected population
can have a *higher* hazardous fraction despite fewer false-safe decisions. The
front test demonstrates this; do not claim the veto alone meets the 2% target.
Both policies are independently recomputed from hashed retained predictions.

Always report counts, hazardous-query denominator, permission denominator,
coverage, clear recall, individual seeds, shifts and uncertainty. A policy that
always stops has zero coverage; its zero errors do not demonstrate useful driving.
Wilson intervals and pooled seed summaries are descriptive, not certificates.

## Camera failures and recorded frames

Paired loss tests remove each role from the same scenes without changing labels.
Losing FRONT from a front-only model produces zero permissions. A multi-camera
network can produce newly permissive outputs after losing evidence; the separate
controller utility `CameraLossLatch` therefore requires a new controller plan
once a previously available role disappears, even if the feed later returns.
The model cannot reset that latch or grant actuation. This is a counterfactual
observation test, not a temporal driving-policy simulation.

`recorded.py` reads the game's 64 × 36, big-endian unsigned 16-bit PGM depth
frames. It checks hashes, dimensions, range, hit count and metadata, preserves
IDs as exact strings, and bins complete frames to 32 × 18 without FOV cropping.
Raw zero means a valid ray miss. Self-grid hits require a per-pixel exclusion
mask before use. Missing cameras use independent validity masks.

The four retained historical game views have different operation IDs and lack a
shared tick and controller pose. Their adapter receipt explicitly says
`synchronized: false`, `conditioning_available: false`, and
`usable_for_model_inference: false`. The inspected mosaic is compatibility
evidence only; no model prediction on those game frames is claimed.

## Verification and evidence

```bash
python3 -m unittest discover -s . -p 'test_*.py'
node --test test_control_state.mjs
```

The workstation has no Torch and skips three projection tests. mpai runs the
full suite with its existing pinned GPU environment; no package installation,
model download or provider request is needed. Actual CUDA backward, renderer
parity, cancellation, controlled own-PID crash, campaign PID ownership and export
checks have separate run receipts. A unit test does not establish GPU behavior.

`analyze.py` checks retained NPZ hashes, recomputes both decision policies and
verifies paired architecture inputs before writing JSON, CSV and plots.
`report.py` renders actual retained depth inputs and errors without loading a
model. `benchmark.py` pins checkpoint/data/source hashes, tests export parity and
reports separately timed eager, compiled and host advisory paths.

Private evidence lives at:
`/home/alu52/.local/state/voidwright/depth-navigation-evidence/`.
`CURRENT.json` is the handoff index. Frozen sources, logs, data and checkpoints
also remain on mpai under the paths documented in operations. No experiment
result authorizes live game use; that needs a new, specifically approved
engineering evaluation with synchronized observations and deterministic limits.
