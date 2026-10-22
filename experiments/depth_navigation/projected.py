"""Tiny shared point network using deterministic depth/camera reprojection.

Only declared image/condition tensors enter here. No Scene, oracle, world file,
entity lookup, tool or other privileged geometry is read.
"""
import math
import torch
from torch import nn
from synthetic import frame_dimensions


def project_points(images,conditions,resolution):
    width,height=frame_dimensions(resolution);device=images.device;dtype=images.dtype
    depth=torch.stack([images[:,0,(i//2)*height:(i//2+1)*height,(i%2)*width:(i%2+1)*width]for i in range(4)],dim=1)
    available=torch.stack([images[:,1,(i//2)*height:(i//2+1)*height,(i%2)*width:(i%2+1)*width]for i in range(4)],dim=1)>.5
    cameras=conditions[:,8:].reshape(-1,4,11)
    available=available&(cameras[:,:,10,None,None]>.5)
    sy,cy,sp,cp,sr,cr=(cameras[:,:,i]for i in [3,4,5,6,7,8])
    forward=torch.stack((sy*cp,sp,cy*cp),dim=-1)
    right=torch.stack((cy,torch.zeros_like(cy),-sy),dim=-1)
    up=torch.cross(forward,right,dim=-1)
    rolled_right=right*cr[:,:,None]+up*sr[:,:,None]
    rolled_up=up*cr[:,:,None]-right*sr[:,:,None]
    x=(torch.arange(width,device=device,dtype=dtype)+.5)/width*2-1
    y=1-(torch.arange(height,device=device,dtype=dtype)+.5)/height*2
    yy,xx=torch.meshgrid(y,x,indexing='ij')
    tangent=torch.tan(cameras[:,:,9]*130*math.pi/360)
    rx=xx[None,None]*tangent[:,:,None,None]
    ry=yy[None,None]*tangent[:,:,None,None]*height/width
    rays=forward[:,:,None,None,:]+rolled_right[:,:,None,None,:]*rx[...,None]+rolled_up[:,:,None,None,:]*ry[...,None]
    rays=rays/torch.linalg.vector_norm(rays,dim=-1,keepdim=True).clamp_min(1e-8)
    points=cameras[:,:,:3,None,None].permute(0,1,3,4,2)*4+rays*depth[...,None]*16
    hit=available&(depth>0)&(depth<.9999)
    return points,available,hit,depth


class ProjectedDepth(nn.Module):
    def __init__(self,resolution):
        super().__init__();self.resolution=resolution
        self.shared=nn.Sequential(nn.Conv2d(12,32,1),nn.ReLU(),nn.Conv2d(32,16,1),nn.ReLU())
        self.head=nn.Sequential(nn.Linear(41,32),nn.ReLU(),nn.Linear(32,1))

    def forward(self,images,conditions):
        points,available,hit,depth=project_points(images,conditions,self.resolution)
        width,height=frame_dimensions(self.resolution)
        dx=points[:,:,1:-1,2:]-points[:,:,1:-1,:-2]
        dy=points[:,:,2:,1:-1]-points[:,:,:-2,1:-1]
        raw=torch.cross(dx,dy,dim=-1);length=torch.linalg.vector_norm(raw,dim=-1,keepdim=True)
        reliable=(length[...,0]>1e-6)&hit[:,:,1:-1,1:-1]&hit[:,:,1:-1,2:]&hit[:,:,1:-1,:-2]&hit[:,:,2:,1:-1]&hit[:,:,:-2,1:-1]
        reliable=reliable&(torch.linalg.vector_norm(dx,dim=-1)<2)&(torch.linalg.vector_norm(dy,dim=-1)<2)
        normals=torch.zeros_like(points);normals[:,:,1:-1,1:-1]=raw/length.clamp_min(1e-6)*reliable[...,None]
        normal_valid=torch.zeros_like(depth);normal_valid[:,:,1:-1,1:-1]=reliable.to(depth.dtype)
        jump=torch.zeros_like(depth);jump[:,:,1:-1,1:-1]=torch.maximum(abs(dx[...,1]),abs(dy[...,1]))/2
        vehicle_width=conditions[:,0,None,None,None]*4.3
        vehicle_height=conditions[:,1,None,None,None]*4
        vehicle_length=conditions[:,2,None,None,None]*6
        step=conditions[:,3,None,None,None].clamp_min(.01)
        sine=conditions[:,6,None,None,None];cosine=conditions[:,7,None,None,None]
        along=points[...,0]*sine+points[...,2]*cosine
        across=points[...,0]*cosine-points[...,2]*sine
        corridor=hit&(abs(across)<=vehicle_width/2)&(along>=-vehicle_length/2)&(along<=4+vehicle_length/2)
        nx,ny,nz=normals.unbind(-1);grade=torch.sqrt(nx*nx+nz*nz)/abs(ny).clamp_min(.05)
        features=torch.stack((along/(4+vehicle_length),across/(vehicle_width/2),points[...,1]/vehicle_height,
            depth,abs(ny),grade.clamp(0,5)/5,nx,nz,normal_valid,(jump/step).clamp(0,5)/5,available.to(depth.dtype),hit.to(depth.dtype)),dim=2)
        features=features.permute(0,2,1,3,4).reshape(-1,12,4*height,width)
        mask=corridor.reshape(-1,1,4*height,width);encoded=self.shared(features)
        total=mask.sum(dim=(2,3)).clamp_min(1)
        mean=(encoded*mask).sum(dim=(2,3))/total
        maximum=encoded.masked_fill(~mask,-1e4).flatten(2).max(dim=2).values
        exists=mask.flatten(1).any(dim=1)
        maximum=torch.where(exists[:,None],maximum,torch.zeros_like(maximum))
        density=mask.to(depth.dtype).mean(dim=(2,3))
        score=self.head(torch.cat((mean,maximum,conditions[:,:8],density),dim=1)).flatten()
        # Absence of observed points in a query is unknown, not permission.
        return torch.where(exists,score,torch.ones_like(score)*20)
