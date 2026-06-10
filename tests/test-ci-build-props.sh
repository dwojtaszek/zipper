#!/bin/bash
set -e

echo "Testing Deterministic property..."
DETERMINISTIC=$(dotnet msbuild ../src/Zipper.csproj -getProperty:Deterministic)
if [[ "$DETERMINISTIC" != "true" ]]; then
    echo "FAIL: Deterministic is not true. Got '$DETERMINISTIC'"
    exit 1
fi

echo "Testing ContinuousIntegrationBuild property (CI=true)..."
CI_BUILD=$(CI=true dotnet msbuild ../src/Zipper.csproj -getProperty:ContinuousIntegrationBuild)
if [[ "$CI_BUILD" != "true" ]]; then
    echo "FAIL: ContinuousIntegrationBuild is not true when CI=true. Got '$CI_BUILD'"
    exit 1
fi

echo "Testing ContinuousIntegrationBuild property (GITHUB_ACTIONS=true)..."
GH_BUILD=$(GITHUB_ACTIONS=true dotnet msbuild ../src/Zipper.csproj -getProperty:ContinuousIntegrationBuild)
if [[ "$GH_BUILD" != "true" ]]; then
    echo "FAIL: ContinuousIntegrationBuild is not true when GITHUB_ACTIONS=true. Got '$GH_BUILD'"
    exit 1
fi

echo "Testing ContinuousIntegrationBuild property (no CI)..."
NO_CI_BUILD=$(CI= GITHUB_ACTIONS= dotnet msbuild ../src/Zipper.csproj -getProperty:ContinuousIntegrationBuild)
if [[ "$NO_CI_BUILD" == "true" ]]; then
    echo "FAIL: ContinuousIntegrationBuild is true when not in CI."
    exit 1
fi

echo "PASS: Build properties are correct."
