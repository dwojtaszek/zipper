# Zipper Stress Test Suite

## ⚠️ IMPORTANT WARNING

**These stress tests are for MANUAL INVOCATION ONLY.**

- ❌ NOT part of CI/CD pipelines
- ❌ NOT part of pre-commit hooks
- ❌ NOT automated in any way
- ✅ MUST be run manually by developers
- ✅ REQUIRE explicit confirmation before execution

These tests consume significant system resources and are designed to push the application to its limits.

## 🎯 Purpose

The stress test suite is designed to test failure modes and edge cases that are not covered in regular E2E tests. Each stress test focuses on specific aspects of the application's performance and stability under extreme conditions.

## 🔧 Available Stress Tests

### 1. 10GB Maximum File Count Challenge
**Script:** `stress-10gb-filecount.sh`

**Focus:** Tests absolute limits of file count handling and Zip64 functionality

**Configuration:**
- Target Size: ~10GB compressed archive
- File Count: 5 million PDF files
- Distribution: Exponential across 100 folders
- Features: Metadata + Text extraction enabled

**Unique Aspect:** Tests the relationship between maximum file count vs archive size

**Requirements:**
- Disk Space: ~12GB+ (20% overhead)
- Runtime: 5-10 minutes
- Memory: Up to 2GB
- CPU: High usage

### 3. 30GB Attachment-Heavy EML Focus
**Script:** `stress-30gb-attachments.sh`

**Focus:** Tests attachment handling, nested file processing, and archive size limits

**Configuration:**
- Target Size: ~30GB compressed archive
- Primary: 1 million EML files with 80% attachment rate
- Attachments: Varied PDF/JPG/TIFF files (2-5MB each)
- Distribution: Proportional across 100 folders
- Features: Metadata + Text extraction for all files and attachments

**Unique Aspect:** Tests attachment-heavy generation with nested content processing

**Requirements:**
- Disk Space: ~36GB+ (20% overhead)
- Runtime: 15-30 minutes
- Memory: Up to 6GB+
- CPU: Extremely high usage

### 4. Large Load File Performance
**Script:** `stress-large-loadfile.sh`

**Focus:** Tests large data handling scenarios focused on load file performance

**Configuration:**
- File Count: 10,000 files
- Target Load File Size: ~500MB
- Features: Maximum metadata (all columns enabled)
- Variations: External vs embedded load files, multiple encodings

**Unique Aspect:** Focuses specifically on load file generation bottlenecks

**Requirements:**
- Disk Space: ~2GB
- Runtime: 5-10 minutes
- Memory: Under 1GB
- CPU: Moderate usage

## 🚀 Running Stress Tests

### Prerequisites

1.  **System Requirements:**
    *   Linux/macOS (bash shell required)
    *   Sufficient disk space (check individual test requirements)
    *   Adequate memory (check individual test requirements)
    *   Multi-core CPU recommended

2.  **Software Requirements:**
    *   .NET 8.0 SDK
    *   Required utilities: `bc`, `df`, `stat`, `unzip`, `file`, `grep`, `wc`, `find`
        *   **Ubuntu/Debian:** `sudo apt-get install bc unzip`
        *   **macOS:** `brew install bc`

3.  **Application Build:**
    ```bash
    dotnet build -c Release
    ```

### Pre-run Validations

All stress tests include comprehensive pre-run validations:

- ✅ **Utility Check:** Verifies all required command-line utilities are installed
- ✅ **Disk Space Check:** Verifies sufficient available space (+20% overhead)
- ✅ **System Resources Check:** Validates memory and CPU capacity
- ✅ **Test Details Display:** Shows exact configuration and runtime estimates
- ✅ **Explicit Confirmation:** Requires manual confirmation before execution

### Execution Steps

1.  **Navigate to stress test directory:**
    ```bash
    cd tests/stress
    ```

