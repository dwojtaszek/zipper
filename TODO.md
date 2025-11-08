# TODO List

## EML Generation Bug (Test Case 16)

**Issue**: EML generation bug with Test Case 16 (attachment + metadata + text combination) that causes "Entries cannot be created while previously created entries are still open" error.

**Details**:
- Test Case 16: EML attachments with metadata and text
- Parameters: --type eml --count 15 --attachment-rate 60 --with-metadata --with-text
- Error: "Entries cannot be created while previously created entries are still open"
- Result: No .dat file is generated, causing test verification to fail

**Root Cause Analysis**:
- The issue occurs specifically when EML files have BOTH attachments AND text extraction enabled
- This creates 4 ZIP entries per file: main EML, attachment file, text file for main EML, text file for attachment
- ZipArchive doesn't support concurrent entry creation, and the async/await patterns cause conflicts
- Isolated issue: EML files work fine with just attachments OR just text, but not both together

**Fix Applied**:
1. **Sequential Processing**: Force concurrency=1 for EML files with attachments or text extraction
2. **Synchronous Write Methods**: Added synchronous versions of ZIP write methods for use within lock
3. **Atomic Entry Creation**: Use lock (_zipEntryLock) to ensure all entries for one file are created atomically

**Status**: Failed Fix Attempt ❌
**Priority**: High
**Impact**: Affects EML functionality when combining attachments with metadata and text extraction

**Failed Resolution Attempts**:
1. **Sequential Processing**: Force concurrency=1 for EML files with attachments or text extraction
2. **Synchronous Write Methods**: Added synchronous versions of ZIP write methods for use within lock
3. **Atomic Entry Creation**: Use lock (_zipEntryLock) to ensure all entries for one file are created atomically

**Current Issue**:
- Local testing still fails with "Entries cannot be created while previously created entries are still open" error
- Even with explicit --concurrency 1, the deadlock persists
- GitHub workflow shows deadlock behavior, indicating the fix is not working

**Problem Analysis**:
- The issue appears to be deeper than just concurrency control
- The async/await pattern within the ZIP entry creation loop may be causing issues
- Multiple ZIP entries per EML file (main EML, attachment, text files) create complex entry dependencies

**Next Steps Required**:
- Investigate the async/await pattern in ZIP archive creation
- Consider completely synchronous ZIP creation for EML files with complex scenarios
- Examine if ZipArchive has internal state management issues with nested entry creation

**Files Modified**:
- `Zipper/ZipArchiveService.cs`: Added lock and synchronous write methods
- `Zipper/ParallelFileGenerator.cs`: Force sequential processing for problematic EML scenarios

**Next Steps**: Debugging required - Current fix approach not working

**Current Status**:
- ❌ Sequential processing + lock approach failed
- ❌ Synchronous write methods within lock still causing deadlock
- ❌ Local and GitHub workflows both failing on Test Case 16
- ✅ All other EML tests (10-15) pass successfully
- ✅ Root cause identified: ZIP entry creation conflicts

**Debugging Focus**: Need to investigate ZipArchive internal behavior and async/await patterns in ZIP entry creation for complex multi-entry scenarios.