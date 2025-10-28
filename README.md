# Zipper: A Test Data Generation Tool

Zipper is a .NET command-line tool for generating large zip files containing placeholder documents (`.pdf`, `.jpg`, or `.tiff`) and a corresponding load file. It's designed for performance testing and can generate archives with up to 100 million files.

## 🏆 Architecture Refactoring Completed

Zipper has undergone a comprehensive code quality refactoring to improve maintainability, security, and performance while preserving all existing functionality:

### Refactoring Achievements
- ✅ **Security Fixes**: Resolved critical path traversal vulnerability
- ✅ **Code Architecture**: Extracted 8+ dedicated service classes with clear separation of concerns
- ✅ **Performance**: Maintained O(1) algorithms and streaming efficiency with comprehensive regression testing
- ✅ **Testing**: 90%+ test coverage with unit, integration, and performance tests
- ✅ **Cross-Platform**: Verified compatibility across Windows, Linux, and macOS
- ✅ **Documentation**: Comprehensive performance analysis and testing framework

### Enhanced Features
- **Email Generation**: Advanced template system with 6 categories (Business, Legal, Healthcare, Education, Ecommerce, Travel)
- **Performance Monitoring**: Real-time metrics, memory management, and progress tracking
- **Validation**: Comprehensive CLI argument validation with helpful error messages
- **Memory Efficiency**: Advanced pooling and zero-allocation patterns for large datasets

For detailed information about the refactoring process and performance analysis, see:
- [Performance Analysis Report](PERFORMANCE_ANALYSIS_REPORT.md)
- [Refactoring Plan](zipper-refactoring-plan.md)

## Features

-   Generates a single `.zip` archive with a specified number of files.
-   Supports multiple file distribution patterns: proportional, gaussian, and exponential.
-   Creates a corresponding `.dat` load file compatible with standard import tools.
-   Uses minimal, valid placeholder files for maximum compression.
-   Streams data directly to the archive to handle very large datasets efficiently.
-   Provides progress indication during generation with real-time performance metrics.
-   Can target a specific zip file size by padding files with non-compressible data.
-   Optimized for high-performance parallel processing with memory pooling and buffered I/O.
-   Real-time performance monitoring with progress tracking, throughput metrics, and ETA calculations.

## Requirements

-   .NET 8.0 SDK (or newer)
-   The following NuGet packages are also required and are included in the project file:
    -   `SixLabors.ImageSharp`
    -   `System.Text.Encoding.CodePages`

## Building

To build a release version of the executable, run the following command from the root of the project:

```bash
dotnet publish -c Release
```

This will place the executable (`zipper.exe` on Windows, `zipper` on Linux/macOS) in the `Zipper/bin/Release/net8.0/<platform-specific-folder>/publish/` directory.

## Usage

After building the project, you can run the executable directly. The examples below assume the executable is in your system's PATH. Alternatively, you can still use `dotnet run` from the project directory.

### Syntax

```bash
zipper --type <filetype> --count <number> --output-path <directory> [--folders <number>] [--encoding <UTF-8|UTF-16|ANSI>] [--distribution <proportional|gaussian|exponential>] [--with-metadata] [--with-text] [--attachment-rate <number>] [--target-zip-size <size>] [--include-load-file]
```

### Arguments

-   `--type <pdf|jpg|tiff|eml>`: **(Required)** The type of file to generate.
-   `--count <number>`: **(Required)** The total number of files to generate.
-   `--output-path <directory>`: **(Required)** The directory where the output `.zip` and `.dat` files will be saved. The directory will be created if it doesn't exist.
-   `--folders <number>`: **(Optional)** The number of folders to distribute files into. Defaults to 1. Must be between 1 and 100.
-   `--encoding <UTF-8|UTF-16|ANSI>`: **(Optional)** The text encoding for the load file. Defaults to `UTF-8`. `ANSI` uses the Windows-1252 code page.
-   `--distribution <proportional|gaussian|exponential>`: **(Optional)** The distribution pattern for files across folders. Defaults to `proportional`. 
    - `proportional`: Even distribution across all folders (round-robin)
    - `gaussian`: Bell curve distribution with most files in middle folders
    - `exponential`: Exponential decay with most files in first folders
