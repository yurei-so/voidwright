import unittest
import numpy as np
from metrics import metrics, calibrate, wilson


class MetricChecks(unittest.TestCase):
    def test_denominators_and_abstention(self):
        result=metrics([1,1,0,0],[.1,.9,.2,.8],.5,[True,True,True,False])
        self.assertEqual(result['false_safe'],1);self.assertEqual(result['false_safe_rate'],.5)
        self.assertEqual(result['coverage'],.5);self.assertEqual(result['clear_recall'],.5)
        result=metrics([1,1,0,0],[0,0,0,0],1,[False]*4)
        self.assertEqual(result['safe_predictions'],0)

    def test_calibration_uses_same_eligibility_as_test(self):
        labels=np.array([1,1,0,0]);probability=np.array([.01,.8,.5,.6])
        raw=calibrate(labels,probability,maximum_false_safe=0)
        gated=calibrate(labels,probability,maximum_false_safe=0,eligible=[False,True,True,True])
        self.assertLess(raw,.02);self.assertGreater(gated,.6)
        self.assertEqual(metrics(labels,probability,gated,[False,True,True,True])['false_safe'],0)

    def test_zero_observed_errors_are_not_zero_uncertainty(self):
        low,high=wilson(0,100)
        self.assertAlmostEqual(low,0,places=12);self.assertGreater(high,.03)
        self.assertEqual(wilson(0,0),[0,1])

    def test_invalid_probabilities_and_empty_hazards_fail(self):
        with self.assertRaises(ValueError):metrics([0],[np.nan],.5)
        with self.assertRaises(ValueError):calibrate([0,0],[.2,.3])

    def test_permission_error_is_not_hazard_miss_rate(self):
        result=metrics([1]*100+[0]*100,[.1]+[.9]*99+[.9]*100,.5)
        self.assertEqual(result['false_safe_rate'],.01)
        self.assertEqual(result['unsafe_per_permission'],1.)
        threshold=calibrate([1]*100+[0]*100,[.1]+[.9]*99+[.9]*100,maximum_permission_error=.02,minimum_permissions=10)
        self.assertEqual(threshold,0.)
        self.assertIsNone(metrics([1],[1],.5)['unsafe_per_permission'])


if __name__=='__main__':unittest.main()
