# EML Generation Refactoring Plan

## Objective
Consolidate the separate EML generation logic into the main `ParallelFileGenerator` workflow to improve code reuse, maintainability, and performance consistency.

## Analysis
- **`EmlFile.cs`**: Contains the core logic for creating EML file content, including headers, body, and attachments. This is well-structured and can be reused.
- **`Program.cs`**: Contains two separate generation methods: `GenerateFiles` (for generic file types) and `GenerateEmlFiles` (specifically for EMLs). This leads to code duplication, especially in areas like archive creation, load file generation, and parallel processing.
- **`ParallelFileGenerator.cs`**: A robust, parallelized file generation engine that is currently used by `GenerateFiles` but not `GenerateEmlFiles`. It handles concurrency, memory management, and progress reporting.
- **`PlaceholderFiles.cs`**: Provides placeholder content for various file types and attachments.

## Refactoring Plan

The core idea is to eliminate the `GenerateEmlFiles` method and integrate its functionality directly into the more advanced `ParallelFileGenerator`.

### TODO List

- [ ] **1. Create `EmlGenerator.cs`:**
    -    Move the static `CreateEmlContent` method from the old `EmlFile.cs` into a new `Zipper/EmlGenerator.cs` class. This isolates EML-specific content creation.
    -    Delete the now-redundant `EmlFile.cs`.

- [ ] **2. Integrate EML Logic into `ParallelFileGenerator.cs`:**
    -    Modify `GenerateFileData` to handle the "eml" file type.
    -    Instead of using a single `placeholderContent`, it will call `EmlGenerator.CreateEmlContent` to dynamically generate the EML file bytes.
    -    This will involve generating random email metadata (To, From, Subject, etc.) for each file.
    -    Handle attachments within this method by calling `PlaceholderFiles.GetRandomAttachment()` based on the `attachmentRate`.

- [ ] **3. Unify Load File Logic in `ParallelFileGenerator.cs`:**
    -    Update the `WriteLoadFileContent` method to handle the specific columns required for EML files (`To`, `From`, `Subject`, `Attachment`, etc.).
    -    Use conditional logic based on `request.FileType` to add the correct headers and data columns.

- [ ] **4. Refactor `Program.cs`:**
    -    Remove the entire `GenerateEmlFiles` method.
    -    In the `Main` method, update the logic to call the generic `GenerateFiles` method for all file types, including "eml".
    -    The `if (fileType.ToLower() == "eml")` block will be removed.

- [ ] **5. Update and Verify Tests:**
    -    Review and update all test scripts that currently invoke the CLI for EML generation (`run-tests.sh`, `test-eml-comprehensive.sh`, etc.).
    -    Ensure that the command-line arguments and expected outputs (load file headers, file counts) are adjusted to match the new unified implementation.
    -    Run all tests to confirm that the refactored code produces the same output and passes all checks.

- [ ] **6. Update Documentation:**
    -    Update `README.md` and `AGENTS.md` to reflect the refactored architecture.
    -    Add a note about the use of the Single Responsibility Principle (e.g., `EmlGenerator.cs`) as a pattern for future development when adding new complex file types.

## Design Decisions
1.  **Performance**: There are no special performance requirements for EML generation. The existing `ParallelFileGenerator` is sufficient and will provide a performance improvement over the previous implementation.
2.  **Load File Structure**: The load file for EML generation will be an extension of the base load file. The standard columns will always be present, and the EML-specific columns (To, From, Subject, etc.) will be appended only when `--type eml` is specified.
