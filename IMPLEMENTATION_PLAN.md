# Implementation Plan: File Distribution Patterns

## Overview
Add a new optional command-line flag `--distribution` to control how files are distributed across folders with three patterns: proportional (default), gaussian, and exponential. The implementation will be contained in a dedicated helper file to maintain separation of concerns.

## Task Description
Implement file distribution patterns for the Zipper tool to provide more realistic test data generation scenarios. This enhancement will allow users to simulate different document distribution patterns that might be found in real-world scenarios.

## Files to Create

### 1. `FileDistributionHelper.cs`
**Purpose**: Encapsulate all file distribution logic and algorithms.

**Key Components**:
- **Enum `DistributionType`**: Define the three distribution types (Proportional, Gaussian, Exponential)
- **Static class `FileDistributionHelper`**: Main helper class containing distribution algorithms
- **Method `GetFolderNumber()`**: Primary method that takes current file index, total count, number of folders, and distribution type to return the target folder number
- **Private helper methods**: Individual algorithm implementations for each distribution type

**Core Functionality**:
- **Proportional Distribution**: Round-robin assignment (current implementation)
- **Gaussian Distribution**: Bell curve distribution with most files in middle folders
- **Exponential Distribution**: Exponential decay with most files in first few folders

## Files to Modify

### 1. `Program.cs`
**Changes Required**:
- Add new command-line argument parsing for `--distribution` flag
- Add validation for distribution type parameter
- Update help/usage text to include new parameter
- Pass distribution type to the file generation logic
- Modify the folder assignment logic in `GenerateFiles()` method to use `FileDistributionHelper`

**Specific Areas**:
- **Line ~30**: Add new case in switch statement for `--distribution` argument
- **Line ~40**: Add validation for distribution type (similar to encoding validation)
- **Line ~35**: Update error message to include new parameter
- **Line ~75**: Update console output to show selected distribution type
- **Line ~105**: Replace current folder assignment logic with helper method call

### 2. `README.md`
**Changes Required**:
- Add `--distribution` parameter to syntax section
- Add parameter description in arguments section
- Update example command to demonstrate new functionality
- Update description to mention distribution patterns feature

**Specific Sections**:
- **Syntax section**: Update command template
- **Arguments section**: Add distribution parameter documentation
- **Example section**: Show usage with different distribution patterns

## Implementation Strategy

### Phase 1: Create Distribution Helper
1. Create `FileDistributionHelper.cs` with enum and base structure
2. Implement proportional distribution (replicate current logic)
3. Implement gaussian distribution using mathematical formula
4. Implement exponential distribution using decay function
5. Add comprehensive input validation and error handling

### Phase 2: Integrate with Main Program
1. Add command-line argument parsing for distribution flag
2. Update validation logic to handle new parameter
3. Modify file generation loop to use distribution helper
4. Update console output and help text

### Phase 3: Update Documentation
1. Update README.md with new parameter information
2. Add examples demonstrating different distribution patterns
3. Update syntax and usage sections

## Technical Requirements

### Distribution Algorithms

**Gaussian Distribution**:
- Use normal distribution curve centered around middle folders
- Apply appropriate scaling to ensure all folders are used
- Handle edge cases for small folder counts

**Exponential Distribution**:
- Use exponential decay function starting from folder 1
- Ensure sufficient distribution across folders while maintaining exponential characteristic
- Apply minimum threshold to prevent empty folders

**Input Validation**:
- Validate distribution type against enum values
- Provide case-insensitive matching
- Default to proportional if not specified

### Performance Considerations
- All distribution calculations should be O(1) per file
- Pre-calculate distribution parameters where possible
- Avoid expensive mathematical operations in tight loops

## Testing Strategy

### Manual Testing Commands
```bash
# Test proportional (default)
dotnet run -- --type pdf --count 1000 --output-path ./test --folders 10

# Test gaussian distribution
dotnet run -- --type pdf --count 1000 --output-path ./test --folders 10 --distribution gaussian

# Test exponential distribution
dotnet run -- --type pdf --count 1000 --output-path ./test --folders 10 --distribution exponential

# Test case insensitivity
dotnet run -- --type pdf --count 1000 --output-path ./test --folders 10 --distribution GAUSSIAN
```

### Validation Tests
- Test with various folder counts (1, 5, 10, 50, 100)
- Test with different file counts (small: 100, medium: 10000, large: 100000)
- Verify folder distribution matches expected patterns
- Test error handling for invalid distribution types

## Backward Compatibility
- New parameter is optional with proportional as default
- Existing commands will continue to work unchanged
- No breaking changes to current functionality

## Error Handling
- Invalid distribution type should show clear error message
- Suggest valid options (proportional, gaussian, exponential)
- Maintain existing error handling patterns for consistency

## Success Criteria
1. All three distribution patterns work correctly
2. Command-line interface properly validates input
3. Documentation is updated and accurate
4. Backward compatibility is maintained
5. Performance impact is minimal
6. Code is well-organized in separate helper file

## Dependencies
- No new external dependencies required
- Uses existing .NET 8.0 mathematical functions
- Maintains current project structure and patterns
