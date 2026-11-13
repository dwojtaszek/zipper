"""One head-pinned CI watcher provides local state and emits only changes."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


@unittest.skipIf(os.name == 'nt', 'executable GitHub fixture is POSIX-only')
class CiWatchTests(unittest.TestCase):
    def run_watch(self, directory, failure=False, changed=False, no_run=False, api_failure=False, timeout=10, slow=False, other_pr=False):
        root = Path(directory)
        gh = root / 'gh'
        gh.write_text(
            '#!' + sys.executable + '\n'
            'import json, os, pathlib, sys, time\n'
            'root = pathlib.Path(os.environ["WATCH_FIXTURE"])\n'
            'args = sys.argv[1:]\n'
            'if os.environ.get("SLOW"): time.sleep(3)\n'
            'with (root / "calls").open("a") as log: log.write(json.dumps(args) + "\\n")\n'
            'if args[:2] == ["pr", "view"]:\n'
            '    seen = (root / "head-seen").exists(); (root / "head-seen").touch()\n'
            '    print(json.dumps({"headRefOid": ("b" if seen and os.environ.get("CHANGED") else "a") * 40}))\n'
            'elif args[:2] == ["repo", "view"]:\n'
            '    print(json.dumps({"nameWithOwner": "owner/repo"}))\n'
            'elif args[0] == "api":\n'
            '    print(json.dumps({"workflow_runs": [] if os.environ.get("NO_RUN") else [{"id": 99, "head_sha": "a" * 40, "pull_requests": [{"number": 999 if os.environ.get("OTHER_PR") else 1421}]}]}))\n'
            'elif args[:2] == ["run", "view"]:\n'
            '    if os.environ.get("API_FAILURE"): raise SystemExit(1)\n'
            '    count = root / "polls"; n = int(count.read_text()) if count.exists() else 0\n'
            '    count.write_text(str(n+1))\n'
            '    done = n >= 2 or bool(os.environ.get("FAILURE"))\n'
            '    conclusion = ("failure" if os.environ.get("FAILURE") else "success") if done else ""\n'
            '    print(json.dumps({"status": "completed" if done else "in_progress", "conclusion": conclusion,\n'
            '        "jobs": [{"databaseId": 55, "name": "unit", "status": "completed" if done else "in_progress",\n'
            '                  "conclusion": conclusion, "steps": []}]}))\n'
            'else: raise SystemExit("unexpected gh command")\n')
        gh.chmod(0o755)
        env = {**os.environ, 'PATH': str(root) + os.pathsep + os.environ['PATH'],
               'WATCH_FIXTURE': str(root)}
        if slow:
            env['SLOW'] = '1'
        if other_pr:
            env['OTHER_PR'] = '1'
        if no_run:
            env['NO_RUN'] = '1'
        if api_failure:
            env['API_FAILURE'] = '1'
        if failure:
            env['FAILURE'] = '1'
        if changed:
            env['CHANGED'] = '1'
        return subprocess.run([sys.executable, str(ROOT / '.github/scripts/watch-pr.py'), '1421',
                               '--snapshot', str(root / 'snapshot.json'), '--interval', '1', '--timeout', str(timeout)],
                              cwd=ROOT, env=env, capture_output=True, text=True, timeout=15)

    def test_success_uses_one_watcher_and_suppresses_unchanged_jobs(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            snapshot = json.loads((Path(directory) / 'snapshot.json').read_text())
            self.assertEqual(snapshot['head'], 'a' * 40)
            self.assertEqual(snapshot['run_id'], 99)
            self.assertEqual(snapshot['conclusion'], 'success')
            self.assertEqual(result.stdout.count('"job":'), 2, result.stdout)
            calls = [json.loads(line) for line in (Path(directory) / 'calls').read_text().splitlines()]
            self.assertEqual(sum(call[:2] == ['run', 'view'] for call in calls), 3)
            self.assertFalse(any('checks' in call for call in calls))

    def test_failure_is_retained_in_snapshot_and_exit_status(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory, failure=True)
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            snapshot = json.loads((Path(directory) / 'snapshot.json').read_text())
            self.assertEqual(snapshot['jobs'][0]['conclusion'], 'failure')

    def test_push_during_watch_cannot_report_success_for_current_pr(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory, changed=True)
            self.assertEqual(result.returncode, 3, result.stdout + result.stderr)
            self.assertEqual(json.loads((Path(directory) / 'snapshot.json').read_text())['status'], 'superseded')

    def test_superseded_head_without_a_run_stops_watching(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory, changed=True, no_run=True, timeout=2)
            self.assertEqual(result.returncode, 3, result.stdout + result.stderr)
            self.assertEqual(json.loads((Path(directory) / 'snapshot.json').read_text())['status'], 'superseded')

    def test_api_failure_is_unavailable_not_success(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory, api_failure=True)
            self.assertEqual(result.returncode, 3, result.stdout + result.stderr)
            self.assertEqual(json.loads((Path(directory) / 'snapshot.json').read_text())['status'], 'unavailable')

    def test_missing_run_times_out_with_local_state(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory, no_run=True, timeout=2)
            self.assertEqual(result.returncode, 124, result.stdout + result.stderr)
            self.assertEqual(json.loads((Path(directory) / 'snapshot.json').read_text())['status'], 'timeout')

    def test_hung_github_call_obeys_overall_deadline(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory, slow=True, timeout=1)
            self.assertEqual(result.returncode, 124, result.stdout + result.stderr)
            self.assertEqual(json.loads((Path(directory) / 'snapshot.json').read_text())['status'], 'timeout')

    def test_other_pr_run_for_same_head_cannot_prove_success(self):
        with tempfile.TemporaryDirectory() as directory:
            result = self.run_watch(directory, other_pr=True, timeout=2)
            self.assertEqual(result.returncode, 124, result.stdout + result.stderr)
            self.assertIsNone(json.loads((Path(directory) / 'snapshot.json').read_text())['run_id'])

    def test_status_reads_existing_snapshot_without_calling_gh(self):
        with tempfile.TemporaryDirectory() as directory:
            snap = Path(directory) / 'snapshot.json'
            for status, conclusion, expected_rc in (
                ('completed', 'success', 0),
                ('completed', 'failure', 1),
                ('in_progress', '', 4),
                ('timeout', '', 124),
            ):
                snap.write_text(json.dumps({'pr': 1421, 'status': status, 'conclusion': conclusion}))
                res = subprocess.run(
                    [sys.executable, str(ROOT / '.github/scripts/watch-pr.py'), '1421',
                     '--snapshot', str(snap), '--status'],
                    cwd=ROOT, capture_output=True, text=True, timeout=10,
                )
                self.assertEqual(res.returncode, expected_rc, res.stdout + res.stderr)
            snap.write_text(json.dumps({'pr': 9999, 'status': 'completed', 'conclusion': 'success'}))
            mismatch = subprocess.run(
                [sys.executable, str(ROOT / '.github/scripts/watch-pr.py'), '1421',
                 '--snapshot', str(snap), '--status'],
                cwd=ROOT, capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(mismatch.returncode, 3, mismatch.stdout + mismatch.stderr)
            snap.write_text(json.dumps([]))
            bad_type = subprocess.run(
                [sys.executable, str(ROOT / '.github/scripts/watch-pr.py'), '1421',
                 '--snapshot', str(snap), '--status'],
                cwd=ROOT, capture_output=True, text=True, timeout=10,
            )
            self.assertEqual(bad_type.returncode, 3, bad_type.stdout + bad_type.stderr)
