# Movement intent, authorization, and LAND actuation

This checkout has a partial separation across three boundaries:

- **Intent:** `RouteFollower` and `DepthAvoidancePolicy` compute bounded route
  guidance and obstacle-corridor decisions without writing game controls.
- **Authorization:** `MovementCommandGate` and the run-bound host/mod
  preflight protocol bind dispatch to a run, operation, vehicle, fresh evidence,
  and the selected motion owner.
- **LAND actuation:** `VoidwrightLabPreflight.ApplyOneBoundedStep` converts the
  accepted LAND guidance into wheel steering and propulsion overrides. That
  adapter is the only place that directly applies ordinary mod-owned LAND
  guidance; the host reviews mod-owned depth commands and returns before its
  own legacy movement path.

The LAND executor still coordinates guidance, local safety checks, and setter
calls in one method. The policy and actuation boundaries are nevertheless
distinct enough for this task; no navigation framework or NPC/AIR refactor is
needed. For the direct-maneuver zero-motion experiment, a focused coupling
guard now marks stop/hold-only plans and rejects any nonzero or nonfinite wheel
input before a host setter. The general movement path and its fail-closed
authorization gates remain unchanged.
