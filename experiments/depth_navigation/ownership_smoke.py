"""Harmless CUDA ownership fixture, stopped/killed only by its supervisor."""
import argparse,json,os,signal,time
from pathlib import Path
import torch


def main():
    p=argparse.ArgumentParser();p.add_argument('--config',type=Path,required=True);p.add_argument('--out',type=Path,required=True);args=p.parse_args()
    args.out.mkdir(mode=0o700,parents=True,exist_ok=False)
    owner=json.loads((args.out.parent/'scope-owner.json').read_text())
    if owner['pid']!=os.getpid()or owner['ppid']!=os.getppid():raise RuntimeError('Lease process identity changed')
    torch.cuda.set_per_process_memory_fraction(.4)
    a=torch.ones((64,64),device='cuda');b=a@a;torch.cuda.synchronize()
    ready=dict(schema='voidwright.gpu-ownership-smoke.v1',pid=os.getpid(),ppid=os.getppid(),start_ticks=owner['start_ticks'],
        scope=owner['scope'],run_id=owner['run_id'],gpu=torch.cuda.get_device_name(0),gpu_value=float(b[0,0]),
        gpu_owner_pid_equals_manager_child=True)
    (args.out/'ownership-ready.json').write_text(json.dumps(ready,indent=2)+'\n');print(json.dumps(ready),flush=True)
    stopped=False
    def stop(signum,frame):
        nonlocal stopped
        stopped=True
    signal.signal(signal.SIGTERM,stop);signal.signal(signal.SIGINT,stop)
    deadline=time.monotonic()+120
    while not stopped and time.monotonic()<deadline:
        marker=os.environ.get('VOIDWRIGHT_CANCEL_FILE')
        if marker and Path(marker).exists():stopped=True;break
        time.sleep(.1)
    (args.out/'result.json').write_text(json.dumps(dict(stopped=stopped,pid=os.getpid(),scope=owner['scope'],actuation_authorized=False),indent=2)+'\n')
    return 130 if stopped else 0


if __name__=='__main__':raise SystemExit(main())
