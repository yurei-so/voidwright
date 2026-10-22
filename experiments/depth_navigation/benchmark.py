"""Exact-checkpoint offline export/parity/failure/latency experiment."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import time
import zipfile
import numpy as np
import torch
from torch.torch_version import TorchVersion
from projected import ProjectedDepth
from advisory import prepare,advise,CameraLossLatch


def sha(path):
    with path.open('rb')as stream:return hashlib.file_digest(stream,'sha256').hexdigest()


def checked_file(spec,maximum):
    path=Path(spec['path'])
    parent=Path.home()/'.local/state/agent-runtime/experiment-artifacts'
    if path.is_symlink()or not path.is_file()or not path.resolve().is_relative_to(parent)or path.stat().st_size>maximum:
        raise ValueError('Expected bounded owned experiment artifact')
    if sha(path)!=spec['sha256']:raise ValueError('Reference artifact hash changed')
    return path


def main():
    p=argparse.ArgumentParser();p.add_argument('--config',type=Path,required=True);p.add_argument('--out',type=Path,required=True);args=p.parse_args()
    config=json.loads(args.config.read_text());args.out.mkdir(mode=0o700,parents=True,exist_ok=False)
    checkpoint_path=checked_file(config['checkpoint'],64*1024**2);data_path=checked_file(config['data'],128*1024**2)
    source=Path(config['source']['path']);manifest=source/'manifest.json'
    if sha(manifest)!=config['source']['manifest_sha256']:raise ValueError('Reference source manifest changed')
    original=json.loads(manifest.read_text())
    for name in ['projected.py','synthetic.py']:
        if sha(Path(__file__).with_name(name))!=original['files'][name]:raise ValueError('Export model dependencies differ from training')
    with zipfile.ZipFile(data_path)as archive:
        if sum(x.file_size for x in archive.infolist())>512*1024**2:raise ValueError('Reference NPZ expansion exceeds bound')
    if not torch.cuda.is_available():raise RuntimeError('Actual CUDA GPU required')
    torch.set_num_threads(4);torch.cuda.set_per_process_memory_fraction(.4);torch.use_deterministic_algorithms(True)
    # Only the known library's string-version wrapper is added; arbitrary
    # pickle constructors remain disallowed by weights_only loading.
    with torch.serialization.safe_globals([TorchVersion]):
        checkpoint=torch.load(checkpoint_path,map_location='cpu',weights_only=True)
    if checkpoint['config'].get('model')!='projected_depth':raise ValueError('Pinned model architecture differs')
    resolution=checkpoint['config']['resolution'];model=ProjectedDepth(resolution).cuda().eval();model.load_state_dict(checkpoint['model'])
    # NPZ indexing inflates a whole compressed array on every access. Keep
    # owned arrays in memory once; archive I/O is not per-frame inference.
    load_started=time.perf_counter()
    with np.load(data_path,allow_pickle=False)as archive:
        data={name:archive[name]for name in ['images','conditions','labels']}
    data_load_seconds=time.perf_counter()-load_started
    count=min(256,len(data['labels']));threshold=float(config['threshold'])
    allowed_roles=tuple(config.get('allowed_roles',[0]))
    def prepare_input(pixels,context):return prepare(pixels,context,resolution,allowed_roles=allowed_roles)
    samples=[prepare_input(data['images'][i],data['conditions'][i])for i in range(count)]
    image=torch.from_numpy(samples[0]['images'])[None].cuda();context=torch.from_numpy(samples[0]['conditions'])[None].cuda()
    variants=[]
    for sample in samples:
        variants.append((sample['images'],sample['conditions'],'nominal'))
        for camera in range(4):
            lost_image=sample['images'].copy();lost_context=sample['conditions'].copy();width,height=resolution;row,col=divmod(camera,2)
            lost_image[:2,row*height:(row+1)*height,col*width:(col+1)*width]=0;lost_context[8+camera*11+10]=0
            variants.append((lost_image,lost_context,'loss-'+str(camera)))
    with torch.inference_mode():
        lost_image=torch.zeros_like(image);lost_context=context.clone();lost_context[:,18::11]=0
        traced=torch.jit.trace(model,(image,context),check_inputs=[(lost_image,lost_context)],strict=True)
        traced.save(str(args.out/'projected-depth.torchscript.pt'))
        torch.save(dict(model=model.state_dict()),args.out/'weights-only.pt')
        graph=torch.cuda.CUDAGraph();static_image=image.clone();static_context=context.clone()
        stream=torch.cuda.Stream();stream.wait_stream(torch.cuda.current_stream())
        with torch.cuda.stream(stream):
            for _ in range(8):model(static_image,static_context)
        torch.cuda.current_stream().wait_stream(stream)
        with torch.cuda.graph(graph):static_output=model(static_image,static_context)
        maximum_trace=maximum_graph=0.;trace_failures=[];trace_decision_mismatches=0
        loss_new_permissions=loss_new_dangerous=latched_new_permissions=0
        original_candidates={};role_counts={}
        for index,(pixels,conditioning,kind)in enumerate(variants):
            prepared=prepare_input(pixels,conditioning)
            tensor=torch.from_numpy(prepared['images'])[None].cuda();metadata=torch.from_numpy(prepared['conditions'])[None].cuda()
            eager=model(tensor,metadata);scripted=traced(tensor,metadata)
            static_image.copy_(tensor);static_context.copy_(metadata);graph.replay()
            maximum_trace=max(maximum_trace,float(abs(eager-scripted).max()))
            maximum_graph=max(maximum_graph,float(abs(eager-static_output).max()))
            trace_error=float(abs(eager-scripted).max())
            if trace_error>1e-4:
                trace_failures.append(dict(case=index//5,variant=kind,eager=float(eager[0]),scripted=float(scripted[0]),absolute_error=trace_error))
            if maximum_graph>1e-4:
                (args.out/'parity-failure.json').write_text(json.dumps(dict(case=index//5,variant=kind,
                    eager=float(eager[0]),scripted=float(scripted[0]),graph=float(static_output[0]),
                    trace_max_error=maximum_trace,graph_max_error=maximum_graph,tolerance=1e-4),indent=2)+'\n')
                raise AssertionError('Export/capture changed score semantics; exact failing case retained')
            score=float(torch.sigmoid(eager)[0]);decision=advise(prepared,score,threshold);case=index//5
            trace_decision_mismatches+=int((score<threshold)!=(float(torch.sigmoid(scripted)[0])<threshold))
            if (score<threshold)!=(float(torch.sigmoid(static_output)[0])<threshold):
                raise AssertionError('CUDA capture changed a threshold decision')
            role_counts[kind]=role_counts.get(kind,0)+1
            if kind=='nominal':original_candidates[case]=decision['candidate']
            elif decision['candidate']and not original_candidates[case]:
                loss_new_permissions+=1;loss_new_dangerous+=int(data['labels'][case]>.5)
            if kind!='nominal':
                latch=CameraLossLatch();latch.update(samples[case]['available']);allowed=latch.update(prepared['available'])
                latched_new_permissions+=int(decision['candidate']and not original_candidates[case]and allowed)
        all_missing=prepare_input(np.zeros_like(samples[0]['images']),samples[0]['conditions'])
        zero=model(torch.from_numpy(all_missing['images'])[None].cuda(),torch.from_numpy(all_missing['conditions'])[None].cuda())
        if advise(all_missing,float(torch.sigmoid(zero)[0]),threshold)['candidate']:raise AssertionError('Missing cameras manufactured permission')
        poison=samples[0]['images'].copy();poison[0,0,0]=np.nan
        try:prepare_input(poison,samples[0]['conditions'])
        except ValueError:pass
        else:raise AssertionError('Invalid available depth reached the model')
        def timing(operation,iterations=200):
            for _ in range(20):operation()
            torch.cuda.synchronize();values=[]
            for _ in range(iterations):
                start=time.perf_counter();operation();torch.cuda.synchronize();values.append((time.perf_counter()-start)*1000)
            return dict(p50=float(np.median(values)),p95=float(np.percentile(values,95)))
        static_image.copy_(image);static_context.copy_(context)
        timings=dict(eager_device_ready=timing(lambda:model(image,context)),
            torchscript_device_ready=timing(lambda:traced(image,context)),cuda_graph_replay=timing(graph.replay))
        def host_advisory():
            prepared=prepare_input(data['images'][0],data['conditions'][0])
            static_image.copy_(torch.from_numpy(prepared['images'])[None]);static_context.copy_(torch.from_numpy(prepared['conditions'])[None])
            graph.replay();probability=float(torch.sigmoid(static_output)[0].cpu())
            return advise(prepared,probability,threshold)
        timings['cpu_validation_transfer_graph_and_advisory']=timing(host_advisory)
    trace_qualification=dict(qualified=not trace_failures and trace_decision_mismatches==0,strict_logit_absolute_tolerance=1e-4,
        cases=len(variants),score_failures=trace_failures,decision_mismatches=trace_decision_mismatches,
        policy='Each backend qualifies separately under the original tolerance; rejected TorchScript is retained for diagnosis only')
    (args.out/'torchscript-qualification.json').write_text(json.dumps(trace_qualification,indent=2)+'\n')
    result=dict(schema='voidwright.depth-export-benchmark.v2',reference=config,torch=str(torch.__version__),gpu=torch.cuda.get_device_name(0),
        parameters=sum(p.numel()for p in model.parameters()),parity=dict(cases=len(variants),roles=role_counts,trace_max_error=maximum_trace,graph_max_error=maximum_graph),
        backend_qualification=dict(cuda_graph=True,torchscript=trace_qualification['qualified'],torchscript_score_failures=len(trace_failures),torchscript_decision_mismatches=trace_decision_mismatches),
        camera_loss=dict(raw_new_guarded_candidates=loss_new_permissions,raw_new_hazardous_candidates=loss_new_dangerous,
            after_controller_loss_latch_new_candidates=latched_new_permissions),
        guards=dict(all_missing_abstains=True,malformed_available_depth_rejected=True),timings_ms=timings,
        retained_arrays_materialized_once=True,data_load_seconds=data_load_seconds,
        timing_scope='CPU validation + in-memory retained tensors + transfer/inference/advisory only; archive inflation is outside timing; excludes game acquisition, transport, timing provenance, physics and actuation',
        exports={name:sha(args.out/name)for name in ['projected-depth.torchscript.pt','weights-only.pt']},
        source={p.name:sha(p)for p in Path(__file__).parent.glob('*.py')},promoted=False,actuation_authorized=False)
    (args.out/'result.json').write_text(json.dumps(result,indent=2)+'\n')
    (args.out/'model-contract.json').write_text(json.dumps(dict(schema='voidwright.offline-model.v1',shape=[1,3,image.shape[2],image.shape[3]],
        condition_count=52,encoding='numeric',allowed_camera_roles=list(allowed_roles),model='projected_depth',parameters=result['parameters'],threshold=threshold,
        required_initial_support='Upright gravity-aligned supported start; finite synthetic geometric query, not wheel physics',
        qualification='Experimental, not promoted; exact source/checkpoint and synthetic protocol only',backend_qualification=result['backend_qualification'],
        exports=result['exports'],actuation_authorized=False),indent=2)+'\n')
    print(json.dumps(result,indent=2));return 0


if __name__=='__main__':raise SystemExit(main())