2.  **Run the main test runner:**
    You can run all tests sequentially or specify a single test.

    ```bash
    # Run all stress tests
    ./run-stress-tests.sh

    # Run a specific test by name (substring match)
    ./run-stress-tests.sh 10gb
    ./run-stress-tests.sh multi-format
    ```

3.  **Alternatively, run an individual test script:**
    ```bash
    ./stress-10gb-filecount.sh
    ```

4.  **Confirm execution when prompted:**
    *   Each test will display resource requirements
    *   Read the warnings carefully
    *   Press Enter to continue or Ctrl+C to cancel

## 📊 Test Results and Output

### Output Structure

All stress tests write their output to a `results` directory within the `tests/stress` directory.

```
tests/stress/
└── results/
    ├── archive_20251019_104843.zip
    └── archive_20251019_104843.dat
```

### Validation Performed

All stress tests include comprehensive post-execution validation:

- ✅ **File Count Verification:** Confirms exact number of files generated
- ✅ **Archive Size Validation:** Checks if target size achieved (±10% tolerance)
- ✅ **Load File Structure:** Validates line count and header columns
- ✅ **Encoding Verification:** Confirms correct text encoding
- ✅ **Content Integrity:** Validates file content and structure
- ✅ **Feature Verification:** Confirms all requested features are present

### Performance Metrics

Each test reports:
- **Generation Time:** Total time taken for file generation
- **Throughput:** Files per second generation rate
- **Memory Usage:** Peak memory consumption during execution
- **Disk Usage:** Final archive and load file sizes
- **Feature Performance:** Individual feature performance metrics

## 🧹 Cleanup

### Automatic Cleanup

Stress tests do **not** automatically clean up generated files to allow for manual inspection and analysis.

### Manual Cleanup

When you're finished with test results, clean up the `results` directory:

```bash
# Remove all stress test outputs
rm -rf results/

# Check current disk usage
du -sh results/
```

## 🐛 Troubleshooting

### Common Issues

1.  **Disk Space Insufficient:**
    ```
    Error: Insufficient disk space. Need X GB, have Y GB
    ```
    **Solution:** Free up disk space or run on a machine with more storage

2.  **Memory Issues:**
    ```
    System.OutOfMemoryException
    ```
    **Solution:** Run on a machine with more RAM or reduce test parameters

3.  **Permission Denied:**
    ```
    Error: Permission denied
    ```
    **Solution:** Ensure write permissions to the output directory

4.  **Missing Dependencies:**
    ```
    command not found: bc
    ```
    **Solution:** Install required utilities:
    ```bash
    # Ubuntu/Debian
    sudo apt-get install bc unzip

    # macOS
    brew install bc
    ```

### Getting Help

If you encounter issues with stress tests:

1.  Check system requirements for the specific test
2.  Ensure all dependencies are installed
3.  Verify sufficient disk space and memory
4.  Check that the application builds successfully
5.  Review test output for specific error messages

## 📝 Development Notes

### Adding New Stress Tests

When adding new stress tests:

1.  Follow the established naming convention: `stress-<description>.sh`
2.  Include comprehensive pre-run validations
3.  Add detailed warnings and resource requirements
4.  Implement thorough post-execution validation
5.  Provide clear performance metrics and summaries
6.  Update this README with new test details

### Test Design Principles

- **Manual Only:** Never automate stress tests
- **Resource Validation:** Always check system resources first
- **Clear Warnings:** Provide explicit warnings about resource usage
- **Comprehensive Validation:** Validate all aspects of generated output
- **Performance Metrics:** Report detailed performance information
- **Cleanup Guidance:** Provide clear cleanup instructions

## 🔗 Related Documentation

- [Main Test Suite](../README.md)
- [Regular E2E Tests](../run-tests.sh)
- [Optimized Pre-commit Tests](../run-tests-optimized.sh)
- [Application README](../../README.md)
- [Requirements](../../Requirements.md)

---

**Remember:** Stress tests are powerful tools for finding edge cases and performance bottlenecks, but they require careful manual execution and monitoring. Always run them on appropriate hardware with sufficient resources.
