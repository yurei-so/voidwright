import unittest
import numpy as np
from synthetic import sample_scene,encode,camera_rays
try:
    import torch
except ImportError:
    torch=None
if torch is not None:
    from projected import project_points,ProjectedDepth


@unittest.skipIf(torch is None,'Torch not installed on this workstation; run with pinned mpai Python')
class ProjectionChecks(unittest.TestCase):
    def test_projection_matches_numpy_camera_geometry(self):
        scene=sample_scene(np.random.default_rng(53),'flat',loadout='random')
        for c in scene.cameras:c['valid']=True
        image,condition=encode(scene,[32,18]);images=torch.from_numpy(image)[None];conditions=torch.from_numpy(condition)[None]
        points,available,hit,depth=project_points(images,conditions,[32,18])
        for index,camera in enumerate(scene.cameras):
            expected=np.asarray(camera['position'])+camera_rays(camera,[32,18])*depth[0,index].numpy()[...,None]*16
            np.testing.assert_allclose(points[0,index].numpy(),expected,atol=8e-6)
        self.assertTrue(available.all())

    def test_missing_or_no_hit_has_no_permission(self):
        scene=sample_scene(np.random.default_rng(3),'flat');image,condition=encode(scene,[32,18])
        model=ProjectedDepth([32,18])
        image[:2]=0
        with torch.inference_mode():score=model(torch.from_numpy(image)[None],torch.from_numpy(condition)[None])
        self.assertGreater(score.item(),15)
        image[0]=1;image[1]=1
        with torch.inference_mode():score=model(torch.from_numpy(image)[None],torch.from_numpy(condition)[None])
        self.assertGreater(score.item(),15)

    def test_real_pixels_produce_finite_nonzero_gradients(self):
        scene=sample_scene(np.random.default_rng(7),'flat');scene.heading=0
        image,condition=encode(scene,[32,18]);model=ProjectedDepth([32,18])
        output=model(torch.from_numpy(image)[None],torch.from_numpy(condition)[None]);self.assertTrue(torch.isfinite(output).all())
        output.sum().backward()
        self.assertTrue(any(p.grad is not None and (p.grad!=0).any()for p in model.parameters()))
        self.assertTrue(all(p.grad is None or torch.isfinite(p.grad).all()for p in model.parameters()))


if __name__=='__main__':unittest.main()
