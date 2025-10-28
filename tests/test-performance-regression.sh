#!/bin/bash

# Performance regression test script for Zipper
# This script runs performance tests to ensure no regressions were introduced

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test configuration
TEST_OUTPUT_DIR="./performance-results"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
REPORT_DIR="$TEST_OUTPUT_DIR/$TIMESTAMP"
PROJECT="Zipper/Zipper.Tests/Zipper.Tests.csproj"

# Helper functions
print_success() {
    echo -e "${GREEN}[ SUCCESS ]${NC} $1"
}

print_info() {
    echo -e "${BLUE}[ INFO ]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[ WARNING ]${NC} $1"
}

print_error() {
    echo -e "${RED}[ ERROR ]${NC} $1"
}

# Create output directory
mkdir -p "$REPORT_DIR"

# Function to run unit tests with performance focus
run_performance_unit_tests() {
    print_info "Running performance-focused unit tests..."

    # Run performance regression tests
    print_info "Running PerformanceRegressionTests..."
    if dotnet test "$PROJECT" --filter "FullyQualifiedName~PerformanceRegressionTests" \
        --logger "console;verbosity=detailed" \
        --results-directory "$REPORT_DIR" \
        --collect:"XPlat Code Coverage" \
        --logger "trx;LogFileName=performance_regression_tests.trx"; then
        print_success "Performance regression tests passed"
    else
        print_error "Performance regression tests failed"
        return 1
    fi

    # Run end-to-end performance tests
    print_info "Running EndToEndPerformanceTests..."
    if dotnet test "$PROJECT" --filter "FullyQualifiedName~EndToEndPerformanceTests" \
        --logger "console;verbosity=detailed" \
        --results-directory "$REPORT_DIR" \
        --collect:"XPlat Code Coverage" \
        --logger "trx;LogFileName=end_to_end_performance_tests.trx"; then
        print_success "End-to-end performance tests passed"
    else
        print_error "End-to-end performance tests failed"
        return 1
    fi
}

# Function to run BenchmarkDotNet benchmarks
run_benchmarks() {
    print_info "Running BenchmarkDotNet performance benchmarks..."

    # Create benchmark results file
    local benchmark_output="$REPORT_DIR/benchmark_results.txt"

    # Run benchmarks (if BenchmarkDotNet is available)
    if dotnet run --project "$PROJECT" --configuration Release \
        -- --filter "PerformanceBenchmarks" \
        --job "ShortRun" \
        --exporters "Json" \
        --artifacts-path "$REPORT_DIR/benchmarks" > "$benchmark_output" 2>&1; then
        print_success "Benchmarks completed successfully"

        # Extract key metrics from benchmark results
        if [ -f "$REPORT_DIR/benchmarks/results.json" ]; then
            print_info "Benchmark results saved to JSON"
        fi

        # Display benchmark summary
        print_info "Benchmark Summary:"
        grep -E "(Mean|Allocated|Gen 0|Gen 1|Gen 2)" "$benchmark_output" || true
    else
        print_warning "Benchmarks could not be run (may require different runtime)"
        cat "$benchmark_output" || true
    fi
}

# Function to test real-world performance scenarios
run_real_world_performance_tests() {
    print_info "Running real-world performance tests..."

    local temp_dir=$(mktemp -d)
    trap "rm -rf $temp_dir" RETURN

    # Test 1: Small dataset performance
    print_info "Test 1: Small dataset (100 files)"
    local start_time=$(date +%s%N)
    dotnet run --project Zipper/Zipper.csproj -- \
        --type pdf \
        --count 100 \
        --output-path "$temp_dir/small" \
        --folders 5 \
        --distribution proportional \
        --include-load-file
    local end_time=$(date +%s%N)
    local duration_ms=$(( (end_time - start_time) / 1000000 ))

    if [ $duration_ms -lt 2000 ]; then
        print_success "Small dataset test completed in ${duration_ms}ms"
    else
        print_warning "Small dataset test took ${duration_ms}ms (target < 2000ms)"
    fi

    # Verify output
    if [ -f "$temp_dir/small.zip" ] && [ -f "$temp_dir/small.dat" ]; then
        local zip_size=$(stat -c%s "$temp_dir/small.zip")
        print_info "Small dataset: ZIP size ${zip_size} bytes"
    fi

    # Test 2: Medium dataset performance
    print_info "Test 2: Medium dataset (1000 files)"
    start_time=$(date +%s%N)
    dotnet run --project Zipper/Zipper.csproj -- \
        --type pdf \
        --count 1000 \
        --output-path "$temp_dir/medium" \
        --folders 10 \
        --distribution gaussian \
        --with-metadata \
        --include-load-file
    end_time=$(date +%s%N)
    duration_ms=$(( (end_time - start_time) / 1000000 ))

    if [ $duration_ms -lt 10000 ]; then
        print_success "Medium dataset test completed in ${duration_ms}ms"
    else
        print_warning "Medium dataset test took ${duration_ms}ms (target < 10000ms)"
    fi

    # Verify output
    if [ -f "$temp_dir/medium.zip" ] && [ -f "$temp_dir/medium.dat" ]; then
        local zip_size=$(stat -c%s "$temp_dir/medium.zip")
        print_info "Medium dataset: ZIP size ${zip_size} bytes"
    fi

    # Test 3: EML performance
    print_info "Test 3: EML files with attachments (500 files)"
    start_time=$(date +%s%N)
    dotnet run --project Zipper/Zipper.csproj -- \
        --type eml \
        --count 500 \
        --output-path "$temp_dir/eml" \
        --folders 8 \
        --distribution exponential \
        --attachment-rate 60 \
        --with-metadata \
        --with-text \
        --include-load-file
    end_time=$(date +%s%N)
    duration_ms=$(( (end_time - start_time) / 1000000 ))

    print_info "EML dataset test completed in ${duration_ms}ms"

    # Verify output
    if [ -f "$temp_dir/eml.zip" ] && [ -f "$temp_dir/eml.dat" ]; then
        local zip_size=$(stat -c%s "$temp_dir/eml.zip")
        print_info "EML dataset: ZIP size ${zip_size} bytes"
    fi

    # Calculate throughput metrics
    print_info "Performance Summary:"
    print_info "Small dataset (100 files): $((100 * 1000 / duration_ms)) files/second"
    print_info "Medium dataset (1000 files): $((1000 * 1000 / duration_ms)) files/second"
    print_info "EML dataset (500 files): $((500 * 1000 / duration_ms)) files/second"
}

