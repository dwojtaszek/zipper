#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$SCRIPT_DIR/../src/Zipper.csproj"

echo "Testing Deterministic property..."
DETERMINISTIC=$(dotnet msbuild "$PROJECT_PATH" -getProperty:Deterministic)
if [[ "$DETERMINISTIC" != "true" ]]; then
    echo "FAIL: Deterministic is not true. Got '$DETERMINISTIC'"
    exit 1
fi

echo "Testing ContinuousIntegrationBuild property (CI=true)..."
CI_BUILD=$(CI=true dotnet msbuild "$PROJECT_PATH" -getProperty:ContinuousIntegrationBuild)
if [[ "$CI_BUILD" != "true" ]]; then
    echo "FAIL: ContinuousIntegrationBuild is not true when CI=true. Got '$CI_BUILD'"
    exit 1
fi

echo "Testing ContinuousIntegrationBuild property (GITHUB_ACTIONS=true)..."
GH_BUILD=$(GITHUB_ACTIONS=true dotnet msbuild "$PROJECT_PATH" -getProperty:ContinuousIntegrationBuild)
if [[ "$GH_BUILD" != "true" ]]; then
    echo "FAIL: ContinuousIntegrationBuild is not true when GITHUB_ACTIONS=true. Got '$GH_BUILD'"
    exit 1
fi

echo "Testing ContinuousIntegrationBuild property (no CI)..."
NO_CI_BUILD=$(CI= GITHUB_ACTIONS= dotnet msbuild "$PROJECT_PATH" -getProperty:ContinuousIntegrationBuild)
if [[ "$NO_CI_BUILD" == "true" ]]; then
    echo "FAIL: ContinuousIntegrationBuild is true when not in CI."
    exit 1
fi

echo "Testing docs-only regex classification..."
DOCS_REGEX='(^|/)(docs/|CHANGELOG(\.md)?$|.*\.(md|markdown)$)'

is_docs() {
    echo "$1" | grep -q -E "$DOCS_REGEX"
}

# Documentation files MUST match:
for file in "README.md" "AGENTS.md" "Requirements.md" "UBIQUITOUS_LANGUAGE.md" "docs/cicd.md" "docs/architecture.md" "docs/notes.txt" "src/folder/README.md" "CHANGELOG.md"; do
    if ! is_docs "$file"; then
        echo "FAIL: '$file' should be classified as documentation"
        exit 1
    fi
done

# Non-documentation files MUST NOT match:
for file in "BannedSymbols.txt" "tests/fixtures/chaos-anomaly-types.txt" "tests/fixtures/profiles/expected-headers/full.txt" "src/Program.cs" "zipper.sln"; do
    if is_docs "$file"; then
        echo "FAIL: '$file' should NOT be classified as documentation"
        exit 1
    fi
done

echo "PASS: Build properties and docs-only classification are correct."
