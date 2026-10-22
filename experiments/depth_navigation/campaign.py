"""Fixed bounded matrix; every sequential fit runs in the lease-owned PID."""
import argparse
import json
import re
import signal
import os
import runpy
import gc
import sys
import time
from pathlib import Path


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--config',type=Path,required=True);parser.add_argument('--out',type=Path,required=True);args=parser.parse_args()
    config=json.loads(args.config.read_text());runs=config['experiments']
    if not 1<=len(runs)<=12 or len({r['name']for r in runs})!=len(runs):raise ValueError('Invalid matrix size/identities')
    if any(not re.fullmatch('[a-z0-9-]{1,60}',r['name'])for r in runs):raise ValueError('Invalid run name')
    args.out.mkdir(mode=0o700,parents=True,exist_ok=True)
    if (args.out/'campaign.json').exists():raise ValueError('Matrix directory already used')
    deadline=time.monotonic()+1150
    summary={'schema':'voidwright.depth-campaign.v1','runs':[],'state':'running'}
    def persist():
        temp=args.out/'campaign.tmp';temp.write_text(json.dumps(summary,indent=2)+'\n');temp.replace(args.out/'campaign.json')
    persist()
    cancelled=False
    def cancel(signum,frame):
        nonlocal cancelled
        cancelled=True
    signal.signal(signal.SIGTERM,cancel);signal.signal(signal.SIGINT,cancel)
    for run in runs:
        if cancelled or time.monotonic()>deadline:
            summary['state']='interrupted';persist();return 130
        protocol=dict(run['config']);protocol['wall_seconds']=min(500,max(1,int(deadline-time.monotonic())))
        config_path=args.out/(run['name']+'.config.json');config_path.write_text(json.dumps(protocol,indent=2)+'\n')
        row={'name':run['name'],'state':'running','config':protocol};summary['runs'].append(row);persist()
        old_argv=sys.argv
        try:
            sys.argv=[str(Path(__file__).with_name('train.py')),'--config',str(config_path),'--out',str(args.out/run['name'])]
            try:runpy.run_path(sys.argv[0],run_name='__main__');code=0
            except SystemExit as error:code=error.code if isinstance(error.code,int)else 1
            except Exception as error:code=1;row['error']=str(error)[:500]
        finally:
            sys.argv=old_argv;signal.signal(signal.SIGTERM,cancel);signal.signal(signal.SIGINT,cancel)
            gc.collect()
            if 'torch'in sys.modules:sys.modules['torch'].cuda.empty_cache()
        row['pid']=os.getpid()
        row.update(state='completed'if code==0 else'failed',exit_code=code);persist()
        if code!=0:
            summary['state']='interrupted'if cancelled or code in(2,130)else'failed';persist();return code
    summary['state']='completed';persist();return 0


if __name__=='__main__':raise SystemExit(main())
