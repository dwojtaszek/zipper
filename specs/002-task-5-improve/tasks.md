# Tasks: Improve Build and Test Workflow

**Input**: Design documents from `/specs/002-task-5-improve/`
**Prerequisites**: plan.md (required), research.md, data-model.md, quickstart.md

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → Implementation plan loaded successfully
   → Extract: GitHub Actions YAML, matrix strategy, caching
2. Load optional design documents:
   → data-model.md: Extract workflow entities → job definition tasks
   → research.md: Extract technical decisions → analysis tasks
   → quickstart.md: Extract validation scenarios → test tasks
3. Generate tasks by category:
   → Analysis: Understand existing workflows, identify patterns
   → Tests: Workflow validation tests, artifact handling tests
   → Core: New workflow implementation, job definitions
   → Integration: Job dependencies, artifact passing
   → Cleanup: Remove old workflows, finalize
4. Apply task rules:
   → Different workflow phases = mark [P] for parallel
   → Same workflow file = sequential (no [P])
   → Tests before implementation (TDD)
5. Number tasks sequentially (T001, T002...)
6. Generate dependency graph
7. Create parallel execution examples
8. Validate task completeness:
   → All workflow jobs have tests?
   → All platform builds covered?
   → All validation scenarios included?
9. Return: SUCCESS (tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- **GitHub Actions workflows**: `.github/workflows/`
- **Test scripts**: `tests/`
- **Application source**: `Zipper/`
- **Root configuration**: `.editorconfig`, `.version`

## Phase 3.1: Analysis & Setup
- [x] T001 Analyze existing build.yml workflow structure and dependencies
- [x] T002 Analyze existing test.yml workflow structure and matrix strategy
- [x] T003 [P] Verify .editorconfig exists or create basic configuration
- [x] T004 Verify test scripts exist (tests/run-tests.bat, tests/run-tests.sh)

## Phase 3.2: Tests First (TDD) ⚠️ MUST COMPLETE BEFORE 3.3
**CRITICAL: These validation tests MUST be created and verified to PASS with current workflows before ANY implementation**
**CROSS-PLATFORM REQUIREMENT: Each validation must work on Windows (.bat) and Unix (.sh)**
- [x] T005 [P] Create workflow validation test script in tests/test-workflow-validation.sh
- [x] T006 [P] Create Windows version of workflow validation test in tests/test-workflow-validation.bat
- [x] T007 [P] Create artifact handling test in tests/test-artifact-handling.sh
- [x] T008 [P] Create Windows version of artifact handling test in tests/test-artifact-handling.bat
- [x] T009 [P] Create build matrix test in tests/test-build-matrix.sh
- [x] T010 [P] Create Windows version of build matrix test in tests/test-build-matrix.bat

## Phase 3.3: Core Implementation (ONLY after tests are validated)
- [x] T011 Create unified build-and-test.yml workflow structure with basic metadata
- [x] T012 Implement lint job with .editorconfig validation
- [x] T013 Implement build job with matrix strategy for parallel builds
- [x] T014 Add comprehensive caching (dependencies, build outputs, tools)
- [x] T015 Implement test job with artifact download and platform-specific testing
- [x] T016 Implement release job with conditional execution and artifact publishing
- [x] T017 Configure job dependencies (lint → build → test → release)
- [x] T018 Set up master branch triggers and permissions

## Phase 3.4: Integration & Validation
- [ ] T019 Configure artifact passing between build, test, and release jobs
- [ ] T020 Test workflow execution with empty commit on master branch
- [ ] T021 Validate matrix strategy execution on all platforms
- [ ] T022 Test fail-fast behavior with intentional lint violations
- [ ] T023 Test fail-fast behavior with intentional build failures
- [ ] T024 Test fail-fast behavior with intentional test failures
- [ ] T025 Validate artifact retention and download functionality
- [ ] T026 Validate .editorconfig linting functionality and missing file handling

## Phase 3.5: Cleanup & Finalization
- [ ] T027 Backup existing workflows (git tag or branch)
- [ ] T028 Remove .github/workflows/build.yml
- [ ] T029 Remove .github/workflows/test.yml
- [ ] T030 Run final workflow validation test
- [ ] T031 Update documentation (README.md if needed)

## Dependencies
- Analysis (T001-T004) before Tests (T005-T010)
- Tests (T005-T010) before Implementation (T011-T018)
- Implementation (T011-T018) before Integration (T019-T026)
- Integration (T019-T026) before Cleanup (T027-T031)

## Parallel Example
```
# Launch T005-T010 together (after T001-T004 complete):
Task: "Create workflow validation test script in tests/test-workflow-validation.sh"
Task: "Create Windows version of workflow validation test in tests/test-workflow-validation.bat"
Task: "Create artifact handling test in tests/test-artifact-handling.sh"
Task: "Create Windows version of artifact handling test in tests/test-artifact-handling.bat"
Task: "Create build matrix test in tests/test-build-matrix.sh"
Task: "Create Windows version of build matrix test in tests/test-build-matrix.bat"
```

## Notes
- [P] tasks = different test files, no dependencies
- Verify current workflows pass tests before implementing new workflow
- Test each phase with current workflows before making changes
- Backup old workflows before deletion
- Validate cross-platform compatibility for all test scripts

## Task Generation Rules
*Applied during main() execution*

1. **From Data Model**:
   - Each workflow job → implementation task
   - Each artifact type → validation task

2. **From Research Decisions**:
   - Each technical decision → analysis task
   - Each risk identified → mitigation task

3. **From User Stories**:
   - Each acceptance scenario → validation test [P]
   - Quickstart validation → integration test

4. **Ordering**:
   - Analysis → Tests → Implementation → Integration → Cleanup
   - Job dependencies block parallel execution

## Validation Checklist
*GATE: Checked by main() before returning*

- [x] All workflow jobs have corresponding tests
- [x] All platform builds covered by tests
- [x] All tests come before implementation
- [x] Parallel tasks truly independent
- [x] Each task specifies exact file path
- [x] No task modifies same file as another [P] task
- [x] Cross-platform test requirements met (.bat and .sh versions)
- [x] Constitution requirements satisfied (CLI-first, cross-platform, etc.)