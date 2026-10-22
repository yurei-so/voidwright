# Operating the private depth laboratory

## Authority and deployment state

Blake explicitly authorized this conversation's offline mpai/GPU work and relevant
milestone messages to MamaGPT. That covers the fixed experimental job definitions,
ordinary private file transfers and idle Agent Runtime reloads used here. It does
not grant a model an identity, tools, paid-provider routing, production-world
access or vehicle control. New live-game evaluation needs its own authorization.

Only this new experiment source directory is part of the current implementation.
Existing dirty game/controller/plugin work is preserved. No commits or pushes
were made. No game server was started. No provider API key was read, copied or provisioned.

The operator client reads the existing local control credential internally to
sign requests to Agent Runtime's private Unix socket. It never prints that key,
uses a Roost service signing key or accepts a supplied model actor identity.
The GPU child receives a small explicit environment with no operator credential.

This is resource and lifecycle containment under Blake's existing Unix account.
It is **not** a new OS identity, network-denial sandbox or protection against a
malicious process already running as that account. Fixed code makes no provider
or game calls. Frozen modes and file hashes provide review/reproducibility under
the trusted operator, not immutable storage against the machine owner.

## Installed locations on mpai

- Source versions: `/home/alu52/.local/share/voidwright-depth-lab/`.
- Definition config: `/home/alu52/.config/agent-runtime/experiments.json`.
- Run state: `/home/alu52/.local/state/agent-runtime/experiments.json`.
- Logs/data/checkpoints: `/home/alu52/.local/state/agent-runtime/experiment-artifacts/RUN_UUID/`.
- Exclusive accelerator queue: `/home/alu52/.local/state/runtime-roost/accelerator.json`.
- Pinned Python: `/home/alu52/github/prosody-013-run/.venv/bin/python`.
- Verified environment: Torch 2.6.0+cu124, CUDA 12.4, NumPy 2.5.3, RTX 4070 12 GB.

No environment installation or model download was required. Historical source
roots remain preserved. r5/r6 were staged drafts and never installed as runnable
definitions. New versions never overwrite a completed or failed run.

## Preview, execute, inspect, cancel

Use the Node installed on mpai, `/home/alu52/.local/bin/node`, from the exact
selected frozen source directory. The development checkout is deliberately an
unregistered definition and cannot submit a job as an installed experiment.

```text
node control.mjs preview
node control.mjs execute EXACT_PREVIEW_SHA256
node control.mjs inspect
node control.mjs cancel EXACT_EXPERIMENT_UUID
```

Preview is local and read-only. Execution checks the current plan hash, source
hashes and exact installed root/executable/empty arguments/empty environment/
timeout binding. The digest is a stale-plan guard, not independent authorization.
A durable, private, fsynced journal prevents automatic duplicate submission.
Missing local journal state is reconciled against exact backend plan history;
ambiguous request/approval acknowledgement requires inspection, not blind retry.
Ten isolated coordinator tests cover these cases, concurrent calls and symlinks.

Cancellation is exact-run scoped. A queued run receives a private marker and
exits when Roost ordinarily admits it; it does not jump or edit the queue. A
running r7+ job additionally stops its exact resource scope. Verify backend
terminal state **and** exact lease release. Agent Runtime currently records
intentional nonzero exits as `failed`; exit 130 and the interruption receipt
identify cooperative cancellation. A killed fixture has a null exit code and
`process_exit_null`. These are expected fault-test outcomes, not trained models.

## Process ownership and limits

The original r1–r4 runner tracked a wrapper around a detached transient service.
Normal cancellation passed, but a wrapper crash could leave GPU work alive after
lease release. All nine definitions created here using that runner are now
retired to a harmless stdlib executable that exits 78 before CUDA. Their IDs and
historical records remain available. Original frozen files remain unchanged.

r7+ verifies the actual Agent Runtime service MainPID is its direct parent, the
exact server-assigned run/definition is running, and that run owns the sole active
Roost **experiment** accelerator lease. Shell-launched or borrowed-lease execution
is rejected. It then moves **its own PID** into a transient scope and waits for
actual kernel cgroup membership before initializing CUDA. `exec` retains that
same PID. Sequential campaign fits also run within that process. The scope is
bound to Agent Runtime's service lifetime.

