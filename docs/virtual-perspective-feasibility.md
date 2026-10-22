# Virtual perspective: first read-only slice

## Existing seams

- Voidwright's Agent Core snapshot exporter already provides enrolled physical-grid IDs, transforms, cell size, block bounds, definitions, orientation, state, coverage markers, capture tick, and a structure fingerprint. The local stdio Agent MCP adapter reads that bounded world-scoped file. It does **not** enumerate all world grids.
- VRageCage can inspect grids and camera blocks in a stopped disposable fixture, and its lab sensor-frame path obtains camera-pose depth rays from a running dedicated server. These are separate from the Agent Core body snapshot.
- HoloView can produce software-rendered camera/LCD frames in dedicated mode. They are not native framebuffer captures and currently require configured camera/output blocks. A dedicated server cannot simply move a client spectator camera and return native pixels.
- The existing lab plugin can actuate selected vehicles under a separate, pinned disposable-lab contract. This first perspective slice does not invoke that path.

## Implemented now

The existing Agent MCP process has five additional read-only tools:

1. `create_voidwright_perspective(agentId, gridEntityId, rightMeters, upMeters, forwardMeters)` creates a process-local virtual observation point relative to an enrolled physical grid.
2. `move_voidwright_perspective(perspectiveId, rightMeters, upMeters, forwardMeters)` moves it to a bounded absolute offset (250 m maximum radius).
3. `inspect_voidwright_cell(perspectiveId, x, y, z)` returns exact snapshot block matches for a grid cell, with definition, orientation, health/function state, tick, timestamp, coverage and structure fingerprint. Empty-cell conclusions require complete block coverage.
4. `look_at_voidwright_perspective(perspectiveId, worldX, worldY, worldZ)` aims the virtual frame at a world-space point. The response includes world-space position, forward and up, plus local grid-relative position, forward and up. Local offsets are meters along the grid's right/up/forward axes; world coordinates are meters in the active world frame. The grid basis is checked for orthonormality.
5. `raycast_voidwright_perspective(perspectiveId, maxDistanceMeters, directionWorld?, targetWorldPosition?, targetGridCell?, targetGridId?, expectedSimulationTick?)` casts a bounded ray against enrolled grid block envelopes. It defaults to the current perspective forward, or aims at a supplied world position or cell center. A caller can require a specific snapshot tick. The result contains run/world identity, tick, perspective pose, normalized ray, selected grid and block, exact entered grid cell, distance, local cell-envelope face, and its transformed world normal.

These tools create no game entity, touch no save, and do not move a vehicle. A perspective follows its grid when the snapshot refreshes because its offset and orientation are grid-relative. The adapter holds at most 16 perspectives in memory; all vanish when the MCP process exits. A missing grid, changed structure fingerprint, changed simulation run ID, or changed configured world-scope path invalidates a perspective. `worldScopeId` is a SHA-256 identity of the configured absolute snapshot path, **not** a game-provided world UUID; the exporter adds a fresh `runId` for each session. The ray query rejects snapshots older than the configured freshness window and refuses mixed agent/envelope ticks. It tests oriented block cell envelopes in the exported structure snapshot. A reported normal is the grid-cell envelope face normal, not the detailed block model surface normal. A complete miss only means no enrolled block envelope intersected the ray; the snapshot has no terrain, unrelated grids, or non-grid entities. When snapshot coverage is incomplete, hit/miss status becomes `unresolved`. If an observed block has no entity ID, it is reported with `identityResolved=false`.

The current slice does **not** provide physics-engine raycasts, terrain or unrelated-world-entity targeting, block-mesh surface normals, free-flight beyond the enrolled body, orbit animation, X-ray/slicing, cursor highlights, placement previews, or client-shared rendering. The optional rendered laser endpoint/segment is not implemented; the software renderer has not been connected to authoritative raycast results. The MCP end-to-end raycast test uses a bounded synthetic snapshot. A live test requires a disposable world with an enrolled Agent Core and its exact snapshot file; no production save was used.

## Virtual color-camera integration status

The current HoloView raster job already takes a full `MatrixD Cam`, but its scheduler, scene scans, target selection, and metadata generation still depend on a real `IMyCameraBlock` and an LCD output. In the inspected disposable source, `BoundedHoloCamWorker.cs` refuses work when `camera == null`, assigns `job.CameraEntityId = camera.EntityId`, and obtains the job matrix through `GetBoundedCameraFrame()`. `BoundedHoloCamSession.cs` has further physical-camera-dependent scan paths. Simply overriding `job.Cam` would leave scene selection and provenance tied to a different camera and can produce a convincing but wrong picture.

For that reason there is **no `capture_view()` MCP tool yet**. An honest tool must be backed by a renderer job that actually consumed the virtual frame, not just an image from a nearby fixed camera. The necessary next step is to extract a `RenderSource` (physical camera or virtual pose) used consistently by scene scan, job setup, and capture metadata. Keep the existing physical-camera path as the default and validate its output before enabling the virtual path. The read-only virtual request needs an explicit fixture/run binding, target grid, bounded world pose/FOV/range/resolution, and a request nonce. The response must contain the renderer's actual full pose (including up, not just position/forward), source kind, frame hash, renderer tick, structural snapshot tick, and a signed tick delta. Legacy `HVRAW1` captures contain position and forward but no up or run identity; they are insufficient to prove an arbitrary virtual-camera request was honored.

