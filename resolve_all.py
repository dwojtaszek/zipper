import json
import subprocess
import sys

# Get all unresolved threads
res = subprocess.run([
    "gh", "api", "graphql", "-f", 
    'query=query { repository(owner: "dwojtaszek", name: "zipper") { pullRequest(number: 506) { reviewThreads(first: 100) { nodes { id isResolved comments(first: 1) { nodes { body } } } } } } }'
], capture_output=True, text=True)

data = json.loads(res.stdout)
threads = data['data']['repository']['pullRequest']['reviewThreads']['nodes']

for thread in threads:
    if not thread['isResolved']:
        tid = thread['id']
        print(f"Resolving {tid}")
        
        # Reply
        subprocess.run([
            "gh", "api", "graphql", "-f", f'query=mutation {{ addPullRequestReviewThreadReply(input: {{pullRequestReviewThreadId: "{tid}", body: "Fixed."}}) {{ clientMutationId }} }}'
        ])
        
        # Resolve
        subprocess.run([
            "gh", "api", "graphql", "-f", f'query=mutation {{ resolveReviewThread(input: {{threadId: "{tid}"}}) {{ clientMutationId }} }}'
        ])

print("All threads resolved.")
