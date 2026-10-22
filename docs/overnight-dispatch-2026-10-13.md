# Voidwright / VRageCage overnight dispatch receipt

Date: 2026-10-13  
Scope: read-only perception and engineering telemetry; no autonomous actuation

## Baseline and stopping state

The prior vertical slice connected a process-local Voidwright perspective to
HoloView's software rasterizer through a bounded MCP mailbox. It produced
three real color frames from the isolated V2R3 Agent Core fixture and was
measured with short serial capture batches. The render counter and game
simulation tick remain distinct clocks; rendering is saved-grid-only. Full
details, frame hashes, and cadence data are in
[`vragecage/docs/voidwright-live-capture-2026-10-13.md`](../../vragecage/docs/voidwright-live-capture-2026-10-13.md).

I rechecked the exact fixture using VRageCage status and receipt commands:

- Fixture: `test-suite-world-v2-r3-voidwright-unified-20261013-c`
- Fixture ID: `19dc9f16e7eaf050e470d3f1624956b2`
- Last run: `f419fad2474be11bb163e387fdb2a36a`
- Service: inactive/dead, exit status 0, no restarts
- Receipt: `clean_shutdown=true`, `game_ready=true`, `scripts_loaded=true`,
  no fatal markers, zero fixture edits, no pending lab operation
- Terminal receipt SHA-256:
  `536e1fc729e9854818bb6db6d54768bb00fa979a4fff50620d831722c74b5606`
- Final game log SHA-256:
  `99c1a2039beccd91a37bafa1e871e74e56a67a39720e7f704ecb087d7b4f8bcd`
- The terminal receipt confirms zero connected humans. The earlier “one
  online” text was stale log output, not the final player count.

The snapshot is now stale because the fixture is stopped. The original V2R3
save, other V2R3 candidate, production worlds, and Workshop source cache were
not changed. No server was restarted during this dispatch.

## Changes made in this continuation

1. Added a regression test proving the existing grid-local virtual viewpoint
   follows a refreshed grid's translation and rotation while retaining its
   perspective ID, world/run binding, and local offset. This is a synthetic
   snapshot test, not live vehicle movement. No camera block or game entity is
   created.
2. Extended the existing `query_voidwright_body` response with a bounded
   integrity summary: observed block count, known damaged count, known
   nonfunctional count, coverage completeness, and an explicit unknown-state
   explanation. It reports `destroyedOrMissingBlockCount: null` because no
   historical comparison exists; an omitted block is never labeled destroyed.
3. Documented both contracts and their limits.

Changed task-specific files in this continuation:

- `/home/alu52/github/voidwright/AgentMcp/AgentMcpTools.cs`
- `/home/alu52/github/voidwright/tests/Voidwright.AgentMcpTests/Program.cs`
- `/home/alu52/github/voidwright/docs/agent-core-mcp.md`
- `/home/alu52/github/vragecage/docs/voidwright-live-capture-2026-10-13.md`
- This receipt: `/home/alu52/github/voidwright/docs/overnight-dispatch-2026-10-13.md`

The checkout was already heavily dirty with unrelated user work. Nothing was
staged, committed, pushed, cleaned up, or reverted.

## Verification

- `dotnet build AgentMcp/Voidwright.AgentMcp.csproj -c Release`: passed, 0
  warnings and 0 errors.
- `dotnet run --project tests/Voidwright.AgentMcpTests/Voidwright.AgentMcpTests.csproj -c Release`:
  passed, 16 checks, including the new integrity-summary and moving-grid
  perspective checks.
- `python3 worker/tests/test-holoview-live.py`: passed, 3 tests.
- `python3 worker/tests/test-holoview-virtual.py`: passed, 8 tests.
- `python3 worker/tests/test-holoview-capture.py`: passed.
- `/home/alu52/.codex/config.toml` parses as TOML and pins the remote
  read-only adapter, exact fixture snapshot path, exact HoloView storage path,
  and expected world name.