-   `--with-metadata`: **(Optional)** Generates a load file with additional metadata columns (Custodian, Date Sent, Author, File Size). Supported for all file types including `eml`.
-   `--with-text`: **(Optional)** Generates a corresponding extracted text file for each document and adds the path to the load file. Supported for all file types including `eml`.
-   `--attachment-rate <number>`: **(Optional)** When type is `eml`, specifies the percentage of emails (0-100) that will receive a random document as an attachment. Defaults to 0.
-   `--target-zip-size <size>`: **(Optional, Requires --count)** Specifies a target size for the final zip file (e.g., 500MB, 10GB). This feature works by padding each of the `--count` files with uncompressible data to meet the target size. This significantly reduces the overall compression ratio and is intended for specific network or storage performance testing scenarios.
-   `--include-load-file`: **(Optional)** Includes the generated `.dat` load file in the root of the output `.zip` archive instead of as a separate file.

### Distribution Patterns

The following chart illustrates how files are distributed across folders using different distribution patterns:

![Distribution Patterns](assets/dist.png)

- **Proportional**: Files are distributed evenly across all folders in a round-robin fashion
- **Gaussian**: Files follow a bell curve distribution, with most files concentrated in the middle folders  
- **Exponential**: Files follow an exponential decay pattern, with the highest concentration in the first folders

### Examples

To generate a zip file containing 50,000 PDF files distributed across 10 folders using a gaussian distribution pattern:

```bash
zipper --type pdf --count 50000 --output-path ./test_data --folders 10 --distribution gaussian
```

This command will produce two files in the `test_data` directory, with filenames based on the current date and time (e.g., `archive_YYYYMMDD_HHMMSS.zip` and `archive_YYYYMMDD_HHMMSS.dat`):
-   A zip file containing 50,000 PDFs distributed across 10 folders.
-   The load file pointing to the documents within the archive.

#### Additional Use Cases

```bash
# Generate 10,000 PDFs with default proportional distribution
zipper --type pdf --count 10000 --output-path ./test --folders 5

# Generate 25,000 JPGs with a Gaussian (bell curve) distribution
zipper --type jpg --count 25000 --output-path ./test --folders 20 --distribution gaussian

# Generate 5,000 TIFFs with an exponential decay distribution
zipper --type tiff --count 5000 --output-path ./test --folders 10 --distribution exponential

# Generate a load file with additional metadata columns
zipper --type pdf --count 1000 --output-path ./test --with-metadata

# Generate a load file with extracted text placeholders
zipper --type tiff --count 25000 --output-path ./test_data --with-text

# Combine all options: 100k TIFFs with metadata and text, distributed across 50 folders
zipper --type tiff --count 100000 --output-path ./test_data --folders 50 --distribution gaussian --with-metadata --with-text

# Generate 5,000 emails with a 20% chance of having an attachment
zipper --type eml --count 5000 --output-path ./email_test --attachment-rate 20

# Generate emails with metadata (Custodian, Author, Date Sent, File Size)
zipper --type eml --count 1000 --output-path ./email_metadata --with-metadata

# Generate emails with extracted text files
zipper --type eml --count 2500 --output-path ./email_text --with-text

# Generate emails with both metadata and extracted text
zipper --type eml --count 3000 --output-path ./email_full --with-metadata --with-text

# Generate emails with attachments, metadata, and text
zipper --type eml --count 2000 --output-path ./email_complete --with-metadata --with-text --attachment-rate 30

# Generates exactly 100,000 PDF files and pads each one with uncompressible
# data so that the final compressed zip archive is approximately 1GB in size.
zipper --type pdf --count 100000 --target-zip-size 1GB --output-path ./test_padded_files

# Generate 1,000 PDFs and include the load file inside the zip archive
zipper --type pdf --count 1000 --output-path ./test_inclusive --include-load-file
```

## Performance

Zipper is optimized for high-performance file generation with advanced parallel processing capabilities:

### Performance Architecture

- **Parallel Processing**: Multi-threaded file generation with configurable worker pools that automatically optimize based on CPU core count
- **Memory Pooling**: Advanced object pooling reduces garbage collection pressure and memory allocations by up to 50%
- **Buffered I/O**: Intelligent buffering minimizes disk I/O overhead and improves throughput
- **Performance Monitoring**: Real-time progress tracking with detailed performance metrics and ETA calculations

### Performance Benchmarks

Typical performance on modern hardware with parallel processing enabled:

| File Count | Estimated Time | Files/Second | Memory Usage | Improvement |
|------------|---------------|--------------|--------------|-------------|
| 1,000      | 1-2 seconds   | 500-1,500    | Low          | ~2x faster  |
| 10,000     | 5-10 seconds  | 1,000-3,000  | Moderate     | ~2x faster  |
| 100,000    | 30-60 seconds | 1,500-4,000  | Optimized    | ~2x faster  |

*Performance varies based on hardware, file type, and options selected. Parallel processing provides up to 3x improvement over single-threaded generation.*

### Real-time Performance Monitoring

During file generation, you'll see detailed progress updates:

```
Starting parallel file generation...
  File Type: pdf
  Count: 50,000
  Worker Threads: 8 (auto-detected)
  Batch Size: 1000

Progress: 25,000 / 50,000 files (50.0%) - 1,250.5 files/sec - ETA: 00:00:20
Memory Usage: 45.2 MB | GC Collections: Gen0=142, Gen1=8, Gen2=1

Generation complete in 40.2 seconds.
  Performance: 1,243.8 files/second
  Memory Efficiency: 98.5% (low GC pressure)
```

### Automatic Performance Optimization

The system automatically:
- Detects and optimizes for available CPU cores
- Manages memory efficiently to handle large file counts without excessive allocations
- Provides detailed throughput metrics and time estimates
- Balances parallelization with memory usage for optimal performance

## Versioning

The application's version will follow a scheme of the format `MAJOR.MINOR.BUILD`.
- The `MAJOR.MINOR` version numbers are to be managed manually in a `.version` file located in the root of the repository.
- The `BUILD` number will be automatically generated by the CI/CD pipeline, using the GitHub Actions `run_number`. The full version string will be in the format `<major>.<minor>.<run_number>`.
- The CI/CD pipeline will automatically create a new GitHub Release for each successful `master` branch build.

## Testing

The project includes a comprehensive test suite that covers all command-line options and performance characteristics. The test suite is designed to be run on Windows, macOS, and Linux.

### Running the Tests

To run the tests, execute the appropriate script for your operating system:

-   **Windows**: `tests\run-tests.bat`
-   **macOS and Linux**: `./tests/run-tests.sh`

### Performance Testing

The project includes comprehensive performance regression testing to ensure optimal performance:

#### Performance Regression Tests
```bash
# Linux/macOS
./tests/test-performance-regression.sh

# Windows
tests/test-performance-regression.bat
```

#### Performance Features
- **Micro-benchmarks**: BenchmarkDotNet-based performance analysis of all components
- **Regression Testing**: Automated detection of performance degradation
- **Memory Monitoring**: GC pressure and allocation tracking
- **Throughput Analysis**: Files per second and data processing metrics
- **Cross-Platform**: Performance testing on Windows, Linux, and macOS

#### Performance Targets
- **Small Dataset** (100 files): < 2 seconds
- **Medium Dataset** (1,000 files): < 10 seconds
- **Large Dataset** (10,000 files): < 60 seconds
- **Memory Efficiency**: < 500MB peak usage for large datasets
- **Throughput**: 50+ files per second minimum

### Stress Testing

For extreme performance testing and edge case validation, see the [stress test suite](tests/stress/README.md). These tests are designed for manual execution only and test system limits under extreme conditions:

- **10GB File Count Challenge**: Tests maximum file handling (5M files)
- **30GB Attachment-Heavy EML**: Tests attachment processing and large archives
- **Large Load File Performance**: Tests metadata and text extraction performance

⚠️ **Warning**: Stress tests consume significant system resources and require manual confirmation before execution.

### Pre-Commit Hook

The project includes scripts to set up a pre-commit hook that will run the test suite automatically before each commit. To set up the hook, run the appropriate script for your operating system:

-   **Windows**: `setup-hook.bat`
-   **macOS and Linux**: `setup-hook.sh`
