"""Render retained offline evidence; never loads a checkpoint or calls a model."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw


def main():
    p=argparse.ArgumentParser();p.add_argument("directory",type=Path);p.add_argument("--out",type=Path,required=True);args=p.parse_args()
    data=np.load(args.directory/"test.npz",allow_pickle=False)
    records=json.loads((args.directory/"test.scenes.json").read_text())
    result_path=args.directory/"result.json"
    result=json.loads(result_path.read_text())if result_path.is_file()else None
    prediction=args.directory/"test.predictions.npz"
    probabilities=np.load(prediction,allow_pickle=False)["probabilities"]if prediction.is_file()else None
    chosen=[]
    if result and probabilities is not None:
        bad=np.flatnonzero((data['labels']>.5)&(probabilities<result['threshold'])&(data['baseline']!=.5))
        chosen.extend(bad[:8].tolist())
    for family in dict.fromkeys(x['family']for x in records):
        candidates=[i for i,x in enumerate(records)if x['family']==family and i not in chosen]
        for label in [0,1]:
            found=next((i for i in candidates if data['labels'][i]==label),None)
            if found is not None and found not in chosen:chosen.append(found)
    chosen=chosen[:24]
    tile_width,tile_height=310,310
    canvas=Image.new('RGB',(tile_width*4,tile_height*((len(chosen)+3)//4)+50),'#121821');draw=ImageDraw.Draw(canvas)
    draw.text((14,12),'SYNTHETIC ONLY | full FOV depth + explicit invalid mask | oracle includes hidden hazards',fill='white')
    for slot,index in enumerate(chosen):
        x,y=(slot%4)*tile_width,(slot//4)*tile_height+45
        frame=data['images'][index];depth,mask=frame[:2]
        color=np.zeros((*depth.shape,3),dtype=np.uint8)
        color[...,0]=np.clip(255*(1-depth),0,255).astype(np.uint8)
        color[...,1]=np.clip(180*depth,0,255).astype(np.uint8)
        color[...,2]=np.clip(255*depth,0,255).astype(np.uint8)
        color[mask<.5]=(36,39,46)
        image=Image.fromarray(color)
        scale=min(280/image.width,230/image.height)
        image=image.resize((int(image.width*scale),int(image.height*scale)),Image.Resampling.NEAREST)
        canvas.paste(image,(x+12,y))
        r=records[index];truth='HAZARD'if data['labels'][index]else'CLEAR'
        text=f"#{index} {r['family']} | oracle {truth}\nwidth {r['vehicle_width']:.1f}m / height {r['vehicle_height']:.1f}m / heading {r['heading']:.2f}\n"
        if probabilities is not None:text+=f"model risk {probabilities[index]:.3f} | geometry {data['baseline'][index]:.1f}"
        draw.multiline_text((x+12,y+235),text,fill='#f0cd70'if truth=='HAZARD'else'#a3d5be',spacing=3)
    args.out.parent.mkdir(parents=True,exist_ok=True);canvas.save(args.out);print(args.out)


if __name__=='__main__':main()