- The actual running desktop tool catalog currently exposes eight Voidwright
  tools and does **not** include `capture_view`. A direct stdio handshake with
  the configured adapter previously listed 10 tools including `capture_view`.
  This is an unresolved desktop catalog refresh mismatch, not a renderer or
  adapter failure. A fresh MCP session or desktop connection refresh is still
  needed to prove image access from an ordinary Codex chat.

## Backlog status

| Dispatch priority | Status at safe stop |
|---|---|
| 0. Preserve current work | Verified: dirty checkouts preserved; fixture inactive with clean terminal receipt. |
| 1. Visual perception bridge | Adapter and live render path implemented; 3 distinct genuine frames and short serial cadence measurements exist. No continuous stream/latest-frame buffer; not real-time. Desktop tool catalog still needs refresh. |
| 2A. Power/fuel | Not implemented or live-API-verified in this slice. |
| 2B. Integrity/damage | Small MCP summary now implemented from existing `IMySlimBlock` snapshot ratios and functional state. No destroyed/missing inference or history. |
| 2C. Paint/appearance | Not implemented; actual paint colors and skins remain unverified. |
| 2D. Subgrid relations | Snapshot includes physical grids/transforms but does not explicitly classify rotor/hinge/piston versus connector associations. |
| 3. Vehicle-relative perspectives | Grid-local transform-following behavior tested synthetically across translation and rotation. No smoothing, named profile API, orbit, or moving-target lock; no live moving-grid trial in this slice. |
| 4. Docking geometry | Not implemented or API-verified. |
| 5. Turret and analog controls | Not investigated here; no actuation attempted. Existing control experiments were not invoked. |
| 6. Agent presence/communication | Anonymous sole-player pose is existing behavior; marker, radio rules, Emotion Controller, and LCD app integration remain unimplemented. No ongoing player tracking was added. |
| 7. Environmental awareness | Natural gravity already appears in grid snapshots. Sun/day-night/light observations are not implemented. |
| 8. Independent observation loop | No background or unbounded loop added. Existing bounded, on-demand observations remain the safe operating model. |

## Performance and limitations inherited from the live capture receipt

Short 4-call batches measured approximately 1.006, 4.326, 4.262, and 9.992
captures/s when requesting 1, 5, 10, and 30 fps, respectively. A separate
8-call 30-fps-requested burst achieved 6.112 end-to-end calls/s. Request-to-
capture latency was 20.1–186.7 ms across those samples, while the structural
snapshot-to-frame age ranged up to about five seconds. A coarse process sample
showed roughly 14.8 MiB added RSS during the short burst; CPU attribution was
too coarse to make a stable claim. These are bounded serial requests, not
sustained rates, dropped-frame statistics from a stream, or evidence of 30-fps
video.

The custom HoloView renderer covers saved-grid proxies only. It does not
establish planet terrain, voxel surfaces, characters, or arbitrary dynamic
entities. Structural ray hits use exported grid-cell envelopes, not detailed
meshes or live physics. Image/render timestamps and structural ticks are not
simultaneous.

## Deferred decisions and next experiment

No new permission is needed for the next read-only test. First refresh the
desktop MCP session and verify `capture_view` is actually listed in its tool
catalog. Then start only this same disposable fixture, confirm its service and
player count before observation, and compare two frames from a grid-local
perspective before/after a naturally occurring vehicle move. Do not issue a
drive command to create motion. Record snapshot ticks, renderer-session
counters, pose, latency, and hashes separately. Stop cleanly when done.

After that, the next highest-value telemetry experiment is to verify a small
read-only power/fuel summary against game-exposed battery, reactor, tank, and
thruster APIs in the compatible game assemblies. Keep measured block values
separate from any estimates, add a bounded snapshot/MCP contract, and only
claim it after a live disposable-world check. Explicit paint color and
mechanical subgrid relation data are the next likely follow-ons.
