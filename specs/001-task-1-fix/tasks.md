# Tasks: Fix Incomplete EML Feature Interaction

**Input**: Design documents from `/specs/001-task-1-fix/`
**Prerequisites**: plan.md, research.md, data-model.md, quickstart.md

## Feature Summary
Fix the incomplete feature interaction where `--with-metadata` and `--with-text` flags are ignored when using `--type eml`. Update the `GenerateEmlFiles` method in `Program.cs` to accept and handle these flags consistently with other file types, with comprehensive cross-platform testing per Constitution v1.4.0.

## Path Conventions
- **Primary Target**: `Zipper/Program.cs` - Main implementation changes
- **Supporting Files**: `Zipper/PlaceholderFiles.cs` - Email-specific text content
- **Testing**: `tests/` directory - Consolidated cross-platform test suite

## Phase 1: Setup & Baseline

### T001 [P] ✅ Establish Current EML Baseline
**File**: `tests/baseline-eml-test.txt`
**Description**: Document current EML generation behavior by running baseline tests and capturing expected output format for regression prevention
**Commands**:
```bash
dotnet run --project Zipper/Zipper.csproj -- --type eml --count 5 --output-path ./baseline-test
```
**Output**: Documented baseline showing current 7-column structure without metadata/text support

### T002 [P] ✅ Create Email-Specific Text Content Constant
**File**: `Zipper/PlaceholderFiles.cs`
**Description**: Add email-specific extracted text content constant to be used for EML text file generation
**Implementation**: Add `public static readonly byte[] EmlExtractedText = System.Text.Encoding.UTF8.GetBytes("This is extracted text from email body.");`

## Phase 2: Method Signature & Core Structure

### T003 ✅ Update GenerateEmlFiles Method Signature
**File**: `Zipper/Program.cs`
**Description**: Add `withMetadata` and `withText` parameters to the `GenerateEmlFiles` method signature
**Current**: `static async Task GenerateEmlFiles(long count, DirectoryInfo outputDir, int numFolders, Encoding encoding, DistributionType distributionType, int attachmentRate, bool includeLoadFile)`
**Updated**: Add `bool withMetadata, bool withText` parameters after `attachmentRate`

### T004 ✅ Update Main Method Parameter Passing
**File**: `Zipper/Program.cs` (line ~125)
**Description**: Update the call to `GenerateEmlFiles` in the main method to pass the `withMetadata` and `withText` flags
**Current**: `await GenerateEmlFiles(count.Value, outputPath, folders, encoding, distributionType.Value, attachmentRate, includeLoadFile);`
**Updated**: Pass `withMetadata` and `withText` parameters from CLI parsing

### T005 ✅ Implement Dynamic Header Generation
**File**: `Zipper/Program.cs` (GenerateEmlFiles method, line ~401)
**Description**: Replace the hardcoded header string with dynamic generation based on `withMetadata` and `withText` flags
**Logic**: Base columns + conditional metadata columns + conditional text column following the specified order

## Phase 3: Metadata Support Implementation

### T006 ✅ Implement Metadata Column Logic
**File**: `Zipper/Program.cs` (GenerateEmlFiles method)
**Description**: Add conditional logic to include metadata columns (Custodian, Author, Date Sent, File Size) when `withMetadata` is true
**Reuse**: Leverage existing `GetRandomAuthor()` and `GetRandomDate()` functions from the `GenerateFiles` method
**Column Order**: Control Number, File Path, To, From, Subject, Custodian, Author, Sent Date, Date Sent, File Size, Attachment

### T007 ✅ Implement File Size Calculation for EML
**File**: `Zipper/Program.cs` (GenerateEmlFiles method)
**Description**: Calculate actual EML file size including attachments for the File Size metadata field
**Implementation**: Measure `emlContent.Length` after EML generation to get accurate byte count

### T008 ✅ Update EML Data Row Generation
**File**: `Zipper/Program.cs` (GenerateEmlFiles method, line ~438)
**Description**: Modify the load file line generation to conditionally include metadata fields when `withMetadata` is enabled
**Format**: Follow same delimiter and quote character pattern as existing code

## Phase 4: Text File Support Implementation

### T009 ✅ Implement Text File Generation Logic
**File**: `Zipper/Program.cs` (GenerateEmlFiles method)
**Description**: Add conditional logic to generate `.txt` files when `withText` is enabled, using email-specific placeholder content
**Pattern**: Follow the same approach as in `GenerateFiles` method for creating text entries in the ZIP archive
**Content**: Use `PlaceholderFiles.EmlExtractedText` from T002

### T010 ✅ Add Extracted Text Column Support
**File**: `Zipper/Program.cs` (GenerateEmlFiles method)
**Description**: Conditionally add Extracted Text column to header and data rows when `withText` is enabled
**Path Format**: `folder_###/########.txt` matching the corresponding EML file numbering

## Phase 5: Comprehensive Cross-Platform Testing (CONSTITUTIONAL REQUIREMENT)

### T011 ✅ Create Comprehensive EML Test Suite - Windows Version
**Files**: `tests/test-eml-comprehensive.bat` (new)
**Description**: Create comprehensive Windows batch script testing ALL EML functionality scenarios in a single test file
**Constitutional Compliance**: Per Constitution v1.4.0, Principles III & VI - CRITICAL dual platform requirement
**Scenarios Covered**:
- Basic EML generation (regression baseline)
- EML with metadata only (11 columns)
- EML with text only (8 columns)
- EML with combined metadata and text (12 columns)
- EML with attachments and full flags
- Performance validation with large dataset
**Validation**: Each scenario must verify file counts, column structure, content correctness
**Commands**: Use Windows cmd.exe compatible syntax with proper path separators

