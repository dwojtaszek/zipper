#!/bin/bash

# Test script to validate artifact handling in current workflows
# This script checks artifact creation, naming, and retention patterns

set -e

echo "=== Artifact Handling Test (Unix) ==="
echo "Testing artifact creation and handling patterns"

# Use the unified build-and-test.yml workflow
WORKFLOW_FILE=".github/workflows/build-and-test.yml"

# Check if workflow exists
if [ ! -f "$WORKFLOW_FILE" ]; then
    echo "✗ Workflow file not found: $WORKFLOW_FILE"
    exit 1
fi

# Check build-and-test.yml for artifact patterns
echo "1. Analyzing $WORKFLOW_FILE artifact patterns..."

# Check for artifact upload steps
if grep -q "actions/upload-artifact@v" "$WORKFLOW_FILE"; then
    echo "✓ Uses actions/upload-artifact"
else
    echo "✗ Missing actions/upload-artifact"
    exit 1
fi

# Check for artifact names
ARTIFACT_NAMES=$(grep -A 2 "name:" "$WORKFLOW_FILE" | grep -E "name:|zipper-" | wc -l)
if [ "$ARTIFACT_NAMES" -ge 3 ]; then
    echo "✓ Found expected artifact names"
else
    echo "✗ Insufficient artifact names found"
    exit 1
fi

# Check for platform-specific artifacts
echo "2. Checking platform-specific artifact configuration..."
PLATFORMS=("win-x64" "linux-x64" "osx-arm64")
EXPECTED_ARTIFACTS=("zipper-win-x64" "zipper-linux-x64" "zipper-osx-arm64")

for platform in "${PLATFORMS[@]}"; do
    if grep -q "$platform" "$WORKFLOW_FILE"; then
        echo "✓ Found $platform configuration"
    else
        echo "✗ Missing $platform configuration"
        exit 1
    fi
done

for artifact in "${EXPECTED_ARTIFACTS[@]}"; do
    if grep -q "$artifact" "$WORKFLOW_FILE"; then
        echo "✓ Found $artifact naming"
    else
        echo "✗ Missing $artifact naming"
        exit 1
    fi
done

# Check for caching strategy
echo "3. Analyzing caching strategy..."
if grep -q "actions/cache@v" "$WORKFLOW_FILE"; then
    echo "✓ Uses actions/cache"
else
    echo "✗ Missing actions/cache"
    exit 1
fi

# Check cache keys
CACHE_KEYS=$(grep -A 3 "key:" "$WORKFLOW_FILE" | grep -E "key:|build-" | wc -l)
if [ "$CACHE_KEYS" -ge 2 ]; then
    echo "✓ Found platform-specific cache keys"
else
    echo "✗ Insufficient cache keys found (expected at least 2, got $CACHE_KEYS)"
    exit 1
fi

# Check cache paths
echo "4. Checking cache paths..."

# Check for publish directory pattern with template variable
if grep -q "publish/\${{ matrix.platform }}" "$WORKFLOW_FILE"; then
    echo "✓ Found publish/ directory structure with platform variable"
else
    echo "✗ Missing publish/ directory structure"
    exit 1
fi

# Also verify publish/ is used
if grep -q "publish/" "$WORKFLOW_FILE"; then
    echo "✓ Uses publish/ directory"
else
    echo "✗ Missing publish/ directory"
    exit 1
fi

# Check for artifact download in release job
echo "5. Checking release job artifact handling..."
if grep -q "actions/download-artifact@v" "$WORKFLOW_FILE"; then
    echo "✓ Uses actions/download-artifact"
else
    echo "✗ Missing actions/download-artifact"
    exit 1
fi

# Check release file patterns
echo "6. Checking release file patterns..."
RELEASE_FILES=("artifacts/zipper-win-x64/zipper-win-x64.exe" "artifacts/zipper-linux-x64/zipper-linux-x64" "artifacts/zipper-osx-arm64/zipper-osx-arm64")

for file in "${RELEASE_FILES[@]}"; do
    if grep -q "$file" "$WORKFLOW_FILE"; then
        echo "✓ Found release file pattern: $file"
    else
        echo "✗ Missing release file pattern: $file"
        exit 1
    fi
done

# Check for artifact cleanup
echo "7. Checking for PDB file cleanup..."
if grep -q "rm.*\.pdb" "$WORKFLOW_FILE"; then
    echo "✓ Removes PDB files from artifacts"
else
    echo "✗ Missing PDB file cleanup"
    exit 1
fi

# Check for executable renaming
echo "8. Checking executable renaming patterns..."
# The workflow uses case statement with platform-specific renaming
if grep -q "mv publish/\${{ matrix.platform }}/Zipper.exe" "$WORKFLOW_FILE"; then
    echo "✓ Found Windows executable renaming pattern"
else
    echo "✗ Missing Windows executable renaming pattern"
    exit 1
fi

if grep -q "mv publish/\${{ matrix.platform }}/Zipper" "$WORKFLOW_FILE"; then
    echo "✓ Found Unix executable renaming pattern"
else
    echo "✗ Missing Unix executable renaming pattern"
    exit 1
fi

# Check for conditional builds based on cache
echo "9. Checking conditional build logic..."
if grep -q "cache-hit != 'true'" "$WORKFLOW_FILE"; then
    echo "✓ Uses conditional builds based on cache"
else
    echo "✗ Missing conditional build logic"
    exit 1
fi

echo ""
echo "=== All Artifact Handling Tests Passed ==="
echo "Current artifact patterns are compatible with unified workflow design"
