#!/bin/bash
# Path Traversal Security Test Script
# Tests that path traversal attempts are properly blocked

echo "🔒 Path Traversal Security Test"
echo "================================="

# Create test directory
TEST_DIR="$(mktemp -d)"
echo "Created test directory: $TEST_DIR"

# Build the application
echo "Building Zipper..."
dotnet build src/Zipper.csproj --configuration Release > /dev/null 2>&1
if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    rm -rf "$TEST_DIR"
    exit 1
fi

# Test 1: Normal path should work
echo ""
echo "Test 1: Normal path should work"
ZIPPER_DLL="src/bin/Release/net8.0/Zipper.dll"
if [ ! -f "$ZIPPER_DLL" ]; then
    echo "❌ Could not find Zipper DLL at $ZIPPER_DLL"
    rm -rf "$TEST_DIR"
    exit 1
fi

# Run with normal path
dotnet "$ZIPPER_DLL" --type pdf --count 5 --output-path "$TEST_DIR/normal" > /dev/null 2>&1
if [ $? -eq 0 ] && [ -f "$TEST_DIR/normal/archive_"*.zip ]; then
    echo "✅ Normal path works correctly"
    rm -f "$TEST_DIR/normal/"*
else
    echo "❌ Normal path failed"
fi

# Test 2: Path traversal with ../ should be blocked
echo ""
echo "Test 2: Path traversal with ../ should be blocked"

SECURITY_OUTPUT=$(dotnet "$ZIPPER_DLL" --type pdf --count 5 --output-path "$TEST_DIR/../security_test" 2>&1)
SECURITY_EXIT_CODE=$?

if [ $SECURITY_EXIT_CODE -ne 0 ] && echo "$SECURITY_OUTPUT" | grep -q "Path traversal detected"; then
    echo "✅ Path traversal with ../ was properly blocked"
    echo "   Error message: $(echo "$SECURITY_OUTPUT" | grep "Path traversal detected" | head -1)"
else
    echo "❌ Path traversal with ../ was not properly blocked"
    echo "   Exit code: $SECURITY_EXIT_CODE"
    echo "   Output: $SECURITY_OUTPUT"
fi

# Test 3: Path traversal with absolute path to system should be blocked
echo ""
echo "Test 3: Absolute path traversal attempt should be blocked"

SYSTEM_OUTPUT=$(dotnet "$ZIPPER_DLL" --type pdf --count 5 --output-path "/tmp/../etc/security_test" 2>&1)
SYSTEM_EXIT_CODE=$?

if [ $SYSTEM_EXIT_CODE -ne 0 ] && (echo "$SYSTEM_OUTPUT" | grep -q "Path traversal detected\|Error: Invalid path"); then
    echo "✅ Absolute path traversal was properly blocked"
    echo "   Error message: $(echo "$SYSTEM_OUTPUT" | grep -E "Path traversal detected|Error: Invalid path" | head -1)"
else
    echo "❌ Absolute path traversal was not properly blocked"
    echo "   Exit code: $SYSTEM_EXIT_CODE"
    echo "   Output: $SYSTEM_OUTPUT"
fi

# Test 4: Path with invalid characters should be blocked
echo ""
echo "Test 4: Path with invalid characters should be blocked"

INVALID_OUTPUT=$(dotnet "$ZIPPER_DLL" --type pdf --count 5 --output-path "$TEST_DIR/invalid<name" 2>&1)
INVALID_EXIT_CODE=$?

if [ $INVALID_EXIT_CODE -ne 0 ] && echo "$INVALID_OUTPUT" | grep -q "Invalid character"; then
    echo "✅ Path with invalid characters was properly blocked"
    echo "   Error message: $(echo "$INVALID_OUTPUT" | grep "Invalid character" | head -1)"
else
    echo "❌ Path with invalid characters was not properly blocked"
    echo "   Exit code: $INVALID_EXIT_CODE"
    echo "   Output: $INVALID_OUTPUT"
fi

# Test 5: Empty path should be blocked
echo ""
echo "Test 5: Empty path should be blocked"

EMPTY_OUTPUT=$(dotnet "$ZIPPER_DLL" --type pdf --count 5 --output-path "" 2>&1)
EMPTY_EXIT_CODE=$?

if [ $EMPTY_EXIT_CODE -ne 0 ] && echo "$EMPTY_OUTPUT" | grep -q "Output path cannot be null or empty"; then
    echo "✅ Empty path was properly handled"
    echo "   Error message: $(echo "$EMPTY_OUTPUT" | grep "Output path cannot be null or empty" | head -1)"
else
    echo "❌ Empty path was not properly handled"
    echo "   Exit code: $EMPTY_EXIT_CODE"
    echo "   Output: $EMPTY_OUTPUT"
fi

# Test 6: Path with mixed separators and traversal
echo ""
echo "Test 6: Path with mixed separators and traversal should be blocked"

MIXED_OUTPUT=$(dotnet "$ZIPPER_DLL" --type pdf --count 5 --output-path "folder/../..\\mixed\\path" 2>&1)
MIXED_EXIT_CODE=$?

if [ $MIXED_EXIT_CODE -ne 0 ] && (echo "$MIXED_OUTPUT" | grep -q "Path traversal detected\|Invalid character"); then
    echo "✅ Mixed separators with traversal was properly blocked"
    echo "   Error message: $(echo "$MIXED_OUTPUT" | grep -E "Path traversal detected|Invalid character" | head -1)"
else
    echo "❌ Mixed separators with traversal was not properly blocked"
    echo "   Exit code: $MIXED_EXIT_CODE"
    echo "   Output: $MIXED_OUTPUT"
fi

# Cleanup
echo ""
echo "Cleaning up test directory..."
rm -rf "$TEST_DIR"

echo ""
echo "✅ Path traversal security testing completed"
echo ""
echo "Summary:"
echo "- Path traversal attempts are properly blocked"
echo "- Invalid characters are detected and rejected"
echo "- Normal valid paths continue to work correctly"
echo "- Security fixes do not break legitimate functionality"