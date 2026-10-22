import unittest
import numpy as np
from synthetic import sample_scene,encode
from advisory import prepare,advise,CameraLossLatch


class AdvisoryChecks(unittest.TestCase):
    def input(self):
        scene=sample_scene(np.random.default_rng(19),'flat');scene.heading=0
        scene.cameras[0].update(position=[0,1.5,0],yaw=0,pitch=-.4,roll=0,fov=90,valid=True)
        return encode(scene,[32,18])

    def test_numeric_contract_and_geometry_veto(self):
        image,context=self.input();prepared=prepare(image,context)
        prepared['geometry']=1
        result=advise(prepared,0,.5)
        self.assertFalse(result['candidate']);self.assertFalse(result['actuation_authorized'])

    def test_unavailable_pixels_cannot_become_free_space(self):
        image,context=self.input();image[1]=0;image[0,:-4]=np.nan
        prepared=prepare(image,context)
        self.assertTrue(np.isfinite(prepared['images']).all());self.assertFalse(any(prepared['available']))
        self.assertFalse(advise(prepared,0,1)['candidate'])

    def test_invalid_available_pixels_fail_before_model(self):
        image,context=self.input();image[0,2,2]=np.nan
        with self.assertRaises(ValueError):prepare(image,context)
        image,context=self.input();context[6:8]=0
        with self.assertRaises(ValueError):prepare(image,context)

    def test_stale_depth_under_unavailable_role_is_erased(self):
        image,context=self.input();context[18]=0;image[0,:18,:32]=np.nan
        prepared=prepare(image,context)
        self.assertFalse(prepared['available'][0]);self.assertEqual(prepared['images'][0].sum(),0)

    def test_no_hit_cannot_create_support_or_permission(self):
        image,context=self.input();image[0,:18,:32]=1
        prepared=prepare(image,context)
        self.assertEqual(prepared['geometry'],.5);self.assertFalse(advise(prepared,0,1)['candidate'])

    def test_ood_camera_is_not_silently_trusted(self):
        image,context=self.input();context[10]=.5
        with self.assertRaises(ValueError):prepare(image,context)

    def test_loss_requires_replan_even_if_feed_returns(self):
        latch=CameraLossLatch();self.assertTrue(latch.update([1,1,0,0]))
        self.assertFalse(latch.update([1,0,0,0]));self.assertFalse(latch.update([1,1,0,0]))
        latch.reset_for_new_controller_plan();self.assertTrue(latch.update([1,0,0,0]))

    def test_partial_corruption_invalidates_whole_camera(self):
        image,context=self.input();image[1,2,2]=0
        prepared=prepare(image,context)
        self.assertFalse(prepared['available'][0]);self.assertEqual(prepared['images'][1].sum(),0)

    def test_footer_is_not_an_extra_camera_or_numeric_direction(self):
        image,context=self.input();image[0,-1,1]=1
        with self.assertRaises(ValueError):prepare(image,context)

    def test_front_heading_domain_does_not_accept_rear_queries(self):
        image,context=self.input();context[6:8]=[0,-1]
        with self.assertRaises(ValueError):prepare(image,context)
        prepare(image,context,allowed_roles=(0,1,2,3))


if __name__=='__main__':unittest.main()
