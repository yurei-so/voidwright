"""Analyze retained predictions without training, threshold tuning or tool calls."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import numpy as np
from metrics import metrics


def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def analyze(root):
    campaign=json.loads((root/'campaign.json').read_text())
    if campaign['state']!='completed':raise ValueError('Only completed campaigns are analyzed')
    rows=[];pairs=[];seen_baselines=set()
    for run in campaign['runs']:
        directory=root/run['name'];result=json.loads((directory/'result.json').read_text());config=result['config']
        for file in directory.glob('*.npz'):
            if result['data'].get(file.name)!=digest(file):raise ValueError('Retained NPZ differs from run receipt: '+str(file))
        for split in ['test','camera_vehicle_shift','heldout_terrain']:
            file=directory/(split+'.predictions.npz')
            data=np.load(file,allow_pickle=False);labels=data['labels'];probabilities=data['probabilities'];baseline=data['baseline']
            measured=metrics(labels,probabilities,result['threshold'],baseline!=.5)
            original=result['evaluations'][split]['model']
            for key in ['false_safe','safe_predictions','hazardous','eligible']:
                if measured[key]!=original[key]:raise ValueError('Recomputed metric mismatch: '+key)
            row=dict(cohort=root.name,run=run['name'],seed=config['seed'],model=result.get('model_type','tiny_depth'),
                encoding=config['encoding'],loadout=config['loadout'],split=split,parameters=result['parameters'],
                p50_ms=result['latency_ms']['p50'],**measured)
            geometry=metrics(labels,baseline,.25)
            n,k,bad=geometry['safe_predictions'],measured['safe_predictions'],geometry['false_safe']
            if k<=n and n:
                draws=np.random.default_rng(907).hypergeometric(bad,n-bad,k,10000)
                row['matched_coverage_geometry']=dict(expected_false_safe=bad*k/n,
                    reference_interval95=np.quantile(draws,[.025,.975]).tolist(),observed_model_false_safe=measured['false_safe'],
                    scope='Random subset of geometry permissions at matched coverage; descriptive reference, not a formal model-selection test')
            rows.append(row)
            # The learned threshold's evidence-eligibility policy and the
            # stricter controller geometry veto are different decisions.
            # Preserve both; do not describe the former as geometry-guarded.
            guarded=metrics(labels,probabilities,result['threshold'],baseline==0)
            recorded_guard=result['evaluations'][split]['model_with_geometry_guard']
            for name in ['false_safe','safe_predictions','hazardous','eligible']:
                if guarded[name]!=recorded_guard[name]:raise ValueError('Guarded metric mismatch: '+name)
            rows.append(dict(cohort=root.name,run=run['name'],seed=config['seed'],model=result.get('model_type','tiny_depth')+'_geometry_guard',
                encoding=config['encoding'],loadout=config['loadout'],split=split,parameters=result['parameters'],
                p50_ms=result['latency_ms']['p50'],**guarded))
            key=(config['seed'],split)
            if key not in seen_baselines:
                rows.append(dict(cohort=root.name,run='geometry-'+str(config['seed']),seed=config['seed'],model='geometry',
                    encoding='none',loadout=config['loadout'],split=split,parameters=0,p50_ms=None,**geometry))
                seen_baselines.add(key)
    for seed in sorted({run['config']['seed']for run in campaign['runs']}):
        first=root/(str(seed)+'-numeric');second=root/(str(seed)+'-strip')
        if first.is_dir()and second.is_dir():
            a=np.load(first/'test.npz',allow_pickle=False);b=np.load(second/'test.npz',allow_pickle=False)
            np.testing.assert_array_equal(a['images'][:,:2],b['images'][:,:2]);np.testing.assert_array_equal(a['labels'],b['labels'])
            np.testing.assert_array_equal(a['conditions'][:,8:],b['conditions'][:,8:])
            np.testing.assert_array_equal(a['conditions'][:,:6],b['conditions'][:,:6])
            if json.loads((first/'test.scenes.json').read_text())!=json.loads((second/'test.scenes.json').read_text()):raise ValueError('Paired scene metadata differs')
            pairs.append(dict(seed=seed,paired_depth_masks_labels_cameras_vehicle_and_scenes_identical=True,
                only_encoding_differs='numeric heading conditions versus external image marker'))
        first=root/(str(seed)+'-tiny-depth');second=root/(str(seed)+'-projected-depth')
        if first.is_dir()and second.is_dir():
            a=np.load(first/'test.npz',allow_pickle=False);b=np.load(second/'test.npz',allow_pickle=False)
            for key in a.files:np.testing.assert_array_equal(a[key],b[key])
            pairs.append(dict(seed=seed,paired_architecture_input_arrays_identical=True))
    return rows,pairs


def aggregate(rows):
    groups={}
    for row in rows:
        key=(row['cohort'],row['model'],row['encoding'],row['loadout'],row['split'])
        groups.setdefault(key,[]).append(row)
    output=[]
    from metrics import wilson
    for key,values in groups.items():
        counts={name:sum(r[name]for r in values)for name in ['samples','hazardous','false_safe','safe_predictions','eligible']}
        n,hazard,bad,allowed=counts['samples'],counts['hazardous'],counts['false_safe'],counts['safe_predictions']
        output.append(dict(zip(['cohort','model','encoding','loadout','split'],key)),)
        output[-1].update(seeds=len(values),**counts,coverage=allowed/n,false_safe_rate=bad/max(1,hazard),
            unsafe_per_permission=bad/allowed if allowed else None,unsafe_per_permission_wilson95=wilson(bad,allowed))
    return output


def plot(rows,output):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    groups=sorted({r['cohort']for r in rows});fig,axes=plt.subplots(len(groups),3,figsize=(14,4*len(groups)),squeeze=False)
    legend={}
    colors={('tiny_depth','numeric'):'#3478be',('tiny_depth','strip'):'#ae6cb1',('conditioning_only','numeric'):'#c58434',
            ('geometry','none'):'#5d8d62',('projected_depth','numeric'):'#ce4949'}
    for group_index,group in enumerate(groups):
        for column,split in enumerate(['test','camera_vehicle_shift','heldout_terrain']):
            axis=axes[group_index,column];labels=set();abstained=[]
            for row in rows:
                if row['cohort']!=group or row['split']!=split:continue
                names={'tiny_depth':'Image CNN','projected_depth':'Shared point model','geometry':'Geometry','conditioning_only':'Metadata only'}
                base_model=row['model'].removesuffix('_geometry_guard')
                name=names.get(base_model,base_model)+(' + geometry veto'if row['model'].endswith('_geometry_guard')else'')
                if row['encoding']=='strip':name+=' (strip)'
                if row['unsafe_per_permission']is None:abstained.append(name);continue
                color=colors.get((base_model,row['encoding']),'black')
                artist=axis.scatter(row['coverage']*100,row['unsafe_per_permission']*100,color=color,s=40,
                    marker='x'if row['model'].endswith('_geometry_guard')else'o',label=name if name not in labels else None)
                legend.setdefault(name,artist)
                labels.add(name)
            axis.axhline(2,color='#999999',linestyle='--',linewidth=1)
            cohort='Front camera'if group.startswith('front')else'Random camera loadout'
            split_title={'test':'New scenes','camera_vehicle_shift':'Changed cameras / vehicle','heldout_terrain':'Unseen terrain families'}[split]
            axis.set_title(cohort+' | '+split_title);axis.set_xlabel('Queries permitted (%)');axis.set_ylabel('Hazardous among permissions (%)')
            axis.set_xlim(left=0);axis.set_ylim(bottom=0);axis.grid(alpha=.2)
            if abstained:axis.text(.03,.97,str(len(abstained))+' seed/policy results abstained',transform=axis.transAxes,va='top',fontsize=8)
    fig.suptitle('OFFLINE SYNTHETIC RESEARCH | each mark is one seed | dashed line: 2% empirical target',fontsize=13)
    fig.legend(list(legend.values()),list(legend),loc='lower center',ncol=3,fontsize=9,frameon=False)
    fig.tight_layout(rect=(0,.09,1,.97));fig.savefig(output,dpi=160);plt.close(fig)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('campaigns',nargs='+',type=Path);parser.add_argument('--out',type=Path,required=True);args=parser.parse_args()
    args.out.mkdir(mode=0o700,parents=True,exist_ok=False);rows=[];pairs=[]
    for root in args.campaigns:
        a,b=analyze(root);rows.extend(a);pairs.append(dict(cohort=root.name,pairs=b))
    summary=dict(schema='voidwright.depth-analysis.v1',rows=rows,aggregates=aggregate(rows),paired_checks=pairs,
        caveats=['Independent seed scenes; architecture/encoding comparisons are paired within seed.',
            'Wilson intervals are descriptive binomial summaries, not physical safety guarantees.',
            'Different cohort versions have different sampling/eligibility/calibration; cross-version change is not a causal model-only effect.'])
    (args.out/'analysis.json').write_text(json.dumps(summary,indent=2)+'\n')
    columns=['cohort','run','seed','model','encoding','loadout','split','samples','hazardous','false_safe','safe_predictions','coverage','false_safe_rate','unsafe_per_permission','p50_ms']
    with (args.out/'metrics.csv').open('w',newline='')as stream:
        writer=csv.DictWriter(stream,fieldnames=columns,extrasaction='ignore');writer.writeheader();writer.writerows(rows)
    plot(rows,args.out/'risk-coverage.png')
    print(json.dumps(summary['aggregates'],indent=2))


if __name__=='__main__':main()
