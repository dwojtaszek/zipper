---
name: Architecture-Deepening
description: Guide the agent through a structured 4-phase process (Explore, Synthesize, Validate, and Issue/Reject) to systematically deepen codebase architecture.
---

# Architecture Deepening Process

This skill guides a coding agent through a structured, multi-phase architecture deepening process to identify, analyze, validate, and document architectural improvements in any codebase.

## Prerequisites

Before running this process, ensure:
- The `gh` CLI is installed and authenticated with the repository.
- You have access to the skill-loading mechanism in the agent platform.
- You have companion skills (like `brainstorming` and `grill-with-docs`) available to load on-demand.

---

## Phase 1: Explore

**Goal**: Map the system, identify architectural friction points, and compile potential candidates for deepening.

1. **Scope Division**: Divide the codebase into logical, high-impact areas. For example:
   - CLI & Input Validation layer
   - Configuration management
   - Core processing/generation pipelines
   - Integration seams (e.g., Load File composition, serialization)
   - Extensibility points (e.g., profiles, pluggable generators)

2. **Parallel Exploration**: Dispatch parallel subagents (typically 5) to analyze each scope area. Instruct each subagent to investigate:
   - File structures and design patterns used.
   - Core responsibilities and separation of concerns.
   - Key architectural invariants and dependencies.

   > [!NOTE]
   > Running multiple subagents concurrently can consume significant LLM API rate limits (TPM/RPM). If you encounter rate limit errors, execute the subagents sequentially or introduce a delay between dispatches.

3. **Subagent Output Schema**: Each exploration subagent must return:
   - **Files analyzed**: List of key source files inspected.
   - **Friction points**: Code smells, tight coupling, poor testability, or unclear naming.
   - **Depth analysis**: Assessment of how well the area handles scaling, errors, and changes.
   - **Candidates**: Specific structural changes proposed to improve the area.

---

## Phase 2: Synthesize

**Goal**: Aggregate and categorize candidate proposals by their potential impact and feasibility.

1. **Categorization**: Group the candidates discovered during Phase 1 by leverage:
   - **Strong**: Immediate value, clear architectural improvement, low risk of regression.
   - **Worth Exploring**: Moderate impact, requires careful design or scoping.
   - **Speculative**: High risk or effort, potential return is uncertain.

2. **Synthesis Report**: Compile a report (in Markdown or HTML) documenting:
   - A summary of the exploration findings.
   - The categorized list of candidates.
   - Before/after structural diagrams representing the key changes (using Mermaid or similar visual aids).

---

## Phase 3: Validate one-by-one

**Goal**: Rigorously evaluate each candidate under adversarial conditions and prepare concrete implementation definitions.

For each candidate, run the following loop:

1. **Adversarial Review**:
   - Actively attempt to find reasons why the change should *not* be made.
   - Analyze side effects, ripple effects across adjacent modules, and potential regressions.
   - Identify edge cases, performance implications, and alternative approaches.

2. **Brainstorming**:
   - Load the `brainstorming` skill.
   - Formulate and answer clarifying questions one at a time.
   - Develop 2-3 implementation approaches, evaluate their pros/cons, and recommend one.
   - Obtain design approval before continuing.

3. **Grill-with-docs**:
   - Load the `grill-with-docs` skill.
   - Challenge the plan against the existing domain model and glossary.
   - Update `CONTEXT.md` and draft an Architecture Decision Record (ADR) if the change introduces a new pattern or convention.

4. **GitHub Issue Formulation**:
   - For accepted candidates, draft a detailed, agent-ready GitHub issue.
   - **Required Fields**:
     - **Title**: Descriptive title.
     - **File Paths**: Specific files to modify or create.
     - **Code Examples**: Before-and-after snippets showing the exact structural changes.
     - **Test Plan**: Specific unit and E2E tests to write or update.
     - **Verification Steps**: Step-by-step instructions for an agent to verify correct behavior.
   - Submit the issue to GitHub using `gh issue create`.

   > [!WARNING]
   > To prevent shell command injection, pass issue details securely when calling `gh issue create`. Prefer using the `--title` flag and passing the body via a file (`--body-file`) or standard input, rather than inline command arguments.

---

## Phase 4: Rejection criteria and documentation

**Goal**: Formally reject and document candidates that do not represent genuine architectural improvements.

1. **Rejection Criteria**: Reject a candidate if:
   - **Not a real problem**: Code review reveals that the current design is intentional, aligned with dependencies, or already in sync (e.g., column generators having different lifecycles).
   - **Necessary workaround**: The current structure is required to satisfy external standards, APIs, or legacy constraints (e.g., specific encoding workaround requirements).
   - **Over-engineering**: The proposal violates the simplicity principle (YAGNI) and does not provide sufficient long-term value.

2. **Rejection Documentation**:
   - For every rejected candidate, write a clear summary of the rejection rationale.
   - Document the findings in the repository's architecture documentation or project notes so future developers know why the change was decided against.
