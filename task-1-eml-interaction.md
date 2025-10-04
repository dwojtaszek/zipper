# Task 1: Fix Incomplete Feature Interaction with --type eml

## Description

The `--type eml` feature does not currently work with the `--with-metadata` or `--with-text` flags. This task is to update the `GenerateEmlFiles` method in `Program.cs` to correctly handle these flags.

## Acceptance Criteria

*   When a user specifies `--type eml` along with `--with-metadata`, the generated `.dat` load file must include the additional metadata columns (`Custodian`, `Date Sent`, `Author`, `File Size`).
*   When a user specifies `--type eml` along with `--with-text`, the generated `.dat` load file must include the `Extracted Text` column.
*   When all three flags (`--type eml`, `--with-metadata`, and `--with-text`) are used, the load file must contain all the relevant columns.
*   The order of the columns in the load file should be consistent with the order in the `GenerateFiles` method.
