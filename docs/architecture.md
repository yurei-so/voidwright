# Voidwright architecture

Voidwright separates the question **what should this agent do?** from **how can
this particular vehicle safely get there?**

## Layers

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

The engine-independent core implements layers 2–5 and explicitly models the handoff to layer 6.
Game-facing actuation remains fail-closed. Live headless tests established that per-wheel public
overrides can be accepted and read back without moving an unpiloted grid, while the internal grid
wheel-system input that does move wheels is prohibited by the Space Engineers mod-script
whitelist. Voidwright therefore does not pretend that either route is a working control adapter,
and it does not fall back to vanilla AI. See [headless actuation](headless-actuation.md) for the
evidence and the next architectural decision.

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
thrusters/wheels, or replace vanilla AI-block control. Those
belong in game-facing adapters and require live evidence from the current Space
Engineers API before implementation.
