# Zipper Application Requirements

## 1. Core Purpose

The `zipper` application is a .NET Core command-line tool designed to generate large, highly compressed `.zip` files containing a specified number of placeholder files. It also generates a corresponding load file compatible with import specifications.

## 2. Functional Requirements

- **File Generation**: The application must generate a user-specified number of files (up to 100 million) of a single type (`pdf`, `jpg`, or `tiff`) for each run.
- **Internal File Content**: The content for the generated files must be minimal, valid, and identical for every file within the archive to ensure maximum compression. The application will provide these file contents internally; the user does not need to supply template files.
- **Archive Format**: The output must be a **single `.zip` file**. The compression level should be equivalent to the standard "best compression" (DEFLATE) method used by Windows and macOS. The system must be capable of handling archives exceeding the standard 4GB/65k file limits (Zip64).
- **Load File**: The application must generate a **single load file** (`.dat`) corresponding to the zip archive.
    - The load file format must adhere to specifications.
    - It will use standard delimiters (e.g., ASCII 020 for columns).
    - It must be saved with a user-specifiable encoding (defaulting to UTF-8).
    - It will contain at least two columns: a unique `Control Number` for each document and the `Native File Path` pointing to the file's location within the zip archive.
- **Path Structure**: File paths within the load file should be relative to the zip archive's root.
- **Folder Distribution**: Files can be distributed across a specified number of folders within the archive using different distribution patterns. The default is 1 folder, with a maximum of 100. Three distribution patterns are supported:
    - **Proportional**: Even distribution across all folders (round-robin assignment)
    - **Gaussian**: Bell curve distribution with most files concentrated in middle folders
    - **Exponential**: Exponential decay distribution with most files concentrated in the first few folders

## 3. Technical Requirements

- **Framework**: .NET 8.
- **Interface**: Command-line application.
- **Dependencies**: `System.Text.Encoding.CodePages`.
- **Performance**: The application must be designed to stream data directly to the archive without storing intermediate files on disk, minimizing memory and disk space usage.

## 4. Command-Line Arguments

- `--type <pdf|jpg|tiff>`: (Required) The type of file to generate.
- `--count <number>`: (Required) The total number of files to generate.
- `--output-path <directory>`: (Required) The directory where the output `.zip` and `.dat` files will be saved.
- `--folders <number>`: (Optional) The number of folders to distribute files into. Defaults to 1. Must be between 1 and 100.
- `--encoding <UTF-8|UTF-16|ANSI>`: (Optional) The text encoding for the load file. Defaults to UTF-8. `ANSI` corresponds to the Windows-1252 code page.
- `--distribution <proportional|gaussian|exponential>`: (Optional) The distribution pattern for files across folders. Defaults to `proportional`.
