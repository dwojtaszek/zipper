# Changelog

## [Unreleased]

### Internal

- Email aggregate G4 (#217): renamed `EmailTemplate` to `Email`, `AttachmentInfo` to `EmailAttachment`, moved all `src/Emails/**` to `Zipper.Emails` namespace, deleted `EmailBuilder.cs` + `EmailTemplateSystem.cs` + their test files, redistributed test scenarios into `EmailSerializerTests.cs` and `EmailFactoryTests.cs`, added `EmailContext.cs` and `docs/adr/0005-email-aggregate.md`.
- FGR refactor F4 (#213): removed 35 flat pass-through properties from `FileGenerationRequest`, deleted 4 static helpers from `LoadFileWriterBase` (`GetFileTypeLower`, `ShouldIncludeMetadata`, `ShouldIncludeEmlColumns`, `ShouldIncludePageCount`), raised `FGR_FLAT_ACCESS` analyzer severity from `Info` to `Error`, and added CI grep guard to `run-tests.sh`.
