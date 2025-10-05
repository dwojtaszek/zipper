
# Implementation Plan: Improve Build and Test Workflow

**Branch**: `002-task-5-improve` | **Date**: 2025-10-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-task-5-improve/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → If not found: ERROR "No feature spec at {path}"
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Detect Project Type from file system structure or context (web=frontend+backend, mobile=app+api)
   → Set Structure Decision based on project type
3. Fill the Constitution Check section based on the content of the constitution document.
4. Evaluate Constitution Check section below
   → If violations exist: Document in Complexity Tracking
   → If no justification possible: ERROR "Simplify approach first"
   → Update Progress Tracking: Initial Constitution Check
5. Execute Phase 0 → research.md
   → If NEEDS CLARIFICATION remain: ERROR "Resolve unknowns"
6. Execute Phase 1 → contracts, data-model.md, quickstart.md, agent-specific template file (e.g., `CLAUDE.md` for Claude Code, `.github/copilot-instructions.md` for GitHub Copilot, `GEMINI.md` for Gemini CLI, `QWEN.md` for Qwen Code, or `AGENTS.md` for all other agents).
7. Re-evaluate Constitution Check section
   → If new violations: Refactor design, return to Phase 1
   → Update Progress Tracking: Post-Design Constitution Check
8. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
9. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Refactor existing separate build.yml and test.yml GitHub Actions workflows into a unified build-and-test.yml workflow that incorporates linting, parallel multi-platform builds, artifact-based testing, and conditional releases. The workflow will run on master branch pushes only, fail fast on any platform failures, use comprehensive caching for performance, and automatically create releases when all validation steps pass.

## Technical Context
**Language/Version**: GitHub Actions YAML v2.0
**Primary Dependencies**: actions/checkout@v3, actions/setup-dotnet@v3, actions/cache@v3, actions/upload-artifact@v4, actions/download-artifact@v4, softprops/action-gh-release@v2
**Storage**: GitHub Actions artifact storage (7-day retention) + GitHub releases
**Testing**: .NET 8.x test framework with platform-specific test scripts (.bat for Windows, .sh for Unix)
**Target Platform**: GitHub Actions runners (ubuntu-latest, windows-latest, macos-latest)
**Project Type**: single - CI/CD workflow automation for .NET CLI application
**Performance Goals**: Build time optimization through comprehensive caching (dependencies, build outputs, tools) - Target: 30% reduction in overall workflow execution time compared to current separate workflows
**Constraints**: Must fail fast on any platform failure, must use matrix strategy for parallel builds, automatic releases on success
**Scale/Scope**: Single repository with multi-platform builds for Windows x64, Linux x64, macOS ARM64

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **CLI-First Interface**: ✅ PASS - Feature is CI/CD workflow automation that supports the CLI application
- **Efficient, Stream-Based Processing**: ✅ PASS - CI/CD workflow uses artifact-based processing without intermediate file storage
- **Rigorous, Integration-Focused Testing**: ✅ PASS - Plan includes end-to-end testing with both .bat and .sh implementations for all platforms
- **Formal Requirements as Source of Truth**: ✅ PASS - Feature traceable to CI/CD improvement requirements in spec.md
- **Automated and Consistent Development Workflow**: ✅ PASS - Enhances existing CI/CD with proper versioning and automation
- **Cross-Platform Compatibility**: ✅ PASS - Explicitly builds and tests on Windows, Linux, and macOS
- **Pragmatic Simplicity**: ✅ PASS - Combines existing workflows into single streamlined workflow

## Project Structure

### Documentation (this feature)
```
specs/[###-feature]/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
.github/workflows/
├── build-and-test.yml    # New unified workflow (replaces build.yml, test.yml)
├── build.yml             # TO BE DELETED
└── test.yml              # TO BE DELETED

Zipper/                    # Existing .NET application
├── Zipper.csproj
└── [existing source files]

tests/                     # Existing test infrastructure
├── run-tests.bat          # Windows test runner (must exist)
├── run-tests.sh           # Unix test runner (must exist)
└── [existing test files]
```

**Structure Decision**: Single project - CI/CD workflow enhancement for existing .NET CLI application. The unified workflow will replace the separate build.yml and test.yml files while maintaining compatibility with existing test infrastructure.

## Phase 0: Outline & Research
1. **Extract unknowns from Technical Context** above:
   - For each NEEDS CLARIFICATION → research task
   - For each dependency → best practices task
   - For each integration → patterns task

2. **Generate and dispatch research agents**:
   ```
   For each unknown in Technical Context:
     Task: "Research {unknown} for {feature context}"
   For each technology choice:
     Task: "Find best practices for {tech} in {domain}"
   ```

3. **Consolidate findings** in `research.md` using format:
   - Decision: [what was chosen]
   - Rationale: [why chosen]
   - Alternatives considered: [what else evaluated]

**Output**: research.md with all NEEDS CLARIFICATION resolved

## Phase 1: Design & Contracts
*Prerequisites: research.md complete*

1. **Extract entities from feature spec** → `data-model.md`:
   - Entity name, fields, relationships
   - Validation rules from requirements
   - State transitions if applicable

2. **Generate API contracts** from functional requirements:
   - For each user action → endpoint
   - Use standard REST/GraphQL patterns
   - Output OpenAPI/GraphQL schema to `/contracts/`

3. **Generate contract tests** from contracts:
   - One test file per endpoint
   - Assert request/response schemas
   - Tests must fail (no implementation yet)

4. **Extract test scenarios** from user stories:
   - Each story → integration test scenario
   - Quickstart test = story validation steps

5. **Update agent file incrementally** (O(1) operation):
   - Run `.specify/scripts/bash/update-agent-context.sh gemini`
     **IMPORTANT**: Execute it exactly as specified above. Do not add or remove any arguments.
   - If exists: Add only NEW tech from current plan
   - Preserve manual additions between markers
   - Update recent changes (keep last 3)
   - Keep under 150 lines for token efficiency
   - Output to repository root

**Output**: data-model.md, /contracts/*, failing tests, quickstart.md, agent-specific file

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
- Load `.specify/templates/tasks-template.md` as base
- Generate tasks from Phase 1 design docs (data model, quickstart, agent context)
- Workflow analysis tasks → Understand current build.yml and test.yml
- Workflow design tasks → Create unified build-and-test.yml structure
- Implementation tasks → Create the new workflow file
- Validation tasks → Test workflow functionality
- Cleanup tasks → Remove old workflow files

**Ordering Strategy**:
- Analysis phase: Understand existing workflows
- Design phase: Create new unified workflow structure
- Implementation phase: Build the new workflow file
- Testing phase: Validate workflow functionality
- Cleanup phase: Remove old files and finalize

**Task Categories**:
- **Analysis Tasks** (3-4 tasks): Examine existing workflows, understand dependencies
- **Design Tasks** (4-5 tasks): Create unified workflow structure, define job dependencies
- **Implementation Tasks** (6-8 tasks): Build the new workflow YAML with all jobs
- **Validation Tasks** (3-4 tasks): Test workflow execution, validate artifact handling
- **Cleanup Tasks** (2-3 tasks): Remove old workflows, finalize implementation

**Estimated Output**: 18-24 numbered, ordered tasks in tasks.md

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |


## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [x] Phase 2: Task planning complete (/plan command - describe approach only)
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented

---
*Based on Constitution v1.4.0 - See `/memory/constitution.md`*
