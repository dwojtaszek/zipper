import json
import re
import shutil
import subprocess
import sys

gh = shutil.which('gh')
if gh is None:
    sys.exit('gh CLI not found on PATH')

res = subprocess.run([gh, 'api', 'graphql', '-f', 'query=query { repository(owner: "dwojtaszek", name: "zipper") { pullRequest(number: 512) { reviewThreads(first: 100) { nodes { id isResolved comments(first: 5) { nodes { body path line } } } } } } }'], capture_output=True, text=True)
if res.returncode != 0:
    sys.exit(f'gh api failed (exit {res.returncode}): {res.stderr.strip()}')

try:
    data = json.loads(res.stdout)
    threads = data['data']['repository']['pullRequest']['reviewThreads']['nodes']
except (json.JSONDecodeError, KeyError) as exc:
    sys.exit(f'Unexpected gh output: {exc}\n{res.stdout[:200]}')

for t in threads:
    if not t['isResolved']:
        print(f"\n=== THREAD ID: {t['id']} ===")
        for c in t['comments']['nodes']:
            print(f"File: {c.get('path')} Line: {c.get('line')}")
            # print only the prompt for AI agents block if it exists
            body = c.get('body', '')
            match = re.search(r'Prompt for AI Agents[^`]*```([^`]*)```', body)
            if match:
                print(match.group(1).strip())
            else:
                # if no ai prompt, print the whole thing
                print(body.strip()[:500] + '...')
