# Virtual camera marker trial — 2026-10-13

## Scope

Added a display-only client marker for Voidwright virtual camera poses. The server broadcasts a bounded pose packet for the current Voidwright run. The client renders local `VW CAM`, `VW LOOK`, and `VW UP` GPS points, with active and stale colors. The marker does not create a physical game entity or control a vehicle.

## Verification

- Agent MCP build passed; its 20 checks passed, including moving-grid pose correspondence and malformed/stale packet handling.
- Game mod build passed with zero warnings or errors; 29 Core checks passed.
- An isolated package containing only the marker-related game files was staged in the approved disposable fixture `test-suite-world-v2-r3-voidwright-unified-20261013-c` (tree SHA-256 `1faad3304acbc536c221ef0ff623b63859daebca4dc5c0d23a19fd25850f071b`).
- The game loaded the mod and logged that it broadcast one display-only camera pose for the current run. The pose packet appeared in the fixture's world storage.
- The live client display and marker tracking while driving were not independently confirmed in this task. Server-side broadcast is not proof of client-side rendering.

## Shutdown

After the user said they had finished and disconnected, the fresh character observation reported `no_connected_humans` with count 0. The temporary marker bridge was stopped. VRageCage stopped the fixture cleanly; the final receipt reported `fixture_edit_count: 0`, `passed: true`, `clean_shutdown: true`, and no target fatal markers. Final service state was inactive/dead. Run ID: `7562f64048564643037c2cd6700d8efa`; terminal receipt: `Quarantine/7562f64048564643037c2cd6700d8efa/terminal-receipt.json` (SHA-256 `dd449c0a3918e35b400483b1d3ee3c9457e8cf1ac96c00ed90d9278e636ba273`).

No production deployment, runtime configuration change, commit, or push was made. Existing unrelated dirty files were preserved.

## Client visibility correction

The first live client check found no markers. The client's Space Engineers log for the join at 14:59 showed only four published workshop mods and no Voidwright script; the private Magnetar fixture loads Voidwright on the server only. The original secure-message receiver therefore could not run on the client. The server's broadcast log alone did not establish a visible marker.

The marker manager was changed to use the game's built-in server-to-player GPS replication. It creates and updates the same three display-only points per virtual camera for each connected human player, and removes them when that player departs or the session unloads. The isolated package compiled with zero warnings or errors, then was staged under the same synthetic mod ID with tree SHA-256 `18b7751fe23f9b407b14005ed65dc10cf90d1a34349b3de14174fdadf2ca4826`. The server was restarted only after a fresh zero-human observation, and its new log says it is syncing one pose through vanilla GPS. On the second client check, the user confirmed that `VW CAM`, `VW LOOK`, and `VW UP` appeared and followed the Blue Utility Truck while keeping their relative shape. The server-side pose packet independently moved from approximately `(7.08, 12.73, -4.83)` at tick 4600 to `(25.13, -14.55, -24.91)` at tick 5500 while its orientation changed. The live marker feed is bounded to 30 minutes and will display stale status after updates cease. The server remains running while the user is connected; no new shutdown receipt has been taken for this run.
