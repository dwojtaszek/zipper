#!/bin/bash

# Test script to validate build matrix strategy in current workflows
# This script checks matrix configuration and platform support

set -e

echo "=== Build Matrix Test (Unix) ==="
echo "Testing matrix strategy and platform configuration"

# Analyze current test.yml matrix
echo "1. Analyzing test.yml matrix strategy..."
if grep -q "matrix:" .github/workflows/test.yml; then
    echo "✓ test.yml has matrix strategy"
else
    echo "✗ test.yml missing matrix strategy"
    exit 1
fi

# Check matrix operating systems
echo "2. Checking matrix operating systems..."
TEST_OS=("ubuntu-latest" "windows-latest" "macos-latest")

for os in "${TEST_OS[@]}"; do
    if grep -q "$os" .github/workflows/test.yml; then
        echo "✓ Found $os in test matrix"
    else
        echo "✗ Missing $os in test matrix"
        exit 1
    fi
done

# Check matrix variable syntax
if grep -q "runs-on: \${{ matrix.os }}" .github/workflows/test.yml; then
    echo "✓ Uses correct matrix variable syntax"
else
    echo "✗ Incorrect matrix variable syntax"
    exit 1
fi

# Analyze build.yml platform support
echo "3. Analyzing build.yml platform support..."
BUILD_PLATFORMS=("win-x64" "linux-x64" "osx-arm64")

for platform in "${BUILD_PLATFORMS[@]}"; do
    if grep -q "$platform" .github/workflows/build.yml; then
        echo "✓ Found $platform in build configuration"
    else
        echo "✗ Missing $platform in build configuration"
        exit 1
    fi
done

# Check for runtime specifications
echo "4. Checking runtime specifications..."
RUNTIME_PLATFORMS=("win-x64" "linux-x64" "osx-arm64")

for platform in "${RUNTIME_PLATFORMS[@]}"; do
    if grep -q "\-r $platform" .github/workflows/build.yml; then
        echo "✓ Found runtime specification: -r $platform"
    else
        echo "✗ Missing runtime specification: -r $platform"
        exit 1
    fi
done

# Check build runners
echo "5. Checking build runner configuration..."
if grep -q "runs-on: ubuntu-latest" .github/workflows/build.yml; then
    echo "✓ Build job runs on ubuntu-latest"
else
    echo "✗ Build job runner not properly configured"
    exit 1
fi

# Check release job runner
if grep -q "runs-on: ubuntu-latest" .github/workflows/build.yml; then
    echo "✓ Release job runs on ubuntu-latest"
else
    echo "✗ Release job runner not properly configured"
    exit 1
fi

# Validate cross-platform build logic
echo "6. Validating cross-platform build logic..."
if grep -q "self-contained true" .github/workflows/build.yml; then
    echo "✓ Builds are self-contained"
else
    echo "✗ Builds are not self-contained"
    exit 1
fi

# Check output directories
echo "7. Checking output directories..."
OUTPUT_DIRS=("publish/win-x64" "publish/linux-x64" "publish/osx-arm64")

for dir in "${OUTPUT_DIRS[@]}"; do
    if grep -q "$dir" .github/workflows/build.yml; then
        echo "✓ Found output directory: $dir"
    else
        echo "✗ Missing output directory: $dir"
        exit 1
    fi
done

# Check conditional platform logic in tests
echo "8. Checking conditional platform logic in tests..."
if grep -q "runner.os != 'Windows'" .github/workflows/test.yml; then
    echo "✓ Found Unix/Linux conditional logic"
else
    echo "✗ Missing Unix/Linux conditional logic"
    exit 1
fi

if grep -q "runner.os == 'Windows'" .github/workflows/test.yml; then
    echo "✓ Found Windows conditional logic"
else
    echo "✗ Missing Windows conditional logic"
    exit 1
fi

# Check for proper shell specification
echo "9. Checking shell specifications..."
if grep -q "shell: cmd" .github/workflows/test.yml; then
    echo "✓ Windows steps use cmd shell"
else
    echo "✗ Missing shell specification for Windows"
    exit 1
fi

# Check for test script execution patterns
echo "10. Checking test script execution patterns..."
if grep -q "./tests/run-tests.sh" .github/workflows/test.yml; then
    echo "✓ Unix/Linux uses shell script"
else
    echo "✗ Missing Unix/Linux shell script execution"
    exit 1
fi

if grep -q "\\\\tests\\\\run-tests.bat" .github/workflows/test.yml; then
    echo "✓ Windows uses batch script"
else
    echo "✗ Missing Windows batch script execution"
    exit 1
fi

# Verify .NET setup consistency
echo "11. Verifying .NET setup consistency..."
if grep -q "dotnet-version: 8.0.x" .github/workflows/build.yml; then
    echo "✓ Build uses .NET 8.0.x"
else
    echo "✗ Build missing .NET 8.0.x"
    exit 1
fi

if grep -q "dotnet-version: 8.0.x" .github/workflows/test.yml; then
    echo "✓ Test uses .NET 8.0.x"
else
    echo "✗ Test missing .NET 8.0.x"
    exit 1
fi

echo ""
echo "=== All Build Matrix Tests Passed ==="
echo "Current matrix strategy supports unified workflow design"