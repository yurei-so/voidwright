"""Vectorized CUDA depth renderer; labels remain in the independent CPU oracle."""
import numpy as np
import torch
from synthetic import FAMILIES, camera_rays, frame_dimensions, sample_scene, render


def render_batch(scenes, resolution=32, batch_size=32):
    width,height=frame_dimensions(resolution)
    output=np.zeros((len(scenes),4,2,height,width),np.float32)
    device='cuda'
    distances=torch.tensor(np.arange(.2,16.05,.1,dtype=np.float64),device=device,dtype=torch.float32)
    with torch.inference_mode():
        for camera in range(4):
            active=[i for i,s in enumerate(scenes)if s.cameras[camera]['valid']]
            for start in range(0,len(active),batch_size):
                indices=active[start:start+batch_size];subset=[scenes[i]for i in indices]
                def parameter(name):
                    return torch.tensor([getattr(s,name)for s in subset],device=device,dtype=torch.float32)[:,None,None,None]
                rays=torch.from_numpy(np.asarray([camera_rays(s.cameras[camera],resolution)for s in subset])).to(device)
                origin=torch.tensor([s.cameras[camera]['position']for s in subset],device=device,dtype=torch.float32)
                points=origin[:,None,None,None,:]+rays[:,None,:,:,:]*distances[None,:,None,None,None]
                x,y,z=points.unbind(-1)
                angle=parameter('geometry_yaw');lx=x*torch.cos(angle)-z*torch.sin(angle);lz=x*torch.sin(angle)+z*torch.cos(angle)
                family=torch.tensor([FAMILIES.index(s.family)for s in subset],device=device)[:,None,None,None]
                distance=parameter('distance');size=parameter('height');length=parameter('length');span=parameter('width');lateral=parameter('lateral')
                ground=torch.zeros_like(x)
                ground=torch.where(family==1,parameter('slope')*lz,ground)
                ground=torch.where((family==2)&(lz>distance),size,ground)
                ground=torch.where((family==3)&(lz>distance)&(lz<distance+length),-size,ground)
                ground=torch.where(family==7,.22*torch.sin(lx*1.7+parameter('phase'))*torch.sin(lz*1.4),ground)
                bottom=torch.where(family==6,parameter('ceiling'),torch.zeros_like(size))
                top=bottom+torch.where(family==6,torch.ones_like(size),size)
                box=((family==5)|(family==6))&(abs(lx-lateral)<=span/2)&(abs(lz-distance)<=length/2)&(y>=bottom)&(y<=top)
                boulder=(family==4)&(((lx-lateral)/(span/2))**2+((y-size/2)/(size/2))**2+((lz-distance)/(length/2))**2<=1)
                hit=(y<=ground)|box|boulder
                found=hit.any(dim=1);first=hit.to(torch.int8).argmax(dim=1)
                depth=torch.where(found,distances[first],torch.full_like(x[:,0],16.))
                output[indices,camera,0]=depth.cpu().numpy();output[indices,camera,1]=1
    return output


def self_test(resolution=32):
    rng=np.random.default_rng(1079)
    scenes=[sample_scene(rng,family,shift=True,loadout='random')for family in FAMILIES]
    gpu=render_batch(scenes,resolution)
    maximum=0.
    for i,scene in enumerate(scenes):
        for camera in range(4):
            cpu,mask=render(scene,scene.cameras[camera],resolution)
            np.testing.assert_array_equal(gpu[i,camera,1],mask)
            error=float(abs(cpu-gpu[i,camera,0]).max());maximum=max(maximum,error)
            if error>.1001:raise AssertionError('CPU/GPU ray rendering diverged: '+str(error))
    return dict(passed=True,frames=len(scenes)*4,max_depth_error_meters=maximum,oracle='independent CPU swept geometry')
