# Zipper: A Test Data Generation Tool 

Zipper is a .NET command-line tool for generating large zip files containing placeholder documents (`.pdf`, `.jpg`, or `.tiff`) and a corresponding Relativity One load file. It's designed for performance testing and can generate archives with up to 100 million files.

## Features

-   Generates a single `.zip` archive with a specified number of files.
-   Supports multiple file distribution patterns: proportional, gaussian, and exponential.
-   Creates a corresponding `.dat` load file compatible with Relativity One.
-   Uses minimal, valid placeholder files for maximum compression.
-   Streams data directly to the archive to handle very large datasets efficiently.
-   Provides progress indication during generation.

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
zipper --type <filetype> --count <number> --output-path <directory> [--folders <number>] [--encoding <UTF-8|UTF-16|ANSI>] [--distribution <proportional|gaussian|exponential>] [--with-metadata] [--with-text]
```

### Arguments

-   `--type <pdf|jpg|tiff>`: **(Required)** The type of file to generate.
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

### Distribution Patterns

The following chart illustrates how files are distributed across folders using different distribution patterns:

![Distribution Patterns](assets/dist.png)

- **Proportional**: Files are distributed evenly across all folders in a round-robin fashion
- **Gaussian**: Files follow a bell curve distribution, with most files concentrated in the middle folders  
- **Exponential**: Files follow an exponential decay pattern, with the highest concentration in the first folders

### Example

To generate a zip file containing 50,000 PDF files distributed across 10 folders using a gaussian distribution pattern, with an ANSI-encoded load file, and save it to a directory named `test_data`:

```bash
zipper --type pdf --count 50000 --output-path ./test_data --folders 10 --encoding ANSI --distribution gaussian
```

#### Additional Examples

```bash
# Proportional distribution (default) - files evenly distributed
zipper --type pdf --count 10000 --output-path ./test --folders 5 --distribution proportional

# Gaussian distribution - bell curve with most files in middle folders
zipper --type jpg --count 25000 --output-path ./test --folders 20 --distribution gaussian

# Exponential distribution - most files in first few folders
zipper --type tiff --count 5000 --output-path ./test --folders 10 --distribution exponential

# With extracted text
zipper --type tiff --count 25000 --output-path ./test_data --with-text
```

This will produce two files in the `test_data` directory:
-   `archive_YYYYMMDD_HHMMSS.zip`: A zip file containing 50,000 PDFs distributed across 10 folders.
-   `archive_YYYYMMDD_HHMMSS.dat`: The  load file pointing to the documents within the archive.

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
