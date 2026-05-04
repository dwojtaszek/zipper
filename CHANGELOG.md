# Changelog

## [Unreleased]

### Internal

- FGR refactor F4 (#213): removed 35 flat pass-through properties from `FileGenerationRequest`, deleted 4 static helpers from `LoadFileWriterBase` (`GetFileTypeLower`, `ShouldIncludeMetadata`, `ShouldIncludeEmlColumns`, `ShouldIncludePageCount`), raised `FGR_FLAT_ACCESS` analyzer severity from `Info` to `Error`, and added CI grep guard to `run-tests.sh`.
