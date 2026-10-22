# V3 Red Truck GPS trial — 2026-10-09

## Result

The Red Truck moved under its depth-guided controller toward `End Position` in
the disposable V3 lab copy. The bounded run timed out at 1,800 simulation ticks;
it did **not** arrive. The Observation Towers were not a target.

| Field | Evidence |
| --- | --- |
| Instance | `test-suite-world-v3-gravity-001` |
| Fixture ID | `bebd44898770362960c21201ebf851bf` |
| Operation / run | `e50b1763161ec6b51fb41224a63beb36` / `09412509f8e8b7fccb55f1b25db2f7ed` |
| Vehicle | `[Voidwright] Red Truck`, grid `138162475347618642` |
| Controller / depth camera | `90164788286885540` / `109088616374925990` (`Truck Red Front Cam`) |
| Target | `End Position` at `[7781.38, 894.12, 9155.51]` |
| Initial / final target distance | `77.857 m` / `68.520 m` |
| Path / COM displacement | `10.175 m` / `10.125 m` |
| Limit / elapsed | `0.5 m/s`, `1,800 ticks` (30 seconds at nominal 60 ticks/s) |
| Terminal state | `timeout`; no arrival within the 3.1 m radius |
| Shutdown | Confirmed clean; service inactive afterward |

The final depth-controller samples reported a clear scan, 8 m minimum corridor
clearance, fresh data (4 ticks old), and no self-grid hits. The final
position-derived speed sample was `0.521 m/s`, while engine-reported linear
speed was `0.354 m/s`; that small overshoot/disagreement merits telemetry and
speed-cap follow-up before treating `0.5 m/s` as a hard physical ceiling.

## Method and limits

The action was issued directly through VRageCage's local lab CLI to exercise the
real server plugin/controller path. The MCP adapter's build and tests passed,
including rejection of `Observation Towers`, but this session did not have a
live MCP client connected to invoke that tool; its automated integration tests
use a fake CLI. Do not describe this trial as an AI agent's MCP invocation.

An older Blue Truck receipt in the fixture reported `complete`, but recorded
only `0.145 m` path length and `0.036 m` COM displacement, with the target
already about `0.5 m` away. It is not evidence of meaningful driving or a
waypoint arrival and was not reused.

The trial changed the sector state of the disposable lab copy. Its fixture edit
ledger remains valid with the same single pre-existing edit; the world service
is stopped. Start a fresh copy before repeating this exact test. The retained
VRageCage quarantine receipt is under
`Quarantine/09412509f8e8b7fccb55f1b25db2f7ed/terminal-receipt.json` on the lab
worker.
