# Performance Testing Quick Start

## Getting Started with Performance Testing

This guide helps you quickly test and validate Zipper's performance on your system.

## Prerequisites

- .NET 8.0 SDK installed
- Sufficient disk space (at least 2GB for testing)
- Administrator privileges (for accurate performance measurements)

## Quick Performance Test

Run this simple command to test basic performance:

```bash
# Generate 10,000 PDF files - should complete in 5-15 seconds
./zipper --type pdf --count 10000 --output-path ./perf_test --folders 10
```

**Expected output:**
```
Starting parallel file generation...
  File Type: pdf
  Count: 10,000
  Output Path: ./perf_test
  Folders: 10
  Encoding: UTF-8
  Distribution: Proportional

Progress: 10,000 / 10,000 files (100.0%) - 1,500.2 files/sec - ETA: 00:00:00

Generation complete in 6.7 seconds.
  Archive created: ./perf_test/archive_20231017_143022.zip
  Performance: 1,492.5 files/second
```

## Performance Benchmark Suite

### Small Scale Tests

```bash
# 1K files - baseline test
./zipper --type pdf --count 1000 --output-path ./test_1k --folders 5

# 5K files - small batch
./zipper --type pdf --count 5000 --output-path ./test_5k --folders 10

# 10K files - medium batch
./zipper --type pdf --count 10000 --output-path ./test_10k --folders 15
```

### Medium Scale Tests

```bash
# 50K files - large batch
./zipper --type pdf --count 50000 --output-path ./test_50k --folders 25

# 100K files - very large batch
./zipper --type pdf --count 100000 --output-path ./test_100k --folders 50
```

### Advanced Performance Tests

```bash
# With metadata (slower but more realistic)
./zipper --type pdf --count 25000 --output-path ./test_meta --folders 20 --with-metadata

# With extracted text (generates additional files)
./zipper --type pdf --count 25000 --output-path ./test_text --folders 20 --with-text

# Email generation with attachments
./zipper --type eml --count 10000 --output-path ./test_email --folders 15 --attachment-rate 30
```

## Interpreting Results

### Good Performance Indicators

- **1K files**: < 2 seconds, > 500 files/sec
- **10K files**: < 15 seconds, > 700 files/sec
- **100K files**: < 120 seconds, > 800 files/sec

### Performance Factors

| Factor | Impact | Recommendation |
|--------|--------|----------------|
| **Storage Type** | High | Use SSD over HDD |
| **Available RAM** | Medium | 8GB+ for large jobs |
| **CPU Cores** | High | More cores = better parallelization |
| **Antivirus** | Medium | May slow down file creation |
| **Background Load** | High | Close unnecessary apps |

## Performance Troubleshooting

### If Performance is Poor

1. **Check storage speed**:
   ```bash
   # Test disk write speed (Linux/macOS)
   dd if=/dev/zero of=./disktest bs=1M count=1000

   # Test disk write speed (Windows PowerShell)
   $testFile = "C:\temp\disktest.tmp"
   $data = new-object byte[] 1MB
   $stream = [System.IO.File]::OpenWrite($testFile)
   for($i=0; $i -lt 1000; $i++) { $stream.Write($data, 0, $data.Length) }
   $stream.Close()
   ```

2. **Check system resources**:
   - Monitor CPU usage during generation
   - Check available memory
   - Look for disk I/O bottlenecks

3. **Optimize test conditions**:
   - Use local storage, not network drives
   - Temporarily disable real-time antivirus scanning
   - Close unnecessary applications

## Automated Testing

For automated performance testing, you can use the built-in benchmark runner:

```csharp
// Create a simple test program
using Zipper;

class PerformanceTest
{
    static async Task Main(string[] args)
    {
        await PerformanceBenchmarkRunner.RunBenchmarks();
    }
}
```

Compile and run:
```bash
dotnet run --project PerformanceTest.csproj
```

## Performance Monitoring

During generation, monitor these metrics:

### System Resources
- **CPU Usage**: Should be 70-90% during generation
- **Memory Usage**: Should remain stable, not continuously grow
- **Disk I/O**: High write activity during generation

### Application Metrics
- **Files/Second**: Should remain relatively consistent
- **ETA Accuracy**: Estimated time should be reasonably accurate
- **Progress**: Should advance smoothly

## Cleanup

After testing, clean up the generated files:

```bash
# Remove test directories (Linux/macOS)
rm -rf ./perf_test ./test_*

# Remove test directories (Windows PowerShell)
Remove-Item -Recurse -Force perf_test, test_*
```

## Next Steps

- Read the [Performance Optimization Guide](performance.md) for detailed technical information
- Check the main [README](../README.md) for usage examples
- Review the [Implementation Plan](../plans/2025-10-16-performance-optimization.md) for technical details

## Support

If you encounter performance issues:

1. Check system requirements and available resources
2. Try smaller file counts to isolate the issue
3. Test with different storage locations
4. Review the troubleshooting guide above

For additional help, create an issue in the project repository with:
- Your system specifications
- The command you ran
- The output you received
- Any error messages