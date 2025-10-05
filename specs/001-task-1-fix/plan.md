
# Implementation Plan: Fix Incomplete EML Feature Interaction

**Branch**: `001-task-1-fix` | **Date**: 2025-10-05  | **Spec**: [../spec.md](spec.md)
**Input**: Feature specification from `/specs/001-task-1-fix/spec.md`

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
Fix the incomplete feature interaction where `--with-metadata` and `--with-text` flags are ignored when using `--type eml`. The solution involves updating the `GenerateEmlFiles` method to accept and handle these flags consistently with other file types, while maintaining backward compatibility and using email-specific placeholder text for extracted content.

## Technical Context
**Language/Version**: C# / .NET 8.0  
**Primary Dependencies**: SixLabors.ImageSharp, System.Text.Encoding.CodePages, System.IO.Compression  
**Storage**: File system (zip archives and load files)  
**Testing**: Cross-platform test scripts (tests/run-tests.sh, tests/run-tests.bat) - **CRITICAL: Both Windows and Linux/Mac must pass**  
**Target Platform**: Cross-platform (.NET 8.0) - Windows, Linux, macOS
**Project Type**: single (CLI application)  
**Performance Goals**: Stream-based processing for large datasets, direct-to-archive writing  
**Constraints**: No intermediate file storage, maintain backward compatibility, **cross-platform test compatibility mandatory**  
**Scale/Scope**: Single method modification in existing codebase

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Initial Check (Pre-Research)
- **CLI-First Interface**: ✅ YES - Feature extends existing CLI flags (`--with-metadata`, `--with-text`) to work with `--type eml`
- **Efficient, Stream-Based Processing**: ✅ YES - Maintains existing stream-based architecture, writes directly to zip archive
- **Rigorous, Integration-Focused Testing**: ✅ YES - Will use existing test framework to verify correct output format and file generation
- **Formal Requirements as Source of Truth**: ✅ YES - Feature directly addresses Task 1 from project requirements and acceptance criteria
- **Automated and Consistent Development Workflow**: ✅ YES - Changes fit within existing codebase structure and CI/CD pipeline
- **Pragmatic Simplicity**: ✅ YES - Simple method signature change and parameter passing, reuses existing patterns

### Post-Design Check (After Phase 1)
- **CLI-First Interface**: ✅ CONFIRMED - Design preserves CLI-only interface, no new interaction methods
- **Efficient, Stream-Based Processing**: ✅ CONFIRMED - Data model maintains stream processing, no intermediate storage
- **Rigorous, Integration-Focused Testing**: ✅ CONFIRMED - Comprehensive cross-platform test suite covers all scenarios with mandatory dual implementations
- **Formal Requirements as Source of Truth**: ✅ CONFIRMED - All design elements trace back to specification requirements
- **Automated and Consistent Development Workflow**: ✅ CONFIRMED - Agent context updated, follows existing patterns
- **Cross-Platform Compatibility**: ✅ CONFIRMED - Comprehensive test suite has both .sh and .bat implementations
- **Technology Stack**: ✅ CONFIRMED - Uses .NET 8.0 with modern conventions
- **Pragmatic Simplicity**: ✅ CONFIRMED - Single comprehensive test file reduces complexity while maintaining dual-platform support

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
Zipper/                    # Main project directory
├── Program.cs             # Entry point and CLI argument parsing - PRIMARY CHANGE TARGET
├── EmlFile.cs             # EML file generation logic
├── PlaceholderFiles.cs    # File templates and content
├── FileDistributionHelper.cs  # Folder distribution algorithms
└── Zipper.csproj          # Project configuration

tests/                     # Test scripts and validation
├── run-tests.sh           # Linux test runner
├── run-tests.bat          # Windows test runner
└── (test data)

.specify/                  # Development workflow
├── templates/
├── scripts/
└── memory/
```
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Single CLI application** - The Zipper project follows a single project structure with all functionality contained in the main Zipper directory. Changes will be focused on the Program.cs file with supporting modifications to maintain consistency across the codebase.

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

**Task Generation Strategy for EML Feature Fix** (Updated for Constitutional Compliance):
- Load `.specify/templates/tasks-template.md` as base structure
- Generate tasks based on research findings and data model requirements
- Focus on surgical changes to existing GenerateEmlFiles method
- Maintain backward compatibility throughout implementation
- **CRITICAL**: Consolidate all EML tests into single comprehensive cross-platform test suite

**Specific Task Categories**:
1. **Setup Tasks**:
   - Verify current test baseline for EML generation
   - Document existing behavior for regression prevention

2. **Core Implementation Tasks**:
   - Update GenerateEmlFiles method signature
   - Implement conditional header generation logic
   - Add metadata column support using existing patterns
   - Add text file generation support
   - Update main method parameter passing

3. **Integration Tasks**:
   - Create email-specific placeholder text constant
   - Integrate file size calculation for EML files
   - Ensure consistent column delimiter usage

4. **Consolidated Cross-Platform Testing & Validation** (**CRITICAL**):
   - Create comprehensive EML test suite (`test-eml-comprehensive`) with both .sh and .bat versions
   - Single test file covering all scenarios: basic, metadata-only, text-only, combined flags, attachments
   - Performance validation integrated into comprehensive suite
   - Backward compatibility verification included

**Ordering Strategy**:
- Setup and baseline establishment first
- Core method signature changes before implementation
- Incremental feature addition (metadata, then text)
- Single comprehensive cross-platform test suite creation
- Integration and final validation

**Parallelization Opportunities**:
- Comprehensive test suite creation optimizes development time
- Documentation updates can be parallel [P]
- Performance validation integrated rather than separate

**Estimated Task Count**: 10-12 focused, specific tasks (reduced by consolidating multiple test files)

**Key Dependencies**:
- Method signature must be updated before implementation tasks
- Metadata support must be complete before testing
- Text support must be complete before testing
- **CRITICAL**: Comprehensive test suite must have both .sh and .bat implementations
- **CRITICAL**: All test scenarios must pass on both platforms before deployment

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
- [x] Phase 2: Task planning approach updated for constitutional compliance (/plan command - describe approach only)
- [ ] Phase 3: Tasks regenerated with consolidated cross-platform testing (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS (updated for v1.4.0)
- [x] All NEEDS CLARIFICATION resolved
- [x] Cross-platform testing approach defined
- [ ] Tasks updated for constitutional compliance

---
*Based on Constitution v2.1.1 - See `/memory/constitution.md`*
