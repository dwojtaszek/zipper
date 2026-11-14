#!/usr/bin/env python3
"""Watch one PR-head workflow; retain local state and print only changes."""
import argparse
import json
import re
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def github(deadline, *arguments):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError
    try:
        result = subprocess.run(['gh', *arguments], capture_output=True, text=True,
                                check=True, timeout=min(60, remaining))
    except subprocess.TimeoutExpired:
        if time.monotonic() >= deadline:
            raise TimeoutError from None
        raise
    if time.monotonic() >= deadline:
        raise TimeoutError
    return json.loads(result.stdout)


def publish_snapshot(snapshot, state, previous, quiet=False):
    snapshot.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode='w', dir=snapshot.parent, delete=False) as output:
        json.dump({**state, 'checked_at': time.time()}, output)
        temporary = Path(output.name)
    temporary.replace(snapshot)
    old_jobs = {job['id']: job for job in previous.get('jobs', [])}
    for job in state['jobs']:
        if old_jobs.get(job['id']) != job and (not quiet or job['status'] == 'completed'):
            print(json.dumps({'run': state['run_id'], 'job': job['name'],
                              'status': job['status'], 'conclusion': job['conclusion'],
                              'step': job['step']}), flush=True)
    if previous.get('status') != state['status']:
        print(json.dumps({key: state[key] for key in ('head', 'run_id', 'status', 'conclusion')}), flush=True)
    return state.copy()


def find_run(gh, repo, head, pr):
    runs = gh('api', f'repos/{repo}/actions/workflows/pr.yml/runs?head_sha={head}&event=pull_request&per_page=100')['workflow_runs']
    return next((run['id'] for run in runs if run['head_sha'] == head
                 and any(item['number'] == pr for item in run['pull_requests'])), None)


def summarize_job(job):
    return {'id': job['databaseId'], 'name': job['name'], 'status': job['status'],
            'conclusion': job['conclusion'] or '',
            'step': next((step['name'] for step in (job.get('steps') or [])
                          if step['status'] == 'in_progress'), None)}


def poll_pr(gh, repo, pr, state):
    if state['run_id'] is None:
        state['run_id'] = find_run(gh, repo, state['head'], pr)
    if gh('pr', 'view', str(pr), '--json', 'headRefOid')['headRefOid'] != state['head']:
        state['status'] = 'superseded'
        state['conclusion'] = ''
        return 3
    if state['run_id'] is None:
        state['status'] = 'waiting-for-run'
    else:
        run = gh('run', 'view', str(state['run_id']), '--json', 'status,conclusion,jobs')
        state.update(status=run['status'], conclusion=run['conclusion'] or '',
                     jobs=[summarize_job(job) for job in (run.get('jobs') or [])])
    if state['status'] == 'completed':
        head = gh('pr', 'view', str(pr), '--json', 'headRefOid')['headRefOid']
        if head != state['head']:
            state['status'] = 'superseded'
            state['conclusion'] = ''
            return 3
        return 0 if state['conclusion'] == 'success' else 1
    return None


def read_snapshot(snapshot: Path, pr: int) -> int:
    try:
        data = json.loads(snapshot.read_text(encoding='utf-8'))
    except (OSError, json.JSONDecodeError) as error:
        print(f'Cannot read snapshot {snapshot}: {error}', file=sys.stderr)
        return 3
    if not isinstance(data, dict):
        print(f'Invalid snapshot {snapshot}: expected JSON object', file=sys.stderr)
        return 3
    if data.get('pr') != pr:
        print(f'Snapshot {snapshot} is for PR #{data.get("pr")}, not PR #{pr}', file=sys.stderr)
        return 3
    checked_at = data.get('checked_at')
    if isinstance(checked_at, (int, float)):
        age_s = max(0, int(time.time() - checked_at))
        print(f"# offline snapshot age={age_s}s head={data.get('head', 'unknown')}", file=sys.stderr)
    print(json.dumps(data, indent=2))
    status = data.get('status')
    if status == 'completed':
        return 0 if data.get('conclusion') == 'success' else 1
    if status in ('superseded', 'unavailable'):
        return 3
    if status == 'timeout':
        return 124
    return 4


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pr', type=int)
    parser.add_argument('--snapshot', type=Path, required=True)
    parser.add_argument('--interval', type=int, default=30)
    parser.add_argument('--timeout', type=int, default=3600)
    parser.add_argument('--status', action='store_true', help='Print existing snapshot state and exit without polling')
    parser.add_argument('--quiet', action='store_true', help='Print only completed jobs and workflow state transitions')
    args = parser.parse_args()
    if args.pr < 1 or args.interval < 1 or args.timeout < 1:
        parser.error('PR, interval, and timeout must be positive')
    if args.status:
        return read_snapshot(args.snapshot, args.pr)
    state = {'pr': args.pr, 'head': None, 'run_id': None, 'status': 'starting', 'conclusion': '', 'jobs': []}
    previous = {}
    deadline = time.monotonic() + args.timeout

    def gh(*arguments):
        return github(deadline, *arguments)

    def publish():
        nonlocal previous
        previous = publish_snapshot(args.snapshot, state, previous, quiet=args.quiet)

    try:
        state['head'] = gh('pr', 'view', str(args.pr), '--json', 'headRefOid')['headRefOid']
        if not isinstance(state['head'], str) or not re.fullmatch(r'[0-9a-f]{40}', state['head']):
            raise ValueError('PR head was not a readable commit SHA')
        repo = gh('repo', 'view', '--json', 'nameWithOwner')['nameWithOwner']
        while time.monotonic() < deadline:
            result = poll_pr(gh, repo, args.pr, state)
            if result is not None:
                publish()
                return result
            publish()
            time.sleep(min(args.interval, max(0, deadline - time.monotonic())))
        state['status'] = 'timeout'
        publish()
        return 124
    except TimeoutError:
        state['status'] = 'timeout'
        publish()
        return 124
    except (subprocess.SubprocessError, OSError, ValueError, KeyError) as error:
        state['status'] = 'unavailable'
        publish()
        print(str(error)[:1000], file=sys.stderr)
        return 3


if __name__ == '__main__':
    raise SystemExit(main())