# Function to analyze performance trends
analyze_performance_trends() {
    print_info "Analyzing performance trends..."

    # Create performance summary report
    local summary_file="$REPORT_DIR/performance_summary.txt"

    cat > "$summary_file" << EOF
Zipper Performance Regression Test Report
========================================
Date: $(date)
Platform: $(uname -s) $(uname -r)
Architecture: $(uname -m)
.NET Version: $(dotnet --version | head -1)

Test Results:
------------

EOF

    # Add test results to summary
    echo "1. Unit Test Performance:" >> "$summary_file"
    if [ -f "$REPORT_DIR/performance_regression_tests.trx" ]; then
        echo "   - Performance Regression Tests: PASSED" >> "$summary_file"
    else
        echo "   - Performance Regression Tests: FAILED" >> "$summary_file"
    fi

    if [ -f "$REPORT_DIR/end_to_end_performance_tests.trx" ]; then
        echo "   - End-to-End Performance Tests: PASSED" >> "$summary_file"
    else
        echo "   - End-to-End Performance Tests: FAILED" >> "$summary_file"
    fi

    echo "" >> "$summary_file"
    echo "2. Real-World Performance:" >> "$summary_file"
    echo "   - Small Dataset (100 files): Target < 2s" >> "$summary_file"
    echo "   - Medium Dataset (1000 files): Target < 10s" >> "$summary_file"
    echo "   - EML Dataset (500 files): Target reasonable time" >> "$summary_file"

    echo "" >> "$summary_file"
    echo "3. Recommendations:" >> "$summary_file"
    echo "   - Monitor performance metrics over time" >> "$summary_file"
    echo "   - Set up automated alerts for regressions" >> "$summary_file"
    echo "   - Compare results with baseline measurements" >> "$summary_file"

    print_info "Performance summary report generated: $summary_file"
    cat "$summary_file"
}

# Function to compare with previous results (if available)
compare_with_baseline() {
    print_info "Comparing with previous baseline results..."

    local latest_baseline=$(find "$TEST_OUTPUT_DIR" -maxdepth 1 -type d -name "20*" | sort | tail -2 | head -1)

    if [ -n "$latest_baseline" ] && [ -d "$latest_baseline" ]; then
        print_info "Found previous baseline: $latest_baseline"

        # Compare key metrics if available
        if [ -f "$latest_baseline/performance_summary.txt" ]; then
            print_info "Previous performance results available for comparison"
            # Here you could add specific comparison logic
        else
            print_warning "No previous performance summary found"
        fi
    else
        print_info "No previous baseline found - establishing new baseline"
    fi
}

# Main execution
main() {
    print_info "Starting Zipper performance regression tests..."
    print_info "Platform: $(uname -s) $(uname -r)"
    print_info "Architecture: $(uname -m)"
    print_info ".NET Version: $(dotnet --version | head -1)"
    print_info "Results directory: $REPORT_DIR"

    # Run all performance tests
    run_performance_unit_tests || exit 1
    run_benchmarks
    run_real_world_performance_tests
    analyze_performance_trends
    compare_with_baseline

    print_success "All performance regression tests completed!"
    print_info "Results saved to: $REPORT_DIR"
    print_info "Summary report: $REPORT_DIR/performance_summary.txt"

    # Cleanup old test results (keep last 5)
    find "$TEST_OUTPUT_DIR" -maxdepth 1 -type d -name "20*" | sort -r | tail -n +6 | xargs -r rm -rf
    print_info "Cleaned up old test results (kept last 5)"
}

# Run main function
main "$@"