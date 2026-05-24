# Dependency Update Policy

All dependency version bumps must observe a **minimum 3-day waiting period** after the upstream release before merging. This applies to both Dependabot and manual dependency updates.

Rationale: new package releases occasionally introduce breaking changes or regressions that are caught within the first few days. Delaying adoption by 3 days reduces the risk of integrating unstable dependencies.

Implementation:
- Dependabot PRs may be opened immediately (schedule-controlled) but must not be merged until 3 days after the release date of the target version.
- Reviewers should verify the NuGet/GitHub release date before approving a dependency PR.
