import unittest,json,types
from unittest.mock import patch
import run_fixed


class RunnerAdmissionChecks(unittest.TestCase):
    def states(self,run='a'*36,lease='a'*36,definition='voidwright.depth-navigation.test'):
        return [json.dumps({'runs':[{'id':run,'definition_id':definition,'status':'running'}]}),
            json.dumps({'requests':[{'state':'running','runtime_id':'agent-runtime','workload':{'class':'experiment','run_id':lease}}]})]

    def attempt(self,states,parent=45,observed=45,definition='voidwright.depth-navigation.test'):
        with patch.object(run_fixed.subprocess,'run',return_value=types.SimpleNamespace(stdout=str(parent))),patch.object(run_fixed.os,'getppid',return_value=observed),patch.object(run_fixed.Path,'is_symlink',return_value=False),patch.object(run_fixed.Path,'stat',return_value=types.SimpleNamespace(st_size=100)),patch.object(run_fixed.Path,'read_text',side_effect=states):
            return run_fixed.verify_lease('a'*36,definition)

    def test_only_matching_runtime_child_run_and_lease_are_admitted(self):
        self.assertEqual(self.attempt(self.states()),45)

    def test_borrowed_other_run_lease_is_rejected(self):
        with self.assertRaises(PermissionError):self.attempt(self.states(lease='b'*36))

    def test_direct_shell_invocation_is_rejected(self):
        with self.assertRaises(PermissionError):self.attempt(self.states(),observed=46)

    def test_wrong_definition_or_nonrunning_record_is_rejected(self):
        with self.assertRaises(PermissionError):self.attempt(self.states(definition='unrelated'))
        values=self.states();values[0]=values[0].replace('running','completed')
        with self.assertRaises(PermissionError):self.attempt(values)


if __name__=='__main__':unittest.main()
