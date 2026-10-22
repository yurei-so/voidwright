# Voidwright architecture

Voidwright separates the question **what should this agent do?** from **how can
this particular vehicle safely get there?**

## Layers

The product boundary is intentionally split into four roles: the agent chooses
goals and decisions; the **Agent Core** enrolls a body and discovers its grid
construct; perception turns sensors/world APIs into observations; and actuation
is a separate, explicitly authorized capability. The current Agent Core
implements identity and bounded grid discovery only. It does not make plans,
read camera frames, or move the construct.

1. **World observations** describe terrain, obstacles, dynamic contacts, and
   uncertainty without choosing an action.
2. **Embodiment** describes the controlled grid's envelope and mobility limits.
3. **Navigation representations** translate observations for one mobility class.
   LAND uses a terrain/traversability surface. AIR uses a three-dimensional
   occupancy volume. Neither is forced through the other's representation.
4. **Route planning** is shared infrastructure over a narrow traversal contract.
5. **Route following** converts a route into vehicle controls through a
   mobility-specific adapter.
6. **Reactive avoidance** may interrupt route following for an immediate hazard.
   It is an emergency layer, not the route generator.
7. **Goals and behaviors** will eventually choose destinations and tasks without
   acquiring direct control over vehicle mechanics.

## Behavior-to-actuation seam

Human, NPC, vanilla-AI, and script behavior sources are future inputs to this
boundary; none is implemented by this design note. They should converge on a
normalized LAND movement intent before control is considered. Each intent must
bind to the current run and exact vehicle, pass through bounded revocable
authorization, and reach a vehicle-specific adapter that maps it to hardware
controls. `DirectManeuverOperationLease` and the actuator broker model an
experimental run-and-vehicle authorization seam. The server plugin keeps its
direct dispatch closed until a shared authoritative lease and stable controller
priority can be established. Its LAND wheel mapping remains vehicle-specific.
A general behavior-source framework and an AIR actuator adapter are deferred.

The engine-independent core implements layers 2–5 and explicitly models the handoff to layer 6.
The ordinary mod remains observation-only and fails closed. A separate Magnetar server plugin can
apply bounded wheel-system input only inside a VRageCage destructive-lab copy; it is a privileged
development bridge, not production mod authority. Live r4 evidence confirms wheel actuation and
near-target route completion only at a broad tolerance, with tighter approaches timing out. Voidwright
does not fall back to vanilla AI. See [headless actuation](headless-actuation.md) for the evidence,
limitations, and authority boundary.

## Planner invariants

- Start and goal must both fit the vehicle envelope.
- Search work is bounded and reports exhaustion distinctly from no route.
- Identical inputs produce identical routes.
- LAND and AIR share the algorithm, not the world representation.
- AIR searches actual 3D neighbors and inflates obstacles by the vehicle envelope.
- Reactive avoidance never silently rewrites the global planning model.
- Route-following output is finite, normalized to `[-1, 1]`, deterministic, and
  neutral on arrival or invalid input.

## Deliberate omissions

The first slice does not yet sample planet voxels, track dynamic grids, drive
thrusters, deliver precise collision-aware navigation, or replace vanilla
AI-block control. The privileged wheel bridge is confined to disposable test
worlds; broader vehicle support and any ordinary-save actuator require separate
live evidence and an explicit authority design.
