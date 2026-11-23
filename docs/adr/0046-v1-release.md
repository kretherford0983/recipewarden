# ADR 0046: Version 1.0.0

- Status: Proposed — **owner review requested** (2026-11-23).
- Requirements: section 9.10 item 10; docs/branching.md

## Decisions
1. M8 bumps `<VersionPrefix>` to **1.0.0**. The release is published by the existing flow: `feature/m8-hardening`
   → PR into `develop` → dev build tested on the host → PR `develop` → `main`, which builds the release package.
   Nothing is tagged or released from the feature branch.
2. "Every main feature" for v1 means every FR in the spec except those deferred by an ADR: **FR-EXP-08** (prompt
   history, ADR 0033) stays deferred. Per-user favorites (ADR 0044) are not a spec requirement.
3. All ADRs written while the owner was away (0032 to 0046) stay **Proposed** until reviewed; changes after review
   go in as normal feature branches against 1.0.x / 1.1.0.
4. After 1.0.0, work moves to updates: Dependabot PRs into `develop`, fixes on `hotfix/*`, and new features on
   `feature/*` with a minor version bump.
