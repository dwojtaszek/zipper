# Quickstart: EML Metadata and Text Support Testing

## Overview
This quickstart guide provides step-by-step validation scenarios to test the enhanced EML file generation with `--with-metadata` and `--with-text` flag support.

## Prerequisites
- Zipper application built and available
- Write access to a test directory
- Command line access (Windows/Linux/macOS)
- **CRITICAL**: Tests must pass on both Windows and Unix platforms

## Test Scenarios

### Scenario 1: Basic EML Generation (Baseline)
**Purpose**: Verify existing EML functionality remains unchanged

```bash
# Generate basic EML files
./zipper --type eml --count 10 --output-path ./test-output --folders 2

# Expected Output:
# - archive_YYYYMMDD_HHMMSS.zip created
# - archive_YYYYMMDD_HHMMSS.dat created
# - ZIP contains folder_001/ and folder_002/ with .eml files
# - DAT file has columns: Control Number, File Path, To, From, Subject, Sent Date, Attachment
```

**Validation Steps**:
1. Check ZIP archive contains 10 .eml files distributed across 2 folders
2. Verify .dat file has exactly 7 columns in correct order
3. Ensure no .txt files are present
4. Confirm header row matches expected format

### Scenario 2: EML with Metadata Only
**Purpose**: Test `--with-metadata` flag integration

```bash
# Generate EML files with metadata
./zipper --type eml --count 5 --output-path ./test-output --folders 1 --with-metadata

# Expected Output:
# - DAT file now has 11 columns total
# - Additional columns: Custodian, Author, Date Sent, File Size
# - Column order: Control Number, File Path, To, From, Subject, Custodian, Author, Sent Date, Date Sent, File Size, Attachment
```

**Validation Steps**:
1. Verify .dat file has exactly 11 columns
2. Check Custodian field contains "Custodian 1" (single folder)
3. Confirm Author field contains values from existing author list
4. Validate Date Sent field uses YYYY-MM-DD format
5. Ensure File Size field contains numeric byte values
6. Verify no .txt files are generated

### Scenario 3: EML with Text Only
**Purpose**: Test `--with-text` flag integration

```bash
# Generate EML files with extracted text
./zipper --type eml --count 8 --output-path ./test-output --folders 3 --with-text

# Expected Output:
# - DAT file has 8 columns (base 7 + Extracted Text)
# - ZIP contains both .eml and .txt files
# - Column order: Control Number, File Path, To, From, Subject, Sent Date, Attachment, Extracted Text
```

**Validation Steps**:
1. Verify .dat file has exactly 8 columns
2. Check ZIP contains 8 .eml files and 8 .txt files
3. Confirm .txt files contain email-specific placeholder text
4. Verify Extracted Text column points to correct .txt file paths
5. Ensure .txt files are distributed to same folders as corresponding .eml files

### Scenario 4: EML with Both Metadata and Text
**Purpose**: Test combined `--with-metadata --with-text` functionality

```bash
# Generate EML files with both metadata and text
./zipper --type eml --count 12 --output-path ./test-output --folders 4 --with-metadata --with-text

# Expected Output:
# - DAT file has 12 columns total
# - ZIP contains both .eml and .txt files
# - Full column order: Control Number, File Path, To, From, Subject, Custodian, Author, Sent Date, Date Sent, File Size, Attachment, Extracted Text
```

**Validation Steps**:
1. Verify .dat file has exactly 12 columns in specified order
2. Check ZIP contains 12 .eml files and 12 .txt files across 4 folders
3. Confirm all metadata fields are populated correctly
4. Verify text files contain email-specific placeholder content
5. Ensure file distribution is consistent across file types

### Scenario 5: EML with Attachments and Full Flags
**Purpose**: Test interaction with attachment rate and all flags

```bash
# Generate EML files with attachments, metadata, and text
./zipper --type eml --count 20 --output-path ./test-output --folders 2 --with-metadata --with-text --attachment-rate 50

# Expected Output:
# - Approximately 50% of emails have attachments
# - File Size field reflects actual EML size including attachments
# - All functionality works together
```

**Validation Steps**:
1. Check approximately 10 emails (50%) have non-empty Attachment field
2. Verify File Size values are larger for emails with attachments
3. Confirm all other validations from Scenario 4 still pass
4. Ensure attachment presence doesn't affect .txt file generation

## Validation Checklist

### File Structure Validation
- [ ] ZIP archive created successfully
- [ ] .dat load file created (unless --include-load-file used)
- [ ] Correct number of folders created
- [ ] Files distributed according to specified algorithm
- [ ] .eml files have sequential naming (########.eml)
- [ ] .txt files have matching naming (########.txt) when enabled

### Load File Content Validation
- [ ] Header row contains correct column names in specified order
- [ ] Control Number format: DOC########
- [ ] File Path format: folder_###/########.eml
- [ ] Email fields (To, From, Subject) properly populated
- [ ] Sent Date in correct datetime format
- [ ] Custodian field pattern when metadata enabled
- [ ] Author field uses existing random values when metadata enabled
- [ ] Date Sent in YYYY-MM-DD format when metadata enabled
- [ ] File Size contains numeric byte values when metadata enabled
- [ ] Extracted Text paths correct when text enabled

### Content Validation
- [ ] .eml files contain valid email structure
- [ ] .txt files contain email-specific placeholder text
- [ ] File sizes match reported values in metadata
- [ ] Character encoding consistent (UTF-8)

### Backward Compatibility
- [ ] EML generation without flags produces identical output to previous version
- [ ] Existing command line arguments continue to work
- [ ] No regression in performance or functionality

## Error Scenarios

### Invalid Flag Combinations
Test that the application handles edge cases gracefully:

```bash
# These should work (no errors expected)
./zipper --type eml --count 1 --output-path ./test --with-metadata
./zipper --type eml --count 1 --output-path ./test --with-text  
./zipper --type eml --count 1 --output-path ./test --with-metadata --with-text

# Test with other file types (should continue working)
./zipper --type pdf --count 5 --output-path ./test --with-metadata --with-text
```

### Performance Verification
For large datasets, verify performance remains acceptable:

```bash
# Generate larger dataset to test performance
./zipper --type eml --count 10000 --output-path ./test --folders 10 --with-metadata --with-text --attachment-rate 25
```

## Cross-Platform Testing Requirements

### Comprehensive EML Test Suite

#### Windows Testing
```cmd
REM Windows batch script testing - comprehensive EML test suite
tests\test-eml-comprehensive.bat
```

#### Linux/macOS Testing
```bash
# Unix shell script testing - comprehensive EML test suite
./tests/test-eml-comprehensive.sh
```

The comprehensive test suite includes all scenarios:
- Basic EML generation (baseline/regression)
- EML with metadata only
- EML with text only
- EML with combined metadata and text flags
- EML with attachments and full flags
- Performance validation

### Platform-Specific Validation
- **Path Separators**: Ensure forward/backslash compatibility
- **Line Endings**: Verify CRLF (Windows) vs LF (Unix) handling
- **File Permissions**: Test script execution permissions on Unix
- **Character Encoding**: Validate UTF-8 consistency across platforms

## Success Criteria
All scenarios must pass validation checklists with:
- Correct file counts and structure **on both Windows and Unix platforms**
- Proper column ordering and content **verified cross-platform**
- No regression in existing functionality **on any supported platform**
- Consistent behavior across different flag combinations **regardless of OS**
- **MANDATORY**: Both `test-eml-comprehensive.bat` and `test-eml-comprehensive.sh` must pass before deployment