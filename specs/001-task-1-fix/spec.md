# Feature Specification: Fix Incomplete EML Feature Interaction

**Feature Branch**: `001-task-1-fix`  
**Created**: 2025-10-05  
**Status**: Draft  
**Input**: User description: "Task 1: Fix Incomplete Feature Interaction with --type eml"

## Clarifications

### Session 2025-10-05  
- Q: When `--with-text` is used with EML files, what should be the content of the generated `.txt` files? → A: Use email-specific placeholder text (e.g., "This is extracted text from email body.")
- Q: When both `--with-metadata` and `--with-text` are used with EML files, what should be the exact column order in the `.dat` file? → A: Interleave logically: Control Number, File Path, To, From, Subject, Custodian, Author, Sent Date, Date Sent, File Size, Attachment, Extracted Text
- Q: For the Author and Date Sent metadata fields, how should they align with the existing EML fields when both are present? → A: Author and Date Sent should be independently generated metadata following the same pattern as other file types, regardless of From/Sent Date values
- Q: For the File Size metadata field in EML files, what should be the calculated value? → A: Size of the generated EML file including any attachments
- Q: When implementing EML metadata support, how should the system handle validation to ensure existing functionality remains intact? → A: No special validation needed - if flags aren't used, behavior remains exactly the same

## User Scenarios & Testing

### Primary User Story
As a Zipper user generating EML test datasets, I need the `--with-metadata` and `--with-text` flags to work consistently with `--type eml` so that I can create comprehensive email datasets with extended metadata and extracted text files for performance testing scenarios.

### Acceptance Scenarios
1. **Given** I run Zipper with `--type eml --with-metadata`, **When** the generation completes, **Then** the `.dat` load file must include additional metadata columns (Custodian, Date Sent, Author, File Size) in the same order as used for other file types
2. **Given** I run Zipper with `--type eml --with-text`, **When** the generation completes, **Then** the `.dat` load file must include an Extracted Text column pointing to corresponding `.txt` files within the zip archive
3. **Given** I run Zipper with `--type eml --with-metadata --with-text`, **When** the generation completes, **Then** the `.dat` load file must contain all metadata columns plus the Extracted Text column
4. **Given** I run Zipper with `--type eml` (no additional flags), **When** the generation completes, **Then** the behavior remains unchanged with the current EML-specific header structure

### Edge Cases
- Text files for EML will use email-specific placeholder content rather than generic placeholder text
- Column ordering will logically group related fields while maintaining the core structure
- Metadata fields will be independently generated, potentially creating realistic variance between email-specific and metadata fields
- File size calculations will accurately reflect the actual EML content including any generated attachments

## Requirements

### Functional Requirements
- **FR-001**: System MUST accept `--with-metadata` flag when `--type eml` is specified
- **FR-002**: System MUST accept `--with-text` flag when `--type eml` is specified  
- **FR-003**: System MUST generate `.dat` load file with metadata columns (Custodian, Date Sent, Author, File Size) when `--with-metadata` is used with EML generation, where File Size reflects the actual EML file size including attachments
- **FR-004**: System MUST generate corresponding `.txt` files with email-specific placeholder text content and include Extracted Text column when `--with-text` is used with EML generation
- **FR-005**: System MUST use logical column ordering when combining EML-specific and metadata columns: Control Number, File Path, To, From, Subject, Custodian, Author, Sent Date, Date Sent, File Size, Attachment, Extracted Text
- **FR-006**: System MUST preserve existing EML-specific columns (To, From, Subject, Sent Date, Attachment) when adding metadata support
- **FR-007**: Author metadata field MUST be independently generated using the same random author selection as other file types
- **FR-008**: Date Sent metadata field MUST be independently generated using the same random date range as other file types

### Key Entities
- **EML File**: Email message file containing headers, body, and optional attachments
- **Load File Entry**: Row in the `.dat` file containing file metadata and path information
- **Text File**: Extracted text content file generated when `--with-text` flag is used
- **Metadata Columns**: Additional data fields (Custodian, Date Sent, Author, File Size) added when `--with-metadata` flag is used

## Review & Acceptance Checklist

### Content Quality
- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

### Requirement Completeness
- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous  
- [x] Success criteria are measurable
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Execution Status

- [x] User description parsed
- [x] Key concepts extracted
- [x] Ambiguities marked
- [x] User scenarios defined
- [x] Requirements generated
- [x] Entities identified
- [x] Review checklist passed
