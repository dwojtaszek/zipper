# Zipper: A Test Data Generation Tool 

Zipper is a .NET command-line tool for generating large zip files containing placeholder documents (`.pdf`, `.jpg`, or `.tiff`) and a corresponding Relativity One load file. It's designed for performance testing and can generate archives with up to 100 million files.

## Features

-   Generates a single `.zip` archive with a specified number of files.
-   Supports multiple file distribution patterns: proportional, gaussian, and exponential.
-   Creates a corresponding `.dat` load file compatible with Relativity One.
-   Uses minimal, valid placeholder files for maximum compression.
-   Streams data directly to the archive to handle very large datasets efficiently.
-   Provides progress indication during generation.
-   Can target a specific zip file size by padding files with non-compressible data.

## Requirements

-   .NET 8.0 SDK (or newer)

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
zipper --type <filetype> --count <number> --output-path <directory> [--folders <number>] [--encoding <UTF-8|UTF-16|ANSI>] [--distribution <proportional|gaussian|exponential>] [--with-metadata] [--with-text] [--attachment-rate <number>] [--target-zip-size <size>]
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
-   `--with-metadata`: **(Optional)** Generates a load file with additional metadata columns (Custodian, Date Sent, Author, File Size).
-   `--with-text`: **(Optional)** Generates a corresponding extracted text file for each document and adds the path to the load file.
-   `--attachment-rate <number>`: **(Optional)** When type is `eml`, specifies the percentage of emails (0-100) that will receive a random document as an attachment. Defaults to 0.
-   `--target-zip-size <size>`: **(Optional, Requires --count)** Specifies a target size for the final zip file (e.g., 500MB, 10GB). This feature works by padding each of the `--count` files with uncompressible data to meet the target size. This significantly reduces the overall compression ratio and is intended for specific network or storage performance testing scenarios. This mode overrides the `--variable-sizes` flag.

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

This command will produce two files in the `test_data` directory:
-   `archive_YYYYMMDD_HHMMSS.zip`: A zip file containing 50,000 PDFs distributed across 10 folders.
-   `archive_YYYYMMDD_HHMMSS.dat`: The load file pointing to the documents within the archive.

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

# Generates exactly 100,000 PDF files and pads each one with uncompressible
# data so that the final compressed zip archive is approximately 1GB in size.
zipper --type pdf --count 100000 --target-zip-size 1GB --output-path ./test_padded_files
```

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