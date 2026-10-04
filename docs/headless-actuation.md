# Headless actuation boundary

Voidwright's route planner and follower can produce bounded LAND guidance, but
the current Space Engineers mod-script surface does not provide a verified way
to apply that guidance to an unpiloted wheeled grid.

## Verified on the real dedicated-server fixture

- Setting public suspension propulsion and steering overrides was accepted and
  read back on four healthy, attached wheels, but the dynamic grid remained at
  zero speed.
- The public controller movement path depends on player/extended-control
  propagation and likewise did not actuate the headless grid.
- Direct grid wheel-system angular velocity is the engine path that reaches the
  wheel subsystem, but Magnetar's real script compiler rejected that member as
  prohibited. The rejected build was not retained.
- Vanilla autopilot is intentionally not a fallback. Voidwright must own and
  expose its authority rather than quietly delegating to the system it replaces.

The imported world remains fixture data, not a schema. Observed controller
names and block inventories are reported as evidence; guesses about whether a
grid is a truck, hovercraft, or trailer are never baked into classification.

## Privileged development adapter

A working headless adapter needed one of:

1. a sanctioned public game API that propagates autonomous wheel input; or
2. a narrowly scoped, separately reviewed server-plugin bridge that accepts a
   bounded command for an explicitly armed controller and applies the internal
   wheel input outside the mod-script sandbox.

The second option is now implemented only behind VRageCage's destructive-lab
boundary. It uses a universal harness principal rather than player/faction
identity and treats every copied grid as an unsupported development target.
Commands are bounded and one-shot, neutralize on completion/timeout/disposal,
and remain correlated with immutable source, package, log, run, and terminal
receipt hashes. It must never initialize in an ordinary save.

VRageCage destructive-lab instances provide that boundary for development. The
harness validates the full instance/import/run contract before launch; the
`ServerPlugin` independently checks the activated lab identity and quarantine
before initialization. Neither check is a standalone authorization system.
The initialization marker only proves that the gate opened; a separately
correlated terminal receipt proves whether a command actuated and neutralized a
fixture. A live bounded-distance command has completed and stopped a copied
rover without creating or spoofing a human player identity.
