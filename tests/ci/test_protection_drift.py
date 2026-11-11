"""Exercise the protection checker through its command-line boundary."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / '.github/scripts/protection-drift.py'


class ProtectionDriftTests(unittest.TestCase):
    def test_check_sets_must_match_documentation(self):
        for contexts, checks, expected in [
            (['ci-gate'], [], 0),
            ([], [{'context': 'ci-gate', 'app_id': 1}], 0),
            ([], [], 1),
            (['ci-gate', 'CodeQL'], [], 1),
            (['other'], [], 1),
        ]:
            with self.subTest(contexts=contexts), tempfile.TemporaryDirectory() as directory:
                protection = Path(directory) / 'protection.json'
                protection.write_text(json.dumps({'required_status_checks': {
                    'contexts': contexts, 'checks': checks}}))
                rules = Path(directory) / 'rules.json'
                rules.write_text('[]')
                result = subprocess.run([sys.executable, str(SCRIPT), str(ROOT / 'docs/cicd.md'),
                                         str(protection), str(rules)], capture_output=True, text=True)
                self.assertEqual(expected, result.returncode, result.stderr + result.stdout)

    def test_rulesets_and_unreadable_evidence_fail_closed(self):
        for rules, protection, declaration, expected in [
            ([{'type': 'required_status_checks', 'parameters': {
                'required_status_checks': [{'context': 'ci-gate'}]}}],
             {'required_status_checks': {'contexts': [], 'checks': []}},
             'Required status checks for `main`: `ci-gate`.', 0),
            ([{'type': 'required_status_checks', 'parameters': {
                'required_status_checks': [{'context': 'CodeQL'}]}}],
             {'required_status_checks': {'contexts': ['ci-gate'], 'checks': []}},
             'Required status checks for `main`: `ci-gate`.', 1),
            ([], {}, 'Required status checks for `main`: `ci-gate`.', 1),
            ([], {'required_status_checks': None},
             'Required status checks for `main`: `ci-gate`.', 1),
            ([], {}, 'No declaration', 1),
            ([], {}, 'Required status checks for `main`: none.', 1),
        ]:
            with self.subTest(rules=rules, protection=protection), tempfile.TemporaryDirectory() as directory:
                paths = [Path(directory) / name for name in ('docs.md', 'protection.json', 'rules.json')]
                for path, content in zip(paths, [declaration, json.dumps(protection), json.dumps(rules)]):
                    path.write_text(content)
                result = subprocess.run([sys.executable, str(SCRIPT), *map(str, paths)],
                                        capture_output=True, text=True)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn('::error::', result.stderr)


if __name__ == '__main__':
    unittest.main()
