#!/bin/bash

# Test script to validate the unified build-and-test.yml workflow
# This script validates that the new unified workflow functions correctly

set -e

echo "=== Unified Workflow Validation Test (Unix) ==="
echo "Testing new build-and-test.yml workflow"

# The shared job graph lives in a reusable workflow; both callers (PR Checks
# and Build and Test) invoke it, so assertions must target the right file.
# Assertions about the job graph below are assertions about ONE shared
# definition instead of two copies that can drift (see ci.yml's header).
SHARED_WORKFLOW_FILE=".github/workflows/ci.yml"
CALLER_WORKFLOW_FILE=".github/workflows/build-and-test.yml"
PR_WORKFLOW_FILE=".github/workflows/pr.yml"

# Check if the reusable workflow exists
echo "1. Checking workflow files exist..."
if [ -f "$SHARED_WORKFLOW_FILE" ]; then
    echo "✓ ci.yml exists"
else
    echo "✗ ci.yml missing"
    exit 1
fi

if [ -f "$CALLER_WORKFLOW_FILE" ]; then
    echo "✓ build-and-test.yml exists"
else
    echo "✗ build-and-test.yml missing"
    exit 1
fi

if [ -f "$PR_WORKFLOW_FILE" ]; then
    echo "✓ pr.yml exists"
else
    echo "✗ pr.yml missing"
    exit 1
fi

# Check if old workflows still exist (they should until cleanup phase)
echo "2. Checking old workflow files..."
if [ -f ".github/workflows/build.yml" ]; then
    echo "✓ build.yml still exists (expected before cleanup)"
else
    echo "⚠ build.yml already removed (cleanup may have been done)"
fi

if [ -f ".github/workflows/test.yml" ]; then
    echo "✓ test.yml still exists (expected before cleanup)"
else
    echo "⚠ test.yml already removed (cleanup may have been done)"
fi

# Validate unified workflow structure
echo "3. Validating unified workflow structure..."
if grep -q "name: Build and Test" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Unified workflow has correct name"
else
    echo "✗ Unified workflow name incorrect"
    exit 1
fi

# Check for all required jobs. The shared graph (lint, runner-tests,
# version-metadata, build-and-test) lives in ci.yml; the caller keeps prepare and tag-and-release.
echo "4. Checking for all required jobs..."
for job in "prepare:" "tag-and-release:"; do
    if grep -q "$job" "$CALLER_WORKFLOW_FILE"; then
        echo "✓ Found $job job in build-and-test.yml"
    else
        echo "✗ Missing $job job in build-and-test.yml"
        exit 1
    fi
done

for job in "lint:" "runner-tests:" "version-metadata:" "build-and-test:"; do
    if grep -q "$job" "$SHARED_WORKFLOW_FILE"; then
        echo "✓ Found $job job in ci.yml"
    else
        echo "✗ Missing $job job in ci.yml"
        exit 1
    fi
done

if grep -q "run: bash tests/test-build-version.sh" "$SHARED_WORKFLOW_FILE"; then
    echo "✓ version-metadata job runs tests/test-build-version.sh"
else
    echo "✗ version-metadata job does not run tests/test-build-version.sh"
    exit 1
fi

# The reusable hook and both call sites are what keep the two callers in sync.
echo "5. Checking reusable workflow wiring..."
if grep -q "workflow_call:" "$SHARED_WORKFLOW_FILE"; then
    echo "✓ ci.yml is a reusable workflow (workflow_call)"
else
    echo "✗ ci.yml must be triggered by workflow_call"
    exit 1
fi

for caller in "$CALLER_WORKFLOW_FILE" "$PR_WORKFLOW_FILE"; do
    if grep -q "uses: ./.github/workflows/ci.yml" "$caller"; then
        echo "✓ $caller calls ci.yml"
    else
        echo "✗ $caller must call ./.github/workflows/ci.yml"
        exit 1
    fi
done

# Check job dependencies
echo "6. Checking job dependencies..."
if grep -q "needs: lint" "$SHARED_WORKFLOW_FILE"; then
    echo "✓ Build-and-test job depends on lint"