### T012 ✅ Create Comprehensive EML Test Suite - Unix Version
**Files**: `tests/test-eml-comprehensive.sh` (new)
**Description**: Create comprehensive Unix shell script testing ALL EML functionality scenarios - IDENTICAL to T011 but for Linux/macOS
**Constitutional Compliance**: Per Constitution v1.4.0, Principles III & VI - CRITICAL dual platform requirement
**Scenarios Covered**: IDENTICAL to T011 - all scenarios must produce identical validation results
**Validation**: Each scenario must verify file counts, column structure, content correctness
**Commands**: Use bash shell compatible syntax with Unix path conventions
**Execution Permissions**: Must be executable on Linux/macOS systems

## Phase 6: Integration & Validation

### T013 ✅ Update Existing Test Suite Integration
**File**: `tests/run-tests.sh` and `tests/run-tests.bat` (existing)
**Description**: Update both existing test runners to include comprehensive EML test scenarios
**Windows Update**: Added EML test cases (10-13) to `run-tests.bat`
**Unix Update**: Added EML test cases (10-13) to `run-tests.sh`
**Validation**: Ensure EML tests are executed as part of full test suite with proper column validation

### T014 ✅ Update Documentation
**File**: `README.md`
**Description**: Update documentation to reflect that `--with-metadata` and `--with-text` flags now work with `--type eml`
**Content**: Added examples for EML with metadata, text, and combined scenarios with cross-platform testing information

## Task Dependencies

### Sequential Dependencies
- T001 → T011, T012 (baseline needed for comprehensive test regression validation)
- T002 → T009 (text constant needed for text file generation)
- T003 → T004 → T005 (method signature must be updated before calling and header generation)
- T006, T007 → T008 (metadata logic needed before row generation)
- T009 → T010 (text file logic needed before column addition)
- T005, T008, T010 → T011, T012 (core implementation needed before comprehensive testing)
- T011, T012 → T013 (comprehensive tests before integration into main test suite)

### Parallel Opportunities [P]
- T001, T002 can run simultaneously (different concerns)
- T006, T007 can be developed in parallel (independent metadata features)
- T009 can be developed in parallel with metadata tasks
- T011, T012 can be created simultaneously (same logic, different platforms)
- T013, T014 can run in parallel (test integration and documentation)

## Constitutional Compliance Verification

### Constitution v1.4.0 Requirements Met:
- **Principle III**: ✅ Comprehensive test suite implemented in both .bat and .sh formats
- **Principle VI**: ✅ Cross-platform compatibility with mandatory dual testing
- **All Tests**: ✅ Both platforms must pass before deployment
- **CI/CD Ready**: ✅ Tests designed for both Windows and Linux agents

### Cross-Platform Testing Validation:
- **T011 + T012**: Identical test scenarios with platform-specific implementations
- **Dual Validation**: Both Windows and Unix tests must produce identical results
- **Path Handling**: Tests account for different path separator conventions
- **File Permissions**: Unix version includes proper execution permissions

## Execution Examples

### Sequential Implementation
```bash
# Phase 1: Setup
./task T001 && ./task T002

# Phase 2: Core Changes (must be sequential)
./task T003 && ./task T004 && ./task T005

# Phase 3: Metadata (can be parallel)
./task T006 & ./task T007 & wait
./task T008

# Phase 4: Text Support (can be parallel with metadata)
./task T009 & ./task T010

# Phase 5: Constitutional Compliance Testing (can be parallel)
./task T011 & ./task T012 & wait

# Phase 6: Final integration (can be parallel)
./task T013 & ./task T014 & wait
```

### Cross-Platform Testing Validation
```bash
# Windows validation
tests\test-eml-comprehensive.bat

# Unix validation  
./tests/test-eml-comprehensive.sh

# Both must pass for constitutional compliance
```

## Expected Outcomes

### Code Changes
- `GenerateEmlFiles` method signature updated with 2 new parameters
- Dynamic header generation supporting 7-12 columns based on flags
- Conditional metadata and text file generation logic
- Consistent column ordering and formatting
- Reuse of existing helper functions for metadata generation

### Constitutional Compliance
- **Single comprehensive test suite** covering all scenarios
- **Dual platform implementations** (.bat and .sh) with identical validation logic
- **Cross-platform CI/CD ready** - tests work on both Windows and Linux agents
- **Reduced maintenance overhead** - single test logic maintained in two platform formats

### Documentation Updates
- README.md updated with EML flag examples and cross-platform testing information
- Clear documentation of comprehensive test suite approach

## Success Criteria
- [ ] All 14 tasks completed successfully (reduced from 18 through consolidation)
- [ ] EML generation supports --with-metadata and --with-text flags
- [ ] Column ordering matches specification (12 columns max with correct sequence)
- [ ] No regression in existing EML functionality
- [ ] **CONSTITUTIONAL COMPLIANCE**: Both `test-eml-comprehensive.bat` and `test-eml-comprehensive.sh` pass
- [ ] **CROSS-PLATFORM VALIDATION**: Identical test results on Windows and Linux/macOS
- [ ] Performance remains acceptable with new features
- [ ] Documentation accurately reflects new capabilities and testing approach