#!/bin/bash

# Test script to validate the new unified build-and-test.yml workflow
# This script validates that the new unified workflow functions correctly

set -e

echo "=== Unified Workflow Validation Test (Unix) ==="
echo "Testing new build-and-test.yml workflow"

# Check if unified workflow exists
echo "1. Checking unified workflow file existence..."
if [ -f ".github/workflows/build-and-test.yml" ]; then
    echo "✓ build-and-test.yml exists"
else
    echo "✗ build-and-test.yml missing"
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
if grep -q "name: Build and Test" .github/workflows/build-and-test.yml; then
    echo "✓ Unified workflow has correct name"
else
    echo "✗ Unified workflow name incorrect"
    exit 1
fi

# Check for all required jobs
echo "4. Checking for all required jobs..."
REQUIRED_JOBS=("lint:" "build:" "test:" "release:")

for job in "${REQUIRED_JOBS[@]}"; do
    if grep -q "$job" .github/workflows/build-and-test.yml; then
        echo "✓ Found $job job"
    else
        echo "✗ Missing $job job"
        exit 1
    fi
done

# Check job dependencies
echo "5. Checking job dependencies..."
if grep -q "needs: \[prepare, lint\]" .github/workflows/build-and-test.yml; then
    echo "✓ Build job depends on prepare and lint"
else
    echo "✗ Build job missing correct dependencies"
    exit 1
fi

if grep -q "needs: build" .github/workflows/build-and-test.yml; then
    echo "✓ Test job depends on build"
else
    echo "✗ Test job missing build dependency"
    exit 1
fi

if grep -q "needs: \[prepare, lint, build, test\]" .github/workflows/build-and-test.yml; then
    echo "✓ Release job depends on all previous jobs"
else
    echo "✗ Release job missing dependencies"
    exit 1
fi

# Check for matrix strategy in build job
echo "6. Checking build job matrix strategy..."
if grep -A 10 "build:" .github/workflows/build-and-test.yml | grep -q "matrix:"; then
    echo "✓ Build job has matrix strategy"
else
    echo "✗ Build job missing matrix strategy"
    exit 1
fi

# Check for matrix strategy in test job
echo "7. Checking test job matrix strategy..."
if grep -A 10 "test:" .github/workflows/build-and-test.yml | grep -q "matrix:"; then
    echo "✓ Test job has matrix strategy"
else
    echo "✗ Test job missing matrix strategy"
    exit 1
fi

# Check platforms in matrix
echo "8. Checking platform support..."
PLATFORMS=("win-x64" "linux-x64" "osx-arm64")

for platform in "${PLATFORMS[@]}"; do
    if grep -q "$platform" .github/workflows/build-and-test.yml; then
        echo "✓ Found $platform in workflow"
    else
        echo "✗ Missing $platform in workflow"
        exit 1
    fi
done

# Check for artifact handling
echo "9. Checking artifact handling..."
if grep -q "actions/upload-artifact@v" .github/workflows/build-and-test.yml; then
    echo "✓ Uses upload-artifact"
else
    echo "✗ Missing upload-artifact"
    exit 1
fi

if grep -q "actions/download-artifact@v" .github/workflows/build-and-test.yml; then
    echo "✓ Uses download-artifact"
else
    echo "✗ Missing download-artifact"
    exit 1
fi

# Check for artifact retention
echo "10. Checking artifact retention configuration..."
if grep -q "retention-days: 7" .github/workflows/build-and-test.yml; then
    echo "✓ Artifact retention set to 7 days"
else
    echo "✗ Missing artifact retention configuration"
    exit 1
fi

# Check for .editorconfig validation
echo "11. Checking .editorconfig validation in lint job..."
if grep -q "hashFiles('.editorconfig')" .github/workflows/build-and-test.yml; then
    echo "✓ Lint job checks for .editorconfig"
else
    echo "✗ Lint job missing .editorconfig check"
    exit 1
fi

# Check for branch triggers
echo "12. Checking branch triggers..."
if grep -q "branches:" .github/workflows/build-and-test.yml; then
    echo "✓ Has branch triggers configured"
else
    echo "✗ Missing branch triggers"
    exit 1
fi

if grep -q "master" .github/workflows/build-and-test.yml; then
    echo "✓ Master branch trigger found"
else
    echo "✗ Missing master branch trigger"
    exit 1
fi

# Check for release conditions
echo "13. Checking release conditions..."
if grep -q "if: github.ref == 'refs/heads/master'" .github/workflows/build-and-test.yml; then
    echo "✓ Release job only runs on master branch"
else
    echo "✗ Release job missing master branch condition"
    exit 1
fi

# Check for permissions
echo "14. Checking release permissions..."
if grep -q "permissions:" .github/workflows/build-and-test.yml; then
    echo "✓ Release job has permissions"
else
    echo "✗ Release job missing permissions"
    exit 1
fi

if grep -q "contents: write" .github/workflows/build-and-test.yml; then
    echo "✓ Release job has contents write permission"
else
    echo "✗ Release job missing contents write permission"
    exit 1
fi

# Check for caching
echo "15. Checking caching configuration..."
if grep -q "actions/cache@v" .github/workflows/build-and-test.yml; then
    echo "✓ Uses caching"
else
    echo "✗ Missing caching"
    exit 1
fi

# Check for version handling
echo "16. Checking version handling..."
if grep -q "Set Version" .github/workflows/build-and-test.yml; then
    echo "✓ Has version handling"
else
    echo "✗ Missing version handling"
    exit 1
fi

if grep -q "\.version" .github/workflows/build-and-test.yml; then
    echo "✓ Reads .version file"
else
    echo "✗ Missing .version file reading"
    exit 1
fi

echo ""
echo "=== All Unified Workflow Validation Tests Passed ==="
echo "New build-and-test.yml workflow is properly configured and ready"