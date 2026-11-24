# ADR 0054: Drop Ubuntu 22.04 LTS

- Status: Accepted (owner, 2026-11-24).
- Requirements: NFR-02, AC-90; amends ADR 0007's test matrix.

## Context
Ubuntu 22.04's standard support ends on May 31, 2027, and 26.04 LTS is out. The owner chose to drop it on
2026-11-24. Each supported image costs a CI installer job, and 22.04 ships Python 3.10, which is below what the
Python adapters' libraries need (ADR 0051 bundles its own runtime, so that alone would not have forced the change).

## Decision
- NFR-02 and AC-90 name Debian 12/13 and Ubuntu 24.04/26.04 LTS.
- The installer **warns** on Ubuntu 22.04 and carries on ("no longer supported; upgrade to 24.04 or 26.04"), so an
  existing 22.04 host can still upgrade in place; other releases are refused as before.
- CI's installer matrix drops `ubuntu:22.04`; a preflight test checks the warning.

## Consequences
One CI job fewer. A 22.04 host keeps working but is no longer tested.

The `protect-main` ruleset (on `main` and `develop`) listed `installer (ubuntu:22.04)` as a required check, which no
longer reports, so merges waited on it. On 2026-11-24 the owner replaced it with `installer (ubuntu:26.04)`. When a CI
job is renamed or removed, change the ruleset's required checks with it.