else
    echo "✗ Build-and-test job missing correct dependencies"
    exit 1
fi

if grep -q "needs: \[prepare, ci\]" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Release job depends on all previous jobs"
else
    echo "✗ Release job missing dependencies"
    exit 1
fi

# Check for matrix strategy in build-and-test job
echo "7. Checking build-and-test job matrix strategy..."
if grep -A 10 "build-and-test:" "$SHARED_WORKFLOW_FILE" | grep -q "matrix:"; then
    echo "✓ Build-and-test job has matrix strategy"
else
    echo "✗ Build-and-test job missing matrix strategy"
    exit 1
fi

# Check platforms in matrix
echo "8. Checking platform support..."
PLATFORMS=("win-x64" "linux-x64" "osx-arm64")

for platform in "${PLATFORMS[@]}"; do
    if grep -q "$platform" "$SHARED_WORKFLOW_FILE"; then
        echo "✓ Found $platform in workflow"
    else
        echo "✗ Missing $platform in workflow"
        exit 1
    fi
done

# Check for artifact handling
echo "9. Checking artifact handling..."
if grep -q "actions/upload-artifact@" "$SHARED_WORKFLOW_FILE"; then
    echo "✓ Uses upload-artifact"
else
    echo "✗ Missing upload-artifact"
    exit 1
fi

if grep -q "actions/download-artifact@" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Uses download-artifact"
else
    echo "✗ Missing download-artifact"
    exit 1
fi

# Check for artifact retention
echo "10. Checking artifact retention configuration..."
if grep -q "retention-days: 30" "$SHARED_WORKFLOW_FILE"; then
    echo "✓ Accepted release artifacts retained for 30 days"
else
    echo "✗ Missing artifact retention configuration"
    exit 1
fi

# Check for branch triggers
echo "11. Checking branch triggers..."
if grep -q "branches:" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Has branch triggers configured"
else
    echo "✗ Missing branch triggers"
    exit 1
fi

if grep -q "main" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Main branch trigger found"
else
    echo "✗ Missing main branch trigger"
    exit 1
fi

# Check for release conditions
echo "12. Checking release conditions..."
if grep -q "if: needs.prepare.outputs.skip != 'true'" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Release job requires an incomplete reserved intent"
else
    echo "✗ Release job missing reserved intent condition"
    exit 1
fi

# Check for permissions
echo "13. Checking release permissions..."
if grep -q "permissions:" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Release job has permissions"
else
    echo "✗ Release job missing permissions"
    exit 1
fi

if grep -q "contents: write" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Release job has contents write permission"
else
    echo "✗ Release job missing contents write permission"
    exit 1
fi

# Check for caching. The reportgenerator cache moved into the shared composite
# action (.github/actions/coverage-gate); match a tag or SHA-pinned
# `actions/cache@` ref anywhere under .github/.
echo "14. Checking caching configuration..."
if grep -rq "actions/cache@" .github/; then
    echo "✓ Uses caching"
else
    echo "✗ Missing caching"
    exit 1
fi

# Check for version handling
echo "15. Checking version handling..."
if grep -q "python3 .controller/.github/scripts/release.py prepare" "$CALLER_WORKFLOW_FILE"; then
    echo "✓ Version handling uses the durable release controller"
else
    echo "✗ Missing version handling"
    exit 1
fi

# Recovery dispatches check out the historical source SHA; the controller
# itself must come from the triggering ref (the fixed pipeline), so both
# release jobs need the second checkout at .controller.
if [ "$(grep -c "path: .controller" "$CALLER_WORKFLOW_FILE")" -eq 2 ]; then
    echo "✓ Release controller runs from the triggering ref in both release jobs"
else
    echo "✗ Release jobs must check out the controller from the triggering ref"
    exit 1
fi

echo ""
echo "=== All Unified Workflow Validation Tests Passed ==="
echo "New build-and-test.yml workflow is properly configured and ready"
