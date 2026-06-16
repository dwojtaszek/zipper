import json
import subprocess
import re

res = subprocess.run(['gh', 'api', 'graphql', '-f', 'query=query { repository(owner: "dwojtaszek", name: "zipper") { pullRequest(number: 512) { reviewThreads(first: 100) { nodes { id isResolved comments(first: 5) { nodes { body path line } } } } } } }'], capture_output=True, text=True)
data = json.loads(res.stdout)
threads = data['data']['repository']['pullRequest']['reviewThreads']['nodes']
for t in threads:
    if not t['isResolved']:
        print(f"\n=== THREAD ID: {t['id']} ===")
        for c in t['comments']['nodes']:
            print(f"File: {c.get('path')} Line: {c.get('line')}")
            # print only the prompt for AI agents block if it exists
            body = c.get('body', '')
            match = re.search(r'Prompt for AI Agents.*?```(.*?)```', body, re.DOTALL)
            if match:
                print(match.group(1).strip())
            else:
                # if no ai prompt, print the whole thing
                print(body.strip()[:500] + '...')
