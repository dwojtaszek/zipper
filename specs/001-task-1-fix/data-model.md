# Data Model: Fix Incomplete EML Feature Interaction

## Overview
This document defines the data structures and relationships involved in extending EML file generation to support metadata and text extraction flags.

## Core Entities

### EmlFileParameters
**Purpose**: Encapsulates all parameters needed for EML file generation with enhanced metadata support
**Lifecycle**: Created from CLI arguments, passed to generation method

**Attributes**:
- `Count`: long - Number of EML files to generate
- `OutputDirectory`: DirectoryInfo - Target output location
- `FolderCount`: int - Number of distribution folders
- `Encoding`: Encoding - Text encoding for load file
- `DistributionType`: DistributionType - Algorithm for file distribution
- `AttachmentRate`: int - Percentage chance of attachments (0-100)
- `WithMetadata`: bool - Whether to include additional metadata columns
- `WithText`: bool - Whether to generate extracted text files
- `IncludeLoadFile`: bool - Whether to include load file in archive

**Validation Rules**:
- Count > 0
- AttachmentRate between 0-100
- OutputDirectory must be writable
- FolderCount between 1-100

### LoadFileEntry
**Purpose**: Represents a single row in the .dat load file for EML files
**Lifecycle**: Created for each generated EML file, written to load file

**Attributes**:
- `ControlNumber`: string - Unique document identifier (DOC########)
- `FilePath`: string - Path within zip archive
- `To`: string - Email recipient address
- `From`: string - Email sender address  
- `Subject`: string - Email subject line
- `Custodian`: string - Document custodian (if withMetadata)
- `Author`: string - Document author (if withMetadata)
- `SentDate`: DateTime - Email sent timestamp
- `DateSent`: string - Metadata date field (if withMetadata, YYYY-MM-DD format)
- `FileSize`: long - Actual EML file size in bytes (if withMetadata)
- `Attachment`: string - Attachment filename if present
- `ExtractedText`: string - Path to .txt file (if withText)

**Relationships**:
- Links to corresponding EML file in archive
- May link to corresponding .txt file if withText enabled
- Custodian correlates with folder distribution

### TextFileContent
**Purpose**: Represents extracted text content for EML files
**Lifecycle**: Created when withText flag is enabled

**Attributes**:
- `Content`: byte[] - UTF-8 encoded text content
- `FileName`: string - Name of .txt file (########.txt)
- `FilePath`: string - Path within zip archive

**Content Rules**:
- Uses email-specific placeholder: "This is extracted text from email body."
- Consistent UTF-8 encoding
- File naming follows existing pattern

## Data Flow

### Input Processing
1. CLI arguments parsed into EmlFileParameters
2. Validation applied to parameters
3. Parameters passed to GenerateEmlFiles method

### Generation Process
1. For each file (1 to Count):
   - Generate LoadFileEntry with base EML data
   - If withMetadata: Add metadata fields using existing generators
   - If withText: Create TextFileContent and update LoadFileEntry
   - Create EML file in archive
   - Create text file in archive (if enabled)
   - Write LoadFileEntry to .dat file

### Output Structure
```
archive_YYYYMMDD_HHMMSS.zip
├── folder_001/
│   ├── 00000001.eml
│   ├── 00000001.txt (if withText)
│   └── ...
├── folder_002/
│   └── ...
└── archive_YYYYMMDD_HHMMSS.dat (if includeLoadFile)
```

## Column Ordering Specification

### Base EML Columns
- Control Number
- File Path  
- To
- From
- Subject
- Sent Date
- Attachment

### With Metadata (--with-metadata)
- Control Number
- File Path
- To
- From
- Subject
- **Custodian** (added)
- **Author** (added)
- Sent Date
- **Date Sent** (added)
- **File Size** (added)
- Attachment

### With Text (--with-text)
- [Previous columns...]
- **Extracted Text** (added at end)

### Combined (--with-metadata --with-text)
- Control Number
- File Path
- To
- From
- Subject
- Custodian
- Author
- Sent Date
- Date Sent
- File Size
- Attachment
- Extracted Text

## Data Generation Rules

### Metadata Field Generation
- **Custodian**: "Custodian {folderNumber}" or "Custodian 1" if single folder
- **Author**: Random selection from existing Authors array
- **Date Sent**: Random date in YYYY-MM-DD format from same range as other file types
- **File Size**: Actual byte length of generated EML content including attachments

### Text File Generation
- **Content**: Static placeholder text specific to emails
- **Naming**: Sequential numbering matching EML files (########.txt)
- **Encoding**: UTF-8 for consistency

### Field Independence
- Author and From fields are generated independently
- Date Sent and Sent Date are generated independently
- This creates realistic variance for testing scenarios

## Consistency Requirements

### Delimiter Format
- Column delimiter: (char)20
- Quote character: (char)254
- Same as existing GenerateFiles method

### File Naming
- EML files: ########.eml
- Text files: ########.txt
- Sequential numbering starting from 1

### Distribution
- Uses existing FileDistributionHelper
- Same algorithms: proportional, gaussian, exponential
- Folder naming: folder_###

## Cross-Platform Compatibility

### File System Considerations
- **Path Handling**: Use System.IO.Path.Combine() for cross-platform paths
- **Directory Separators**: Let .NET handle platform-specific separators automatically
- **File Names**: Ensure all file names are valid on Windows, Linux, and macOS
- **Case Sensitivity**: Consider case-sensitive file systems (Linux/macOS) vs case-insensitive (Windows)

### Testing Platform Requirements
- **Consolidated Test Suite**: Single comprehensive test file covering all EML scenarios
- **Dual Script Implementation**: Both .sh and .bat versions of the comprehensive test
- **Output Validation**: Test validation logic must work identically across platforms
- **Archive Handling**: ZIP format handling must be consistent across platforms
- **Character Encoding**: UTF-8 encoding must produce identical results on all platforms

### Platform-Specific Validation Rules
- **Windows**: Test .bat script with cmd.exe and PowerShell compatibility
- **Linux/macOS**: Test .sh script with bash shell compatibility
- **Archive Paths**: Ensure ZIP internal paths use forward slashes (ZIP standard)
- **Line Endings**: .dat files must handle CRLF/LF consistently across platforms
- **Comprehensive Coverage**: Single test suite must validate all flag combinations on both platforms