# Voidwright

Voidwright is an experimental autonomy framework for Space Engineers. Its first
vertical slice replaces "steer and hope" navigation with vehicle-aware route
planning. Navigation is the first capability, not the permanent boundary of the
project.

## Current slice

The local agent-tooling extension provides a compact vehicle workflow with
named software-camera views, motion/condition summaries, and reviewed,
journaled VRageCage GPS trials. See
[higher-level vehicle tools](docs/agent-vehicle-workflow.md). This extension is
built/tested locally; live adapter/worker/plugin activation remains gated.

- A shared, deterministic A* planner with a bounded search budget.
- LAND traversal that accounts for footprint, slope, step height, and turning.
- AIR traversal that searches true 3D space with clearance inflated by the
  vehicle envelope.
- Explicit separation between planned navigation and reactive emergency
  avoidance.
- A deterministic route-follower core that converts vehicle-local targets into
  bounded LAND steering/throttle or three-axis AIR guidance. It is deliberately
  actuator-neutral: game-facing code still cannot write wheels or thrusters.
- An observation-only Space Engineers vehicle probe. Rename a Remote Control so
  its name contains `[Voidwright]`; the probe reports its physical-grid-group
  envelope, block count, propulsion evidence, and conservative mobility class.
  It never changes controls or autopilot state. Motion and rotation do not
  retrigger notifications; only structural or mobility-evidence changes do.
- A local, ephemeral route-horizon preview using the first waypoint configured
  on the opted-in Remote Control. Purple `VW` markers show the bounded LAND or
  AIR planner output and are replaced as the vehicle advances. This first
  preview establishes goal ingestion and visualization; collision and terrain
  sampling are deliberately not represented yet.
  HYBRID vehicles show both candidate paths: purple `VW-L` for LAND and cyan
  `VW-A` for AIR. Marker names include a short controller ID so multiple opted-in
  vehicles remain distinguishable.
- Dedicated small- and large-grid **Voidwright Agent Core** blocks (the stable
  internal subtype IDs remain unchanged) using Keen's non-DLC Event Controller
  models. The Automations programmable-
  block reskin is explicitly DLC-gated and is not repackaged or bypassed. Its
  terminal owns the persisted `Navigation armed` consent boundary,
  `Auto`/`LAND`/`AIR` mode, destination GPS, and vanilla-AI arbitration policy.
  The current slice discovers and diagnoses these blocks but deliberately grants
  them no vehicle-control authority.
- Placing an Agent Core enrolls its physical grid construct under a world-local
  agent ID. Snapshot schema v1 records core/grid world positions
  and axes, grid size, velocity and gravity, plus per-block type, subtype,
  grid coordinate/bounds, orientation, build/damage, and functional state.
  Discovery is capped at 64 physical grids and 4,096 block entries and marks
  incomplete snapshots as truncated. Terminal Custom Info provides an
  inspection view. The retained snapshot can also generate an optional,
  read-only SVG wireframe with top, side, and front profiles and color-highlighted
  block selections. A separate local stdio MCP adapter reads a bounded
  world-scoped snapshot file and exposes read-only body queries plus that
  optional wireframe. It also has one separately pinned VRageCage lab-only
  tool for bounded allowlisted GPS goals against a normal Remote-Control vehicle and
  same-grid depth camera; that tool does not require Agent Core. This is a
  destructive-lab development bridge, not an ordinary-world control grant.
- Basic, Recorder, Offensive Combat, and Defensive Combat blocks on the physical
  vehicle are discovered as intent providers. Diagnostics report the provider
  inventory and current arbitration winner; no vanilla provider receives movement
  authority. The controller deliberately stays on the stable Event Controller
  shell: custom AI Move subtypes crash inside the game's opaque movement-block
  initializer before mod code runs.

See [docs/architecture.md](docs/architecture.md) for the design boundaries,
[docs/game-integration.md](docs/game-integration.md) for the staged integration
plan, and [docs/agent-core-wireframe.md](docs/agent-core-wireframe.md) for the
wireframe query contract. See [docs/agent-core-mcp.md](docs/agent-core-mcp.md)
for local MCP adapter setup and its explicit file boundary.

## Build and test

```bash
dotnet run --project tests/Voidwright.CoreTests/Voidwright.CoreTests.csproj
dotnet run --project tests/Voidwright.AgentMcpTests/Voidwright.AgentMcpTests.csproj
./scripts/build-mod.sh
```

`build-mod.sh` compiles against the installed game assemblies and packages the
mod into `dist/Voidwright`. Point it at a local Space Engineers installation:

```bash
export SPACE_ENGINEERS_BIN64="/path/to/SpaceEngineers/Bin64"
./scripts/build-mod.sh
```

Install the optional development link by naming the Proton local-mod directory:

```bash
export SPACE_ENGINEERS_MODS_DIR="/path/to/compatdata/244850/pfx/drive_c/users/steamuser/AppData/Roaming/SpaceEngineers/Mods"
./scripts/link-proton-mod.sh
```

The link is intentionally a symlink to the generated package, not to the
repository root. Both scripts require explicit paths so the repository carries
no operator-specific Steam-library assumptions.

## Safety boundary

The current mod does not commandeer vanilla AI blocks. Agent Core placement
enrolls a body for observation; the older `[Voidwright]` Remote Control marker
remains a temporary vehicle-probe path. Neither grants movement authority.
Observation results are written to `SpaceEngineers.log` and shown as a local
notification when they change. Perception and any future actuation remain
separate capabilities with separate authority boundaries.

The separately privileged `ServerPlugin/` adapter is not part of the ordinary
mod authority surface. It may actuate copied fixture grids only when launched by
VRageCage's destructive-lab contract; ordinary saves and the normal mod remain
observation-only.
