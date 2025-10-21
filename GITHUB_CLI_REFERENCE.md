# GitHub CLI Commands Reference

This document contains detailed GitHub CLI commands for PR management and repository operations.

## PR Management Commands

### Get PR Comments
```bash
# Get PR comments with file locations and line numbers
gh api repos/:owner/:repo/pulls/45/comments --jq '.[] | {path, line, body}'
```
Replace `:owner/:repo` with actual repository (e.g., `dwojtaszek/zipper`) and `45` with the PR number.

### Get PR Reviews and Comments
```bash
# Get all review comments for a specific PR
gh pr view 13 --json comments,reviews --jq '.reviews[].body'
```

### Get Inline Review Comments
```bash
# Get inline comments with file context
gh api repos/dwojtaszek/zipper/pulls/13/comments --jq '.[] | {path, line, body}'
```

### Review Thread Management
```bash
# Quick check review threads status
owner="dwojtaszek" repo="zipper" pr=123
gh api graphql -f query='query($owner:String!,$repo:String!,$pr:Int!){repository(owner:$owner,name:$repo){pullRequest(number:$pr){reviewThreads(first:100){nodes{id isResolved}}}}}' -F owner="$owner" -F repo="$repo" -F pr="$pr" --jq '.data.repository.pullRequest.reviewThreads.nodes[]'

# Resolve all review threads in a PR
owner="dwojtaszek"
repo="zipper"
pr=123
gh api graphql -f query='query($owner:String!,$repo:String!,$pr:Int!){repository(owner:$owner,name:$repo){pullRequest(number:$pr){reviewThreads(first:100){nodes{id}}}}}' -F owner="$owner" -F repo="$repo" -F pr="$pr" --jq '.data.repository.pullRequest.reviewThreads.nodes[].id' | xargs -I {} gh api graphql -f query='mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{id isResolved}}}' -F id={}

# Simple one-liner for current repo (dwojtaszek/zipper)
pr=123 && gh api graphql -f query='query($pr:Int!){repository(owner:"dwojtaszek",name:"zipper"){pullRequest(number:$pr){reviewThreads(first:100){nodes{id}}}}}' -F pr=$pr --jq '.data.repository.pullRequest.reviewThreads.nodes[].id' | xargs -I {} gh api graphql -f query='mutation($id:ID!){resolveReviewThread(input:{threadId:$id}){thread{id isResolved}}}' -F id={}
```

**Note**: All review thread commands use the GraphQL API since review threads are only available via GraphQL. Replace example values with your actual repository owner, repo name, and PR number.