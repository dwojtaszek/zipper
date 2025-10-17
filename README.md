# Zipper: A Test Data Generation Tool 

Zipper is a .NET command-line tool for generating large zip files containing placeholder documents (`.pdf`, `.jpg`, or `.tiff`) and a corresponding load file. It's designed for performance testing and can generate archives with up to 100 million files.

## Features

-   Generates a single `.zip` archive with a specified number of files.
-   Supports multiple file distribution patterns: proportional, gaussian, and exponential.
-   Creates a corresponding `.dat` load file compatible with standard import tools.
-   Uses minimal, valid placeholder files for maximum compression.
-   Streams data directly to the archive to handle very large datasets efficiently.
-   Provides progress indication during generation with real-time performance metrics.
-   Can target a specific zip file size by padding files with non-compressible data.
-   Optimized for high-performance parallel processing with memory pooling and buffered I/O.

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

Zipper is optimized for high-performance file generation with the following features:

- **Parallel Processing**: Generates multiple files simultaneously using controlled concurrency
- **Memory Pooling**: Reduces garbage collection pressure through efficient memory reuse
- **Buffered I/O**: Minimizes disk I/O overhead with intelligent buffering
- **Progress Monitoring**: Real-time progress reporting with performance metrics

### Performance Benchmarks

Typical performance on modern hardware:

| File Count | Estimated Time | Files/Second | Memory Usage |
|------------|---------------|--------------|--------------|
| 1,000      | 1-3 seconds   | 300-1,000    | Low          |
| 10,000     | 5-15 seconds  | 600-2,000    | Moderate     |
| 100,000    | 45-120 seconds| 800-2,200    | Optimized    |

*Performance varies based on hardware, file type, and options selected.*

### Performance Features

During file generation, you'll see real-time progress updates:

```
Progress: 25,000 / 50,000 files (50.0%) - 1,250.5 files/sec - ETA: 00:00:20
```

The system automatically:
- Optimizes concurrency based on your CPU core count
- Manages memory efficiently to handle large file counts
- Provides throughput metrics and time estimates

## Versioning

The application's version will follow a scheme of the format `MAJOR.MINOR.BUILD`.
- The `MAJOR.MINOR` version numbers are to be managed manually in a `.version` file located in the root of the repository.
- The `BUILD` number will be automatically generated by the CI/CD pipeline, using the GitHub Actions `run_number`. The full version string will be in the format `<major>.<minor>.<run_number>`.
- The CI/CD pipeline will automatically create a new GitHub Release for each successful `master` branch build.

## Testing

The project includes a test suite that covers all command-line options. The test suite is designed to be run on Windows, macOS, and Linux.

### Running the Tests

To run the tests, execute the appropriate script for your operating system:

-   **Windows**: `tests\run-tests.bat`
-   **macOS and Linux**: `./tests/run-tests.sh`

### Pre-Commit Hook

The project includes scripts to set up a pre-commit hook that will run the test suite automatically before each commit. To set up the hook, run the appropriate script for your operating system:

-   **Windows**: `setup-hook.bat`
-   **macOS and Linux**: `setup-hook.sh`