# ADR 0006: Parallel tests from the start

- Status: Accepted (2026-11-20)
- Requirements: NFR-06, AC-97, section 9.8, 9.9

## Context
PennyWarden's CI grew to about 45 minutes before it was parallelised (2.4.0). RecipeWarden's suite is small, but its
structure decides how it scales, so the speed rules are set now.

## Decision
- `build/test.sh` runs all test projects at the same time; xUnit runs test classes in parallel on every core
  (`tests/xunit.runner.json`).
- Test databases are migrated once per test process and copied per test; test hosts use cheap password hashing.
- `ci.yml` runs independent jobs side by side; the job `test` aggregates them and is the only required status check.
- Every job has a time limit of about 3-4x its normal run.
- E2E tests (from M1) give each Playwright worker its own app instance and run as their own job, sharded when long.
- Rules for writing parallel-safe tests live in docs/testing.md.

## Consequences
- Locally on 2 cores the suite went from 24 s to 15 s; in CI the vulnerability check no longer adds to the test job.
- The branch ruleset requires one check, `test`, instead of listing every job.
- Tests must not share mutable state; a flaky test that only fails in parallel is a bug in the test.
