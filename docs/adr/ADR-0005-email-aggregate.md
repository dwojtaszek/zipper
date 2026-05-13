# ADR-0005: Email Aggregate

## Status: Accepted

## Context

Email generation previously used string-keyed column synthesis scattered across writers. The Email domain was implicit.

## Decision

`Email` is a value object at `src/Emails/Email.cs`. `EmailFactory` is the sole constructor. `EmailSerializer` is the sole serializer. `EmailAttachmentPicker` handles attachment selection.

## Consequences

- Do not re-introduce string-keyed email column synthesis in writers
- All email metadata flows through the `Email` record attached to `FileData.Email`
- `EmlFileGenerator` is the pipeline entry point; `EmlGenerationService` is retired
- Email determinism (seeded Random, fixed reference date) is handled at the generator level