An archived `HVRAW1` frame in `artifacts/holocam-depth-texture-proof-20261011/inputs/` was checked directly: its header records a 256×256 raster, camera/output IDs, tick 66, FOV 60°, position and forward; its sibling scene sidecar has no camera-up or run-ID field. The HoloView source examined for this assessment was the disposable `test-suite-world-v2-r3-hvvideo-001` copy on the lab worker, not an upstream revision or a deployed production tree.

`ViewCaptureReconciler` now implements the local consistency gate for that future trusted producer: it checks world/run/target identity, a finite orthonormal full pose, requested-vs-rendered position and orientation, bounded dimensions and exact RGBA byte length, RGBA SHA-256, and the signed tick delta. This is **not** an attestation that the pixels were actually generated from the pose; only an instrumented renderer with a request/response nonce and end-to-end disposable-world test can establish that. It is deliberately not exposed as a capture MCP tool yet.

The local MCP test verifies position and orientation change, world/run invalidation, and exact-cell queries using a **synthetic snapshot**. It does not verify a changed HoloView image. A live disposable-world render and pixel-difference test remain required before claiming `capture_view()` works.

## Raycast verification receipt

The MCP integration test exercises a synthetic snapshot with a rotated grid basis and three known blocks. Two aim directions select different block IDs; aiming through a lower cell hits the nearer stacked block; the returned cell and transformed face normal are checked. It also tests a range-limited miss, an open direction, world-position aiming, grid translation between snapshots, expected-tick rejection, stale-snapshot rejection, and changed-run rejection. This proves the adapter's snapshot geometry calculation and provenance guards. It does not prove that the result matches Space Engineers' collision/physics raycast in a live world. The next live check needs a disposable enrolled-grid fixture and a comparison to the engine's read-only `CastRay` result before calling it engine-authoritative.

## Offline capture replay view — 2026-10-19

`scripts/render-driving-lab-replay.py` reads one schema-v1 JSONL stream or
compares a human demonstration with a later attempt. Each stream is capped at
64 MiB and 1 MiB per line; combined comparisons are capped at 10,000 records,
with 512 actuator observations per record and a 16 MiB output limit. The
viewer validates sequence and cumulative drop ordering and binds one
run/session/stream/grid identity within each input. Comparison requires an
explicit `relative-simulation-time` or `explicit-start-offset` policy; it never
infers UTC synchronization. The second policy requires offsets plus explicit
uncertainty values. Missing source timing bounds are labeled unknown. Different
tick rates use each source's simulation-time samples; simulation ticks and
identities remain separately displayed. Gaps and missing time/value samples
break traces; no interpolation occurs. It rejects comparing an input stream to
itself and validates finite offsets and nonnegative uncertainty in both the
CLI and direct comparison API.

The viewer rejects duplicate actuator observations with the same entity ID and
kind inside a record, rather than silently collapsing potentially conflicting
values into one series. The top-down view plots only complete observed chassis X/Z positions. The
oblique view projects complete observed X/Y/Z positions, and draws forward
orientation arrows only when the necessary observed direction components are
present. Both views break paths at missing pose samples and sequence gaps.
Hatched space is explicitly unknown. These diagrams can show recorded
translation and orientation, but cannot show terrain shape, voxel contents,
obstacles, collisions, wheel contact, detailed vehicle meshes, occlusion, or a
camera image. They are not a reconstruction or simulated scene. Physics and
reported input time series are plotted only when present. The actuator schema
has generic per-entity `overrideValue` and does not identify it as steering or
propulsion, so the viewer preserves that generic label. Human input remains
unavailable unless a capture explicitly records it, and a reported input is
not proof of human attribution.

Offline fixtures cover sequence gaps, missing pose, partial and unavailable
channels, cumulative drop counts, and identity mismatch rejection. This is a
file-only viewer; it has no game connection or execution/control path. No live
capture was run.

## Propulsion controlled-stop and cancellation gate — 2026-10-19

The offline `DirectManeuverOperationLease` and executor policy now model
preflight-bound snapshots, per-grid in-process exclusion, fresh heartbeat and
expiry, per-channel accepted readbacks, external-change relinquishment,
expected-value cleanup, run-bound cancellation acceptance, and stationary
terminal receipts. Adversarial fixture tests cover competing leases, stale
lease, wrong identity, partial write, cancellation timeout, and cleanup that
preserves changed values. This policy is not connected to any runtime adapter.

Installed ModAPI semantics were checked in SDK XML and decompiled game
implementation. `SteeringOverride` and `PropulsionOverride` set synchronized
per-wheel values without an owner token or conditional-write revision. The
explicit `IMyMotorSuspension.Brake` setter maps to `BrakingEnabled` (the wheel
configuration), while the host's internal wheel `Brake` field is a distinct
active signal. `IMyShipController.HandBrake` and host `GridWheels.Brake` are
shared controller/grid state. None offers operation ownership. The existing
plugin neutralizer changes shared grid brake state. VRageCage has a
process-level server stop, not a maneuver-scoped cancel endpoint and exact
stop-acceptance receipt.

Minimal production support is a server-authoritative broker shared by the
plugin and mod that provides (1) atomic exclusive grid lease acquisition from
fresh all-pass host/mod preflight plus revisioned exact actuator and brake
snapshots, (2) owner-token compare-and-set wheel writes and expected-revision
cleanup receipts, and (3) a run/fixture/operation-bound controlled-stop and
cancel API with acceptance receipts and fresh stationary telemetry. The
current WorldStorage heartbeat is not an atomic ownership broker. Until these
semantics exist and an adapter proves them, `ControlledStopGuaranteed` remains
false in production preflight and direct-maneuver dispatch remains blocked.
