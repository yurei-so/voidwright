"""Offline inspection of retained game frames, not a live sensor authority.

Legacy captures lack a common run/tick and controller frame. This reader may
verify bytes and visualize them, but never invents those bindings or enables
model inference/vehicle control from an old capture.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import stat
import re
import numpy as np

ROLES=('front','rear','left','right')


def canonical_id(value):
    if isinstance(value,bool)or not isinstance(value,(int,str))or not str(value).isascii()or not str(value).isdigit():
        raise ValueError('Exact positive decimal entity ID required')
    text=str(value)
    if str(int(text))!=text or not 0<int(text)<=9223372036854775807:raise ValueError('Invalid canonical entity ID')
    return text


def bounded_read(path,maximum):
    descriptor=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
    with os.fdopen(descriptor,'rb')as stream:
        info=os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode)or info.st_size>maximum:raise ValueError('Expected bounded regular frame file')
        data=stream.read(maximum+1)
        if len(data)>maximum:raise ValueError('Frame file exceeds bound')
        return data


def pgm16(data):
    index=0;tokens=[]
    while len(tokens)<4:
        while index<len(data)and data[index]in b' \t\r\n':index+=1
        if index<len(data)and data[index]==35:
            while index<len(data)and data[index]!=10:index+=1
            continue
        start=index
        while index<len(data)and data[index]not in b' \t\r\n':index+=1
        if index==start:raise ValueError('Incomplete PGM header')
        tokens.append(data[start:index])
    if tokens[0]!=b'P5'or tokens[3]!=b'65535':raise ValueError('Expected P5 16-bit PGM')
    width,height=int(tokens[1]),int(tokens[2])
    if not 4<=width<=64 or not 4<=height<=36:raise ValueError('Unsupported bounded game-frame dimensions')
    if index>=len(data)or data[index]not in b' \t\r\n':raise ValueError('Missing binary delimiter')
    index+=2 if data[index:index+2]==b'\r\n'else 1
    payload=data[index:]
    if len(payload)!=width*height*2:raise ValueError('Truncated/trailing PGM pixels')
    return np.frombuffer(payload,dtype='>u2').reshape(height,width).astype(np.uint16)


def load_frame(metadata_path,depth_path,target=(32,18)):
    metadata_raw=bounded_read(metadata_path,65536);meta=json.loads(metadata_raw)
    data=bounded_read(depth_path,1048576);digest=hashlib.sha256(data).hexdigest()
    if meta.get('schema')!='vragecage.sensor-frame.v1'or meta.get('kind')!='depth':raise ValueError('Unknown game depth contract')
    if meta.get('encoding')!='pgm-p5-16bit-big-endian'or meta.get('miss_value')!=0:raise ValueError('Unsupported game depth encoding')
    if meta.get('depth_sha256')!=digest:raise ValueError('Depth bytes differ from metadata hash')
    pixels=pgm16(data);height,width=pixels.shape
    if (meta.get('width'),meta.get('height'))!=(width,height):raise ValueError('Frame shape differs from metadata')
    sensor_range=float(meta['range_meters']);fov=float(meta['horizontal_fov_degrees'])
    if not math.isfinite(sensor_range)or not 1<=sensor_range<=250 or not math.isfinite(fov)or not 10<=fov<=120:raise ValueError('Invalid depth range/FOV')
    if int(np.count_nonzero(pixels))!=meta.get('hit_pixels'):raise ValueError('Hit count differs from metadata')
    if meta.get('self_grid_hits',0)!=0:raise ValueError('Self-grid pixels need an explicit per-pixel identity mask; refuse to treat them as clear')
    target_width,target_height=target
    if target_width<=0 or target_height<=0 or width%target_width or height%target_height or width*target_height!=height*target_width:
        raise ValueError('Full-FOV downsampling must preserve aspect and use integral bins')
    radial=pixels.astype(np.float32)/65535*sensor_range
    # Zero in this validated format means a ray miss within sensor range, not
    # unavailable camera data. A missing role is handled separately below.
    radial[pixels==0]=sensor_range
    radial=np.minimum(radial,16.)
    block=radial.reshape(target_height,height//target_height,target_width,width//target_width)
    radial=block.min(axis=(1,3))
    if not re.fullmatch('[a-f0-9]{32}',str(meta.get('operation_id',''))):raise ValueError('Invalid exact operation ID')
    receipt=dict(camera_id=canonical_id(meta['camera_id']),grid_id=canonical_id(meta['grid_id']),operation_id=meta['operation_id'],
                 source_depth_sha256=digest,source_metadata_sha256=hashlib.sha256(metadata_raw).hexdigest(),
                 source_shape=[width,height],target_shape=list(target),sensor_range_meters=sensor_range,
                 horizontal_fov_degrees=fov,downsample='minimum radial distance per full-FOV bin',
                 acquisition_time='unknown legacy capture',controller_frame='not supplied',
                 structure_and_hash_verified=True,usable_for_model_inference=False)
    return radial/16,np.ones_like(radial),receipt


def assemble(pairs,target=(32,18)):
    width,height=target;image=np.zeros((3,height*2+4,width*2),np.float32);receipts={}
    if any(role not in ROLES for role in pairs):raise ValueError('Unknown camera role')
    for role,(metadata,depth)in pairs.items():
        d,m,receipt=load_frame(metadata,depth,target)
        row,col=divmod(ROLES.index(role),2)
        image[0,row*height:(row+1)*height,col*width:(col+1)*width]=d
        image[1,row*height:(row+1)*height,col*width:(col+1)*width]=m
        receipts[role]=receipt
    grids={r['grid_id']for r in receipts.values()}
    if len(grids)>1:raise ValueError('Do not combine frames from different vehicle grids')
    return image,dict(schema='voidwright.recorded-depth-inspection.v1',frames=receipts,
        synchronized=False,conditioning_available=False,usable_for_model_inference=False,
        scope='Retained offline format inspection only; no fresh/common run or tick, controller pose, vehicle binding or ground-truth label supplied')


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--frame',nargs=3,action='append',metavar=('ROLE','METADATA','PGM'),required=True)
    parser.add_argument('--out',type=Path,required=True);args=parser.parse_args()
    if len({row[0]for row in args.frame})!=len(args.frame):raise ValueError('Duplicate camera role')
    image,receipt=assemble({role:(Path(meta),Path(depth))for role,meta,depth in args.frame})
    args.out.mkdir(mode=0o700,parents=True,exist_ok=False)
    np.savez_compressed(args.out/'historical-mosaic.npz',images=image)
    (args.out/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps(receipt,indent=2))


if __name__=='__main__':main()
