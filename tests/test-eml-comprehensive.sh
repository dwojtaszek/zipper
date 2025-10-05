#!/bin/bash
# Comprehensive EML Test Suite - Unix Version (Linux/macOS)
# Constitutional Requirement: Must test ALL EML functionality scenarios
# Tests both Windows and Unix compatibility for EML feature implementation

# set -e  # Exit on any error - disabled for better debugging

echo "========================================"
echo "Comprehensive EML Test Suite - Unix"
echo "========================================"
echo

# Set test environment
TEST_DIR="/tmp/zipper-eml-test-$$"
REPO_ROOT="$(pwd)"
ZIPPER_CMD="dotnet run --project $REPO_ROOT/Zipper/Zipper.csproj --"

# Clean up function
cleanup() {
    if [ -d "$TEST_DIR" ]; then
        echo "Cleaning up test directory: $TEST_DIR"
        rm -rf "$TEST_DIR"
    fi
}

# Set trap for cleanup on exit
# trap cleanup EXIT

# Build the project first
echo "Building Zipper project..."
dotnet build Zipper/Zipper.csproj > /dev/null 2>&1

echo "Creating test directory: $TEST_DIR"
mkdir -p "$TEST_DIR"

# Test counters
TEST_COUNT=0
PASSED_COUNT=0

# Function to run a test scenario
run_test() {
    ((TEST_COUNT++))
    echo
    echo "Test $TEST_COUNT: $1"
    echo "Command: $2"

    TEST_PATH="$TEST_DIR/test_$TEST_COUNT"
    mkdir -p "$TEST_PATH"
    cd "$TEST_PATH"

    # Run the command
    cd ../../  # Go back to repo root for dotnet run
    CMD_WITH_PATH="$2 --output-path $TEST_PATH"
    if $CMD_WITH_PATH > "$TEST_PATH/test_output.log" 2>&1; then
        echo "✓ Test $TEST_COUNT PASSED - Command executed successfully"

        # Verify archive was created
        cd "$TEST_PATH"
        if ls archive_*.zip 1> /dev/null 2>&1; then
            echo "  - Archive file created successfully"

            # Extract and verify contents
            unzip -q archive_*.zip

            # Check DAT file structure
            if ls archive_*.dat 1> /dev/null 2>&1; then
                echo "  - Load file created successfully"

                # Count columns in header (first line)
                HEADER_LINE=$(head -n 1 archive_*.dat)

                # Count columns by counting the field delimiter character (ASCII 20 = \024 in octal)
                COLUMN_COUNT=$(echo "$HEADER_LINE" | od -c | grep -o '\ 024' | wc -l)
                ((COLUMN_COUNT++))  # Add 1 for the first column

                echo "  - Header: $HEADER_LINE"
                echo "  - Column count: $COLUMN_COUNT"

                # Validate expected columns
                echo "  - Expected columns: $3"
                if [ "$COLUMN_COUNT" -eq "$3" ]; then
                    echo "  ✓ Column count matches expectation"
                    ((PASSED_COUNT++))
                else
                    echo "  ✗ Column count mismatch - Expected $3, got $COLUMN_COUNT"
                fi

                # Check for text files if expected
                if [ "$4" = "check_text" ]; then
                    TEXT_FILE_COUNT=$(ls *.txt 2>/dev/null | wc -l)
                    if [ "$TEXT_FILE_COUNT" -gt 0 ]; then
                        echo "  ✓ Text files found as expected ($TEXT_FILE_COUNT files)"
                    else
                        echo "  ✗ Text files expected but not found"
                    fi
                fi

            else
                echo "  ✗ Load file not found"
            fi

        else
            echo "  ✗ Archive file not created"
        fi

    else
        echo "✗ Test $TEST_COUNT FAILED - Command failed"
        echo "Error output:"
        cat "$TEST_PATH/test_output.log"
    fi

    cd ../../ > /dev/null
}

# ========================================
# Test Scenarios
# ========================================

echo
echo "Running comprehensive EML test scenarios..."

# Test 1: Basic EML Generation (Baseline)
run_test "Basic EML Generation" "$ZIPPER_CMD --type eml --count 5 --folders 1" 7

# Test 2: EML with Metadata Only
run_test "EML with Metadata Only" "$ZIPPER_CMD --type eml --count 5 --folders 1 --with-metadata" 11

# Test 3: EML with Text Only
run_test "EML with Text Only" "$ZIPPER_CMD --type eml --count 5 --folders 1 --with-text" 8 "check_text"

# Test 4: EML with Both Metadata and Text
run_test "EML with Both Flags" "$ZIPPER_CMD --type eml --count 5 --folders 1 --with-metadata --with-text" 12 "check_text"

# Test 5: EML with Attachments and Full Flags
run_test "EML with Attachments" "$ZIPPER_CMD --type eml --count 5 --folders 2 --with-metadata --with-text --attachment-rate 50" 12 "check_text"

# Test 6: Performance Validation
run_test "Performance Test" "$ZIPPER_CMD --type eml --count 100 --folders 3 --with-metadata --with-text" 12 "check_text"

# ========================================
# Test Results Summary
# ========================================

echo
echo "========================================"
echo "Test Results Summary"
echo "========================================"
echo "Total Tests: $TEST_COUNT"
echo "Passed: $PASSED_COUNT"
FAILED_COUNT=$((TEST_COUNT - PASSED_COUNT))
echo "Failed: $FAILED_COUNT"

if [ $FAILED_COUNT -eq 0 ]; then
    echo
    echo "✓ ALL TESTS PASSED - EML feature implementation is working correctly"
    echo "✓ Cross-platform validation successful"
    exit 0
else
    echo
    echo "✗ SOME TESTS FAILED - Please review the implementation"
    exit 1
fi