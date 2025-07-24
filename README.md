# Zipper: A Test Data Generation Tool for Relativity

Zipper is a .NET command-line tool for generating large zip files containing placeholder documents (`.pdf`, `.jpg`, or `.tiff`) and a corresponding Relativity One load file. It's designed for performance testing and can generate archives with up to 100 million files.

## Features

-   Generates a single `.zip` archive with a specified number of files.
-   Includes a new cosmetic change for testing purposes.
-   Creates a corresponding `.dat` load file compatible with Relativity One.
-   Uses minimal, valid placeholder files for maximum compression.
-   Streams data directly to the archive to handle very large datasets efficiently.
-   Provides progress indication during generation.

## Requirements

-   .NET 8.0 SDK (or newer)

## Usage

You can run the application using the `dotnet run` command from the project directory.

### Syntax

```bash
dotnet run -- --type <filetype> --count <number> --output-path <directory>
```

### Arguments

-   `--type <pdf|jpg|tiff>`: **(Required)** The type of file to generate.
-   `--count <number>`: **(Required)** The total number of files to generate.
-   `--output-path <directory>`: **(Required)** The directory where the output `.zip` and `.dat` files will be saved. The directory will be created if it doesn't exist.
-   `--folders <number>`: **(Optional)** The number of folders to distribute files into. Defaults to 1. Must be between 1 and 100.
-   `--encoding <UTF-8|UTF-16|ANSI>`: **(Optional)** The text encoding for the load file. Defaults to `UTF-8`. `ANSI` uses the Windows-1252 code page.

### Example

To generate a zip file containing 50,000 PDF files distributed across 10 folders, with an ANSI-encoded load file, and save it to a directory named `test_data`:

```bash
dotnet run -- --type pdf --count 50000 --output-path ./test_data --folders 10 --encoding ANSI
```

This will produce two files in the `test_data` directory:
-   `archive_YYYYMMDD_HHMMSS.zip`: A zip file containing 50,000 PDFs distributed across 10 folders.
-   `archive_YYYYMMDD_HHMMSS.dat`: The Relativity load file pointing to the documents within the archive.
