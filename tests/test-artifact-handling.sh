#!/bin/bash

# Test script to validate artifact handling in current workflows
# This script checks artifact creation, naming, and retention patterns

set -e

echo "=== Artifact Handling Test (Unix) ==="
echo "Testing artifact creation and handling patterns"

# Check build.yml for artifact patterns
echo "1. Analyzing build.yml artifact patterns..."

# Check for artifact upload steps
if grep -q "actions/upload-artifact@v4" .github/workflows/build.yml; then
    echo "✓ Uses actions/upload-artifact@v4"
else
    echo "✗ Missing actions/upload-artifact@v4"
    exit 1
fi

# Check for artifact names
ARTIFACT_NAMES=$(grep -A 2 "name:" .github/workflows/build.yml | grep -E "name:|zipper-" | wc -l)
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
    if grep -q "$platform" .github/workflows/build.yml; then
        echo "✓ Found $platform configuration"
    else
        echo "✗ Missing $platform configuration"
        exit 1
    fi
done

for artifact in "${EXPECTED_ARTIFACTS[@]}"; do
    if grep -q "$artifact" .github/workflows/build.yml; then
        echo "✓ Found $artifact naming"
    else
        echo "✗ Missing $artifact naming"
        exit 1
    fi
done

# Check for caching strategy
echo "3. Analyzing caching strategy..."
if grep -q "actions/cache@v3" .github/workflows/build.yml; then
    echo "✓ Uses actions/cache@v3"
else
    echo "✗ Missing actions/cache@v3"
    exit 1
fi

# Check cache keys
CACHE_KEYS=$(grep -A 3 "key:" .github/workflows/build.yml | grep -E "key:|build-" | wc -l)
if [ "$CACHE_KEYS" -ge 3 ]; then
    echo "✓ Found platform-specific cache keys"
else
    echo "✗ Insufficient cache keys found"
    exit 1
fi

# Check cache paths
echo "4. Checking cache paths..."
CACHE_PATHS=("publish/win-x64" "publish/linux-x64" "publish/osx-arm64")

for path in "${CACHE_PATHS[@]}"; do
    if grep -q "$path" .github/workflows/build.yml; then
        echo "✓ Found cache path for $path"
    else
        echo "✗ Missing cache path for $path"
        exit 1
    fi
done

# Check for artifact download in release job
echo "5. Checking release job artifact handling..."
if grep -q "actions/download-artifact@v4" .github/workflows/build.yml; then
    echo "✓ Uses actions/download-artifact@v4"
else
    echo "✗ Missing actions/download-artifact@v4"
    exit 1
fi

# Check release file patterns
echo "6. Checking release file patterns..."
RELEASE_FILES=("artifacts/zipper-win-x64/zipper-win-x64.exe" "artifacts/zipper-linux-x64/zipper-linux-x64" "artifacts/zipper-osx-arm64/zipper-osx-arm64")

for file in "${RELEASE_FILES[@]}"; do
    if grep -q "$file" .github/workflows/build.yml; then
        echo "✓ Found release file pattern: $file"
    else
        echo "✗ Missing release file pattern: $file"
        exit 1
    fi
done

# Check build output directory structure
echo "7. Validating build output structure..."
if grep -q "publish/" .github/workflows/build.yml; then
    echo "✓ Uses publish/ directory structure"
else
    echo "✗ Missing publish/ directory structure"
    exit 1
fi

# Check for artifact cleanup
echo "8. Checking for PDB file cleanup..."
if grep -q "rm.*\.pdb" .github/workflows/build.yml; then
    echo "✓ Removes PDB files from artifacts"
else
    echo "✗ Missing PDB file cleanup"
    exit 1
fi

# Check for executable renaming
echo "9. Checking executable renaming patterns..."
RENAME_PATTERNS=("mv publish/win-x64/Zipper.exe" "mv publish/linux-x64/Zipper" "mv publish/osx-arm64/Zipper")

for pattern in "${RENAME_PATTERNS[@]}"; do
    if grep -q "$pattern" .github/workflows/build.yml; then
        echo "✓ Found renaming pattern: $pattern"
    else
        echo "✗ Missing renaming pattern: $pattern"
        exit 1
    fi
done

# Check for conditional builds based on cache
echo "10. Checking conditional build logic..."
if grep -q "cache-hit != 'true'" .github/workflows/build.yml; then
    echo "✓ Uses conditional builds based on cache"
else
    echo "✗ Missing conditional build logic"
    exit 1
fi

echo ""
echo "=== All Artifact Handling Tests Passed ==="
echo "Current artifact patterns are compatible with unified workflow design"