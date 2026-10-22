#!/usr/bin/python3
"""Put THIS lease-owned PID in a bounded scope, then exec the GPU program.

There is no detached GPU service/wrapper lifetime gap. Campaigns run sequential
fits in this same process too, so the manager's tracked child owns all CUDA work.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parent


def verify_lease(run_id,definition):
    # These identities come from the service/kernel and their private state,
    # never from a model-facing actor/PID claim or a supplied signing key.
    status=subprocess.run(['systemctl','--user','show','agent-runtime.service','-p','MainPID','--value'],
        env={**os.environ,'XDG_RUNTIME_DIR':'/run/user/'+str(os.getuid()),'DBUS_SESSION_BUS_ADDRESS':'unix:path=/run/user/'+str(os.getuid())+'/bus'},
        capture_output=True,text=True,timeout=10,check=True)
    parent=int(status.stdout.strip())
    if parent<=1 or os.getppid()!=parent:raise PermissionError('Only the active runtime child may execute this fixed GPU definition')
    run_file=Path.home()/'.local/state/agent-runtime/experiments.json'
    lease_file=Path.home()/'.local/state/runtime-roost/accelerator.json'
    def load(path):
        if path.is_symlink()or path.stat().st_size>4*1024**2:raise PermissionError('Invalid private runtime state')
        return json.loads(path.read_text())
    runs=load(run_file)['runs'];leases=load(lease_file)['requests']
    matches=[r for r in runs if r.get('id')==run_id and r.get('definition_id')==definition and r.get('status')=='running']
    active=[r for r in leases if r.get('state')=='running']
    if len(matches)!=1 or len(active)!=1 or active[0].get('runtime_id')!='agent-runtime' or active[0].get('workload',{}).get('class')!='experiment' or active[0].get('workload',{}).get('run_id')!=run_id:
        raise PermissionError('The exact run does not own the active experiment accelerator lease')
    return parent


def main():
    manifest = json.loads((ROOT / "manifest.json").read_text())
    for name, expected in manifest["files"].items():
        if Path(name).name != name or hashlib.sha256((ROOT / name).read_bytes()).hexdigest() != expected:
            raise RuntimeError("Frozen experiment source differs from reviewed manifest: " + name)
    artifact = Path(os.environ["ROOST_EXPERIMENT_ARTIFACT_DIR"]).resolve()
    # This path is assigned by ExperimentManager, never by the model subject.
    if artifact.parent != Path.home() / ".local/state/agent-runtime/experiment-artifacts":
        raise ValueError("Unexpected experiment artifact parent")
    run_id = str(uuid.UUID(artifact.name))
    if run_id!=artifact.name:raise ValueError('Noncanonical server run identity')
    runtime_parent=verify_lease(run_id,manifest['definition'])
    unit = "voidwright-depth-" + run_id + '.scope'
    cancellation = ROOT.parent / ("cancel-" + run_id + ".json")
    if cancellation.exists():
        print(json.dumps({"status": "cancelled-before-unit", "run_id": run_id}), flush=True)
        return 130
    python = manifest["python"]
    if not Path(python).is_file():
        raise ValueError("Pinned Python runtime is unavailable")
    config = ROOT / "protocol.json"
    entrypoint=manifest.get("entrypoint","train.py")
    if entrypoint not in ("train.py","campaign.py","benchmark.py","ownership_smoke.py"):
        raise ValueError("Unsupported fixed experiment entrypoint")
    os.umask(0o077)
    bus_env = {"PATH": "/usr/local/bin:/usr/bin:/bin", "LANG": "C.UTF-8", "HOME": str(Path.home()),
           "XDG_RUNTIME_DIR": "/run/user/" + str(os.getuid()),
           "DBUS_SESSION_BUS_ADDRESS": "unix:path=/run/user/" + str(os.getuid()) + "/bus"}
    properties=[('PIDs','au',['1',str(os.getpid())]),('MemoryMax','t',[str(6*1024**3)]),
        ('CPUQuotaPerSecUSec','t',['4000000']),('TasksMax','t',['128']),('RuntimeMaxUSec','t',['1250000000']),
        ('TimeoutStopUSec','t',['20000000']),('CollectMode','s',['inactive-or-failed']),
        ('BindsTo','as',['1','agent-runtime.service']),('After','as',['1','agent-runtime.service'])]
    command=['busctl','--user','call','org.freedesktop.systemd1','/org/freedesktop/systemd1',
        'org.freedesktop.systemd1.Manager','StartTransientUnit','ssa(sv)a(sa(sv))',unit,'fail',str(len(properties))]
    for name,kind,values in properties:command.extend([name,kind,*values])
    command.append('0')
    admitted=subprocess.run(command,env=bus_env,capture_output=True,text=True,timeout=10)
    if admitted.returncode:raise RuntimeError('Own-process scope admission failed: '+admitted.stderr[:500])
    for _ in range(50):
        cgroup=Path('/proc/self/cgroup').read_text().strip()
        if cgroup.split('/')[-1]==unit:break
        time.sleep(.1)
    else:raise RuntimeError('Own PID was not placed in its resource scope; refuse CUDA startup')
    if os.getppid()!=runtime_parent:raise PermissionError('Runtime parent changed during scope admission')
    owner=dict(schema='voidwright.depth-scope-owner.v1',run_id=run_id,pid=os.getpid(),ppid=os.getppid(),scope=unit,
        cgroup=cgroup,start_ticks=Path('/proc/self/stat').read_text().rsplit(')',1)[1].split()[19],
        gpu_owner='This same PID after exec; no detached GPU child/service',exact_runtime_and_lease_verified=True)
    with (artifact/'scope-owner.json').open('x')as stream:json.dump(owner,stream,indent=2);stream.flush();os.fsync(stream.fileno())
    if cancellation.exists():return 130
    env={'PATH':'/usr/local/bin:/usr/bin:/bin','LANG':'C.UTF-8','HOME':str(Path.home()),
        'CUBLAS_WORKSPACE_CONFIG':':4096:8','OMP_NUM_THREADS':'4','OPENBLAS_NUM_THREADS':'4',
        'PYTHONUNBUFFERED':'1','CUDA_VISIBLE_DEVICES':'0','VOIDWRIGHT_CANCEL_FILE':str(cancellation),
        'VOIDWRIGHT_SCOPE_UNIT':unit}
    os.chdir(ROOT)
    # Same PID, same parent/stdio, same scope: the lease follows CUDA lifetime.
    os.execve(python,[python,str(ROOT/entrypoint),'--config',str(config),'--out',str(artifact/'training')],env)


if __name__ == "__main__":
    raise SystemExit(main())