Verified scope properties:

| Limit | Value |
|---|---:|
| Host memory | 6 GiB |
| CPU quota | 4 cores |
| Tasks | 128 |
| Scope runtime | 1,250 seconds |
| Stop grace | 20 seconds |
| Outer manager timeout | 1,320 seconds |
| PyTorch allocation fraction | 40% of GPU memory |

The GPU fraction is a PyTorch allocator limit, not a hardware partition.
Campaign deadline is 1,150 seconds; per-fit allowance is at most 500 seconds.
Artifact/log privacy is 0700/0600 with a 0077 child umask. Frozen source files are
0400 and executable entrypoints 0500. Actual tests verified the exact GPU PID,
start ticks, parent, resource scope, CUDA result and lease before signaling only
the disposable fixture. Both crash and cancellation receipts show no residual
fixture PID/scope and the exact lease ended. Same-PID campaign receipts verify
both fits used the tracked CUDA PID.

## Service changes and recovery

Definition discovery happens only at Agent Runtime startup. Before each reload,
authenticated reads confirmed ordinary jobs were terminal and no experiments
were queued/running. Only idle `agent-runtime.service` was reloaded; Context,
SSO/tunnels, actor grants, game services and routing policy were not changed.
Readiness includes service state and successful control-socket response; a
successful restart command alone can precede socket creation.

Private full config snapshots are stored under the remote source base as
`experiments-before-r2.json`, `...-r3.json`, `...-r4.json`, `...-r7.json`,
`...-r8.json`, `...-r9.json`, and `...-r10.json`; the initial snapshot is inside `20261015-r1/`.
They include definitions belonging to other work. Do not blindly restore an old
snapshot: that would re-enable the superseded detached runners and overwrite
later unrelated definitions. To disable a current definition, retain its ID and
history and point only that exact definition to the existing harmless retired
entrypoint, after an idle preflight and private backup. Do not mutate run/lease
history or stop unrelated work to obtain an idle reload.

## Checkpoints, exports and timing

Reference checkpoints are the **first preregistered r4 seed 48001** for each
loadout, not the best test seed. Hashes pin checkpoint, retained test NPZ and the
original source manifest. Only the known TorchVersion metadata wrapper is
allowlisted for `weights_only=True` loading; arbitrary pickle loading is absent.

Each export backend must separately pass the original absolute logit tolerance
1e-4 and threshold-decision parity across 256 retained cases × nominal/four
camera losses (1,280 cases per loadout). r7/r8 intentionally stop when TorchScript
misses that tolerance. r9 records all TorchScript failures without weakening it
and qualifies CUDA graph replay independently. Inspect `backend_qualification`;
a retained file or a completed benchmark job does not make every backend usable.
No export is promoted to live control. r10 repeats the same fixed inputs after
materializing compressed arrays once outside the timer. r9 host timing included
repeated archive inflation and is not the resident-tensor advisory timing.

Timings synchronize CUDA and distinguish device-ready eager/TorchScript/graph
execution from CPU validation + transfer + graph + advisory. They exclude game
capture, transport, timestamp validation, physics, braking and actuation. A fast
model is not a measured end-to-end driving system.

`advisory.py` rejects invalid masks/numeric rotations and out-of-training-domain
conditioning; partial feeds are invalidated as whole roles. No-hit-only frames
cannot establish support. Even an agreeing model and geometry return only
`candidate-for-controller-review`, always with `actuation_authorized: false`.
Loss of a valid camera latches a replan requirement outside the model.

## Handoff and cleanup

The private workstation evidence index `CURRENT.json` records run UUIDs, source
and artifact locations, verification and remaining gates. Preserve failed
candidates as evidence. Finish by checking all owned requests terminal, exact
leases ended, owned scopes gone, no owned GPU PID, and healthy services. Do not
unload somebody else's model or stop a service merely to make a GPU screenshot
look empty. Never treat unavailable protected reads as an empty inbox or queue.
