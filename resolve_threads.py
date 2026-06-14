import json
import subprocess
import sys

data = {
  "nodes": [
    {
      "id": "PRRT_kwDOPR1iDM6JY_jP",
      "isResolved": False,
      "path": "src/ZipSizeVerifier.cs"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_jR",
      "isResolved": False,
      "path": "tests/test-zip64-boundary.sh"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_jS",
      "isResolved": False,
      "path": "tests/test-zip64-boundary.sh"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_jT",
      "isResolved": False,
      "path": "src/Zipper.Tests/ZipSizeVerifierTests.cs"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_jU",
      "isResolved": False,
      "path": "tests/test-zip64-boundary.sh"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_jV",
      "isResolved": False,
      "path": "tests/test-zip64-boundary.sh"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_jY",
      "isResolved": False,
      "path": "tests/test-zip64-boundary.sh"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_ja",
      "isResolved": False,
      "path": "src/Zipper.Tests/ColumnProfileLoaderTests.cs"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JY_jb",
      "isResolved": False,
      "path": "src/Zipper.Tests/Profiles/Generation/DateGeneratorTests.cs"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JZBAa",
      "isResolved": False,
      "path": ".github/workflows/zip64-nightly.yml"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JZBAc",
      "isResolved": False,
      "path": "src/ZipSizeVerifier.cs"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JZBAd",
      "isResolved": False,
      "path": "tests/test-ci-build-props.sh"
    },
    {
      "id": "PRRT_kwDOPR1iDM6JZBAe",
      "isResolved": False,
      "path": "tests/test-zip64-boundary.sh"
    }
  ]
}

for thread in data["nodes"]:
    tid = thread["id"]
    path = thread["path"]
    
    if path == "tests/test-ci-build-props.sh":
        body = "Skipping: The script uses cd $(dirname $0) at the top, so the relative path is robust to the invocation location."
    elif path == "src/Zipper.Tests/ColumnProfileLoaderTests.cs" or path == "src/Zipper.Tests/Profiles/Generation/DateGeneratorTests.cs":
        body = "We are removing the method-scoped pragmas to rely on the file-scoped suppression instead. Tests run sequentially so global mutation is acceptable for now."
    else:
        body = "Fixed in the latest commit."
        
    print(f"Resolving {tid} for {path}")
    
    subprocess.run([
        "gh", "api", "graphql", "-f", f"query=mutation {{ addPullRequestReviewThreadReply(input: {{pullRequestReviewThreadId: \"{tid}\", body: \"{body}\"}}) {{ clientMutationId }} }}"
    ])
    
    subprocess.run([
        "gh", "api", "graphql", "-f", f"query=mutation {{ resolveReviewThread(input: {{threadId: \"{tid}\"}}) {{ clientMutationId }} }}"
    ])

print("All threads resolved.")
