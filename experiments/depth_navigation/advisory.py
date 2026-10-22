"""Offline tensor validation and controller-owned advisory gates.

No result grants live actuation. The loss latch is a controller utility, never
a tool or an operation callable by the experimental tensor model.
"""
import math
from types import SimpleNamespace
import numpy as np
from synthetic import frame_dimensions,geometric_baseline,YAW


def prepare(images,conditions,resolution=(32,18),check_training_domain=True,allowed_roles=(0,)):
    if tuple(allowed_roles)not in((0,),(0,1,2,3)):raise ValueError('Unknown calibrated camera loadout')
    width,height=frame_dimensions(resolution)
    image=np.asarray(images,dtype=np.float32).copy();context=np.asarray(conditions,dtype=np.float32).copy()
    if image.shape!=(3,height*2+4,width*2)or context.shape!=(52,):raise ValueError('Input shape differs from model contract')
    if not np.isfinite(context[:8]).all():raise ValueError('Nonfinite vehicle/direction conditioning')
    if not np.isclose(np.dot(context[6:8],context[6:8]),1,atol=.001):raise ValueError('Numeric heading must be a unit sine/cosine pair')
    if context[5]not in (0,1):raise ValueError('Reverse state must be explicit')
    if check_training_domain and tuple(allowed_roles)==(0,)and abs(math.atan2(context[6],context[7]))>.85001:
        raise ValueError('Travel heading outside front-model training envelope')
    if not np.isfinite(image[1]).all()or not np.isin(image[1],[0,1]).all():raise ValueError('Validity mask must be explicit binary data')
    if np.any(image[1,-4:])or np.any(image[0,-4:])or np.any(image[2]):raise ValueError('Numeric input requires an empty external marker strip')
    physical=[context[0]*4.3,context[1]*4,context[2]*6,context[3],context[4]]
    if min(physical)<=0:raise ValueError('Positive vehicle dimensions/limits required')
    if check_training_domain:
        for value,(low,high)in zip(physical,[(1.3,3),(1.2,3),(2.5,5.5),(.2,.5),(.25,.5)]):
            if not low-1e-5<=value<=high+1e-5:raise ValueError('Vehicle conditioning outside the calibrated training envelope')
    cameras=[];available=[]
    for index in range(4):
        row,col=divmod(index,2);block=(slice(row*height,(row+1)*height),slice(col*width,(col+1)*width))
        values=context[8+index*11:8+(index+1)*11]
        if values[10]not in (0,1):raise ValueError('Camera availability flag must be binary')
        # Training covers complete or missing cameras, not partial corrupt
        # images. Conservatively invalidate the entire role in that case.
        active=bool(values[10])and bool(image[1][block].all())
        if not active:
            image[0][block]=0;image[1][block]=0
            values[:]=[0,0,0,0,1,0,1,0,1,90/130,0]
        else:
            if index not in allowed_roles:raise ValueError('Camera loadout differs from the calibrated model contract')
            if not np.isfinite(values).all():raise ValueError('Available camera pose is nonfinite')
            for sine,cosine in [(3,4),(5,6),(7,8)]:
                if not np.isclose(values[sine]**2+values[cosine]**2,1,atol=.001):raise ValueError('Camera rotation pair is invalid')
            if check_training_domain and (not -.30001<=values[0]*4<=.30001 or not 1.09999<=values[1]*4<=3.20001 or abs(values[2]*4)>1e-5 or not 64.999<=values[9]*130<=95.001):
                raise ValueError('Camera conditioning outside the calibrated training envelope')
            yaw=math.atan2(values[3],values[4]);pitch=math.atan2(values[5],values[6]);roll=math.atan2(values[7],values[8])
            if check_training_domain and (abs(math.atan2(math.sin(yaw-YAW[index]),math.cos(yaw-YAW[index])))>.15001 or not -.50001<=pitch<=-.11999 or abs(roll)>.25001):
                raise ValueError('Camera orientation outside the calibrated training envelope')
            valid=image[1][block]>.5;depth=image[0][block]
            if not np.isfinite(depth[valid]).all()or (depth[valid]<0).any()or (depth[valid]>1.00001).any():raise ValueError('Available depth pixels are invalid')
            depth[~valid]=0
        available.append(active)
        cameras.append(dict(position=(values[:3]*4).tolist(),yaw=math.atan2(values[3],values[4]),
            pitch=math.atan2(values[5],values[6]),roll=math.atan2(values[7],values[8]),fov=float(values[9]*130),valid=active))
    view=SimpleNamespace(vehicle_width=physical[0],vehicle_height=physical[1],vehicle_length=physical[2],
        step_limit=physical[3],heading=math.atan2(context[6],context[7]),cameras=cameras)
    baseline=geometric_baseline(view,image,resolution)
    return dict(images=image,conditions=context,available=available,geometry=baseline,
        scope='Offline advisory only; freshness/controller binding and live authorization are not established by tensor validation')


def advise(prepared,probability,threshold):
    result=dict(schema='voidwright.depth-advisory.v1',state='unavailable',candidate=False,actuation_authorized=False)
    if not np.isfinite(probability)or not 0<=probability<=1 or not np.isfinite(threshold)or not 0<=threshold<=1:
        return dict(result,reason='Invalid model score/calibration')
    if not any(prepared['available'])or prepared['geometry']==.5:return dict(result,reason='Supporting observation unavailable')
    if threshold<=0:return dict(result,reason='No validation threshold qualified')
    if prepared['geometry']!=0:return dict(result,state='blocked',reason='Deterministic geometry veto')
    if probability>=threshold:return dict(result,state='blocked',reason='Learned score did not qualify')
    return dict(result,state='candidate-for-controller-review',candidate=True,reason='Offline score and geometry agree; live authority remains absent')


class CameraLossLatch:
    def __init__(self):self.previous=None;self.replan_required=False

    def update(self,available):
        if len(available)!=4:raise ValueError('Four camera roles required')
        current=tuple(bool(value)for value in available)
        if self.previous is not None and any(old and not new for old,new in zip(self.previous,current)):
            self.replan_required=True
        self.previous=current
        return not self.replan_required and any(current)

    def reset_for_new_controller_plan(self):
        self.previous=None;self.replan_required=False
