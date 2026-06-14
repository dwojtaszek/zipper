# Architecture Decision Records

This directory records significant architecture decisions for Zipper. ADR numbers are immutable: once assigned, a number is never reassigned or renumbered. The log begins at ADR-0004; no ADRs are recorded for ADR-0001 through ADR-0003.

| ADR | Decision |
|-----|----------|
| [ADR-0004](ADR-0004-unified-column-generation.md) | Unified column value generation via `IColumnValueGenerator` |
| [ADR-0005](ADR-0005-email-aggregate.md) | Email value object + `EmailFactory` as sole constructor |
| [ADR-0006](ADR-0006-three-mode-pipeline.md) | Three-mode pipeline (Standard / Loadfile-Only / Production Set) |
| [ADR-0007](ADR-0007-loadfile-composition-seam.md) | Load File composer → serializer → emitter seam |

New ADRs use the next available number (ADR-0008).
