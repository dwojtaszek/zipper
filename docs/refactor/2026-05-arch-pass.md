# 2026-05 Architecture Pass Summary

## Overview

A comprehensive architecture pass resolved 30+ issues across the Zipper codebase, covering bugs, test coverage, performance, and structural refactors.

## Key PRs

| Area | PRs | Impact |
|------|-----|--------|
| FGR refactor | #235–#243 | FileGenerationRequest sub-configs, flat access analyzer |
| Email aggregate | #244–#251 | Email value object, EmailFactory, EmailSerializer |
| Load File seam | #324 (#261) | LoadFileRecord + ILoadFileSerializer |
| Column generation | #276 (#262) | Unified IColumnValueGenerator, MetadataRowBuilder retired |
| Pipeline cleanup | #326 (#263) | RequiresSequentialProcessing removed, EmlGenerationService retired |
| Production Set split | #329 (#274) | ProductionSetPlanner extracted |
| Orchestrator design | #330 (#266) | Three-mode architecture confirmed as correct |

## Architectural Decisions

- **ADR-0004**: Unified column value generation via IColumnValueGenerator
- **ADR-0005**: Email aggregate (Email record + factory + serializer)
- **ADR-0006**: Three-mode pipeline architecture (no unification needed)

## Bug Fixes

- #279: Deadlock in ParallelFileGenerator when consumer throws
- #280: --chaos-mode requires --loadfile-only enforcement
- #281: Audit file totalRecords off-by-one
- #282: EML pipeline ignores --seed
- #283: EmailSerializer Date header locale-dependent
- #284: CsvWriter hardcodes UTF-8
- #285: CodedGenerator infinite-loop on multi-value
- #292: ColumnProfileLoader.Validate gaps
- #293: ProductionSetGenerator partial output cleanup

## Infrastructure

- #208: Perf guard CI job + baselines + weekly refresh
- #206: Target-zip-size accuracy E2E tests
- #294/#298/#299: Shell script hygiene (set -euo pipefail)
