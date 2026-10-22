import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
from recorded import pgm16,load_frame,assemble


class RecordedChecks(unittest.TestCase):
    def fixture(self,root,grid=106313676705820376):
        pixels=np.full((36,64),32768,dtype='>u2');pixels[0,0]=0
        data=b'P5\n64 36\n65535\n'+pixels.tobytes();pgm=root/'depth.pgm';pgm.write_bytes(data)
        meta=dict(schema='vragecage.sensor-frame.v1',kind='depth',encoding='pgm-p5-16bit-big-endian',miss_value=0,
            depth_sha256=hashlib.sha256(data).hexdigest(),width=64,height=36,range_meters=100,
            horizontal_fov_degrees=90,hit_pixels=2303,camera_id=108893405607549393,grid_id=grid,
            operation_id='a'*32,self_grid_hits=0)
        path=root/'meta.json';path.write_text(json.dumps(meta));return path,pgm

    def test_game_zero_miss_differs_from_unavailable_camera(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);meta,pgm=self.fixture(root)
            image,receipt=assemble({'front':(meta,pgm)})
            self.assertEqual(image.shape,(3,40,64));self.assertTrue((image[1,:18,:32]==1).all())
            self.assertTrue((image[1,:18,32:]==0).all())
            self.assertEqual(receipt['frames']['front']['grid_id'],'106313676705820376')
            self.assertFalse(receipt['usable_for_model_inference']);self.assertFalse(receipt['synchronized'])

    def test_hash_shape_hit_count_and_self_mask_fail_closed(self):
        for field,value in [('depth_sha256','0'*64),('width',32),('hit_pixels',0),('self_grid_hits',1)]:
            with self.subTest(field=field),tempfile.TemporaryDirectory()as directory:
                root=Path(directory);meta,pgm=self.fixture(root);v=json.loads(meta.read_text());v[field]=value;meta.write_text(json.dumps(v))
                with self.assertRaises(ValueError):load_frame(meta,pgm)

    def test_binary_whitespace_is_not_eaten_as_header(self):
        values=np.full((4,4),0x0a20,dtype='>u2')
        parsed=pgm16(b'P5\n# comment\n4 4\n65535\n'+values.tobytes())
        np.testing.assert_array_equal(parsed,values)

    def test_aspect_and_truncation_rejected(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);meta,pgm=self.fixture(root)
            with self.assertRaises(ValueError):load_frame(meta,pgm,target=(32,32))
            with self.assertRaises(ValueError):pgm16(pgm.read_bytes()[:-1])

    def test_nearest_obstacle_survives_conservative_pooling(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);meta,pgm=self.fixture(root)
            pixels=np.full((36,64),65535,dtype='>u2');pixels[1,1]=655
            data=b'P5\n64 36\n65535\n'+pixels.tobytes();pgm.write_bytes(data)
            v=json.loads(meta.read_text());v['depth_sha256']=hashlib.sha256(data).hexdigest();v['hit_pixels']=2304;meta.write_text(json.dumps(v))
            depth,mask,_=load_frame(meta,pgm)
            self.assertLess(depth[0,0]*16,1.01);self.assertTrue((mask==1).all())


if __name__=='__main__':unittest.main()
