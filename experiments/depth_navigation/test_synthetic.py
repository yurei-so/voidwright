import copy
import math
import unittest
import numpy as np
from synthetic import sample_scene, camera_rays, render, encode, oracle, geometric_baseline, dataset


class SyntheticChecks(unittest.TestCase):
    def scene(self, family="flat"):
        scene = sample_scene(np.random.default_rng(3), family)
        scene.heading = 0
        scene.lateral = 0
        scene.distance = 2
        scene.width = 6
        scene.length = 1
        scene.height = 2
        scene.vehicle_height = 2
        scene.slope = 0
        scene.geometry_yaw = 0
        return scene

    def test_flat_clear_and_obstacle_oracles(self):
        self.assertEqual(oracle(self.scene()), 0)
        for family in ("step", "trench", "boulder", "wall"):
            self.assertEqual(oracle(self.scene(family)), 1, family)

    def test_vehicle_clearance_changes_same_overhang_label(self):
        scene = self.scene("overhang");scene.ceiling = 1.6
        scene.vehicle_height = 1.2;self.assertEqual(oracle(scene), 0)
        scene.vehicle_height = 2.2;self.assertEqual(oracle(scene), 1)

    def test_climbable_curb_is_not_a_continuous_steep_slope(self):
        scene=self.scene("step");scene.height=.15;scene.step_limit=.3
        self.assertEqual(oracle(scene),0)
        scene.height=.7;self.assertEqual(oracle(scene),1)
        scene=self.scene("trench");scene.height=.15;scene.step_limit=.3
        self.assertEqual(oracle(scene),0)
        scene.height=.7;self.assertEqual(oracle(scene),1)

    def test_body_length_extends_the_collision_envelope(self):
        scene=self.scene("wall");scene.distance=6;scene.length=.5
        scene.vehicle_length=2;self.assertEqual(oracle(scene),0)
        scene.vehicle_length=5;self.assertEqual(oracle(scene),1)

    def test_steep_slope_and_travel_direction(self):
        scene=self.scene("slope");scene.slope=.8
        self.assertEqual(oracle(scene), 1)
        scene.heading=math.pi/2;self.assertEqual(oracle(scene), 1)  # cross-vehicle roll/clearance
        scene.slope=.04;self.assertEqual(oracle(scene), 0)

    def test_full_fov_ray_transform(self):
        camera=self.scene().cameras[0]
        camera.update(yaw=0,pitch=0,roll=0,fov=90)
        rays=camera_rays(camera, 32)
        np.testing.assert_allclose(np.linalg.norm(rays,axis=-1),1,atol=1e-6)
        self.assertLess(rays[16,0,0],-.65);self.assertGreater(rays[16,-1,0],.65)
        camera["yaw"]=math.pi
        rear=camera_rays(camera,32)
        self.assertLess(rear[16,16,2],-.99)

    def test_missing_camera_is_distinct_from_max_range(self):
        scene=self.scene();camera=scene.cameras[0]
        camera.update(position=[0,3,0],pitch=.5,valid=True)
        depth,valid=render(scene,camera,16)
        self.assertTrue(((depth==16)&(valid==1)).any())
        camera["valid"]=False
        depth,valid=render(scene,camera,16)
        self.assertEqual(float(depth.sum()+valid.sum()),0)

    def test_mosaic_keeps_full_frames_and_external_strip(self):
        scene=self.scene();image,context=encode(scene,16,"strip")
        d,m=render(scene,scene.cameras[0],16)
        self.assertEqual(image.shape,(3,36,32));self.assertEqual(context.shape,(52,))
        np.testing.assert_allclose(image[0,:16,:16],d/16)
        self.assertEqual(float(image[2,:32].sum()),0)
        self.assertEqual(float(image[2,32:].sum()),4)
        self.assertEqual(float(image[1,32:].sum()),0)

    def test_rectangular_full_fov_preserves_camera_aspect(self):
        scene=self.scene();camera=scene.cameras[0];camera.update(yaw=0,pitch=0,roll=0,fov=90)
        rays=camera_rays(camera,[32,18])
        self.assertEqual(rays.shape,(18,32,3))
        self.assertAlmostEqual(float(rays[0,16,1]/rays[0,16,2]),17/32,places=6)
        image,_=encode(scene,[32,18]);self.assertEqual(image.shape,(3,40,64))
        d,m=render(scene,camera,[32,18]);np.testing.assert_allclose(image[0,:18,:32],d/16)

    def test_loss_changes_masks_without_changing_oracle(self):
        scene=self.scene();before=oracle(scene)
        image,context=encode(scene,16,camera_loss=0)
        self.assertEqual(oracle(scene),before)
        self.assertEqual(float(image[1].sum()),0)
        self.assertEqual(context[18],0)
        self.assertEqual(geometric_baseline(scene,image,16),.5)

    def test_seed_reproducibility_and_split_independence(self):
        a=dataset(8,10,resolution=8);b=dataset(8,10,resolution=8);c=dataset(8,11,resolution=8)
        np.testing.assert_array_equal(a["images"],b["images"])
        self.assertNotEqual(a["records"],c["records"])
        self.assertTrue(np.isfinite(a["images"]).all())

    def test_unseen_rear_obstacle_cannot_be_inferred_from_front_frame(self):
        clear=self.scene();clear.heading=math.pi;clear.reverse=True
        blocked=copy.deepcopy(clear);blocked.family="wall";blocked.geometry_yaw=math.pi
        first,context=encode(clear,16);second,other_context=encode(blocked,16)
        np.testing.assert_array_equal(first,second)
        np.testing.assert_array_equal(context,other_context)
        self.assertEqual(oracle(clear),0);self.assertEqual(oracle(blocked),1)
        self.assertEqual(geometric_baseline(clear,first,16),.5)
        self.assertEqual(geometric_baseline(blocked,second,16),.5)

    def test_supported_start_sampler_rejects_initial_overlap(self):
        rows=dataset(24,73,resolution=8,supported_start=True)
        from synthetic import Scene
        for record in rows['records']:self.assertEqual(oracle(Scene(**record),distance=0),0)

    def test_max_range_without_support_is_unknown(self):
        scene=self.scene();image,context=encode(scene,16)
        image[0,:16,:16]=1
        self.assertEqual(geometric_baseline(scene,image,16),.5)


if __name__=="__main__":unittest.main()
