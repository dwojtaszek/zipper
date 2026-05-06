# ADR-0005: Email Aggregate

**Status:** Accepted  
**Date:** 2026-05-06  
**Supersedes:** —

## Context

Email-related code was split across multiple top-level files — `EmailBuilder.cs`, `EmailTemplateSystem.cs`, `EmailTemplate.cs`, and `AttachmentInfo` — all living in the root `Zipper` namespace. As the email domain grew (factory, serializer, attachment picker), the lack of a cohesive package made it hard to reason about email as a bounded concept.

## Decision

Collect all email domain types into a dedicated `src/Emails/` package under the `Zipper.Emails` namespace:

- `Email` — the core domain record (renamed from `EmailTemplate`)
- `EmailAttachment` — attachment data (renamed from `AttachmentInfo`)
- `EmailCategory` — category enum
- `EmailContext` — contextual generation parameters
- `EmailFactory` — template-picking and generation logic
- `EmailSerializer` — RFC 5322 EML serialization
- `EmailAttachmentPicker` — attachment selection from file pool

Delete `EmailBuilder` (MIME-construction shim replaced by `EmailSerializer`) and `EmailTemplateSystem` (factory shim replaced by `EmailFactory`).

## Consequences

- Future email changes land in `src/Emails/` only.
- `MetadataRowBuilder`'s email accessors (GetEmailTo, GetEmailFrom, etc.) remain in `src/` — arch-3 unification is deferred.
- External callers that previously used `EmailBuilder` or `EmailTemplateSystem` use `EmailSerializer` and `EmailFactory` directly.
- `Zipper.Emails.Email` and `Zipper.Emails.EmailAttachment` are the canonical types. `EmailTemplate` and `AttachmentInfo` no longer exist.
