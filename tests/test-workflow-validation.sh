#!/bin/bash

# Test script to validate current build and test workflows
# This script validates that the existing workflows function correctly

set -e

echo "=== Workflow Validation Test (Unix) ==="
echo "Testing current build.yml and test.yml workflows"

# Check if workflow files exist
echo "1. Checking workflow file existence..."
if [ -f ".github/workflows/build.yml" ]; then
    echo "✓ build.yml exists"
else
    echo "✗ build.yml missing"
    exit 1
fi

if [ -f ".github/workflows/test.yml" ]; then
    echo "✓ test.yml exists"
else
    echo "✗ test.yml missing"
    exit 1
fi

# Check if .editorconfig exists
echo "2. Checking .editorconfig..."
if [ -f ".editorconfig" ]; then
    echo "✓ .editorconfig exists"
else
    echo "✗ .editorconfig missing"
    exit 1
fi

# Check if test scripts exist
echo "3. Checking test scripts..."
if [ -f "tests/run-tests.sh" ]; then
    echo "✓ run-tests.sh exists"
    if [ -x "tests/run-tests.sh" ]; then
        echo "✓ run-tests.sh is executable"
    else
        echo "✗ run-tests.sh is not executable"
        exit 1
    fi
else
    echo "✗ run-tests.sh missing"
    exit 1
fi

# Check .version file
echo "4. Checking .version file..."
if [ -f ".version" ]; then
    echo "✓ .version exists"
    VERSION_CONTENT=$(cat .version)
    echo "  Current version: $VERSION_CONTENT"
else
    echo "✗ .version missing"
    exit 1
fi

# Check project structure
echo "5. Checking project structure..."
if [ -d "Zipper" ]; then
    echo "✓ Zipper directory exists"
    if [ -f "Zipper/Zipper.csproj" ]; then
        echo "✓ Zipper.csproj exists"
    else
        echo "✗ Zipper.csproj missing"
        exit 1
    fi
else
    echo "✗ Zipper directory missing"
    exit 1
fi

# Validate build.yml structure
echo "6. Validating build.yml structure..."
if grep -q "name: Build and Release" .github/workflows/build.yml; then
    echo "✓ build.yml has correct name"
else
    echo "✗ build.yml name incorrect"
    exit 1
fi

if grep -q "on:" .github/workflows/build.yml; then
    echo "✓ build.yml has triggers"
else
    echo "✗ build.yml missing triggers"
    exit 1
fi

if grep -q "jobs:" .github/workflows/build.yml; then
    echo "✓ build.yml has jobs section"
else
    echo "✗ build.yml missing jobs section"
    exit 1
fi

# Validate test.yml structure
echo "7. Validating test.yml structure..."
if grep -q "name: Run Tests" .github/workflows/test.yml; then
    echo "✓ test.yml has correct name"
else
    echo "✗ test.yml name incorrect"
    exit 1
fi

if grep -q "matrix:" .github/workflows/test.yml; then
    echo "✓ test.yml has matrix strategy"
else
    echo "✗ test.yml missing matrix strategy"
    exit 1
fi

# Check for required actions in build.yml
echo "8. Validating required actions in build.yml..."
REQUIRED_ACTIONS=("actions/checkout@v3" "actions/setup-dotnet@v3" "actions/cache@v3" "actions/upload-artifact@v4" "softprops/action-gh-release@v2")

for action in "${REQUIRED_ACTIONS[@]}"; do
    if grep -q "$action" .github/workflows/build.yml; then
        echo "✓ Found $action"
    else
        echo "✗ Missing $action"
        exit 1
    fi
done

# Check for required actions in test.yml
echo "9. Validating required actions in test.yml..."
TEST_ACTIONS=("actions/checkout@v3" "actions/setup-dotnet@v3")

for action in "${TEST_ACTIONS[@]}"; do
    if grep -q "$action" .github/workflows/test.yml; then
        echo "✓ Found $action"
    else
        echo "✗ Missing $action"
        exit 1
    fi
done

echo ""
echo "=== All Workflow Validation Tests Passed ==="
echo "Current workflows are properly structured and ready for unification"