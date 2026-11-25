# ADR 0004: Branch flow develop → main with development builds

- Status: Accepted (2026-11-18)
- Requirements: section 9.9, FR-INST-03

## Context
Section 9.9 assumed trunk-based work on `main` with releases on `v*` tags. The product owner wants to install and
test a build before it reaches `main`, the way PennyWarden does (`feature/*` → `develop` → `test` → `main`), but
without PennyWarden's separate `test` stage.

## Decision
- Branches: `feature/*`, `hotfix/*`, `dependabot/*` → `develop` → `main`; hotfixes into `main`, then back-merged.
  `build/check-promotion.sh` enforces the flow as the required `promotion-rules` check.
- One version source: `<VersionPrefix>` in `Directory.Build.props`; `CHANGELOG.md` has a section per version.
- `build.yml` packages on every push to `develop` and `main` (no test re-run: pull requests must be up to date and
  green), smoke-tests the package, and publishes:
  `develop` → pre-release `v<version>-dev.<run>` (newest 3 kept); `main` → release `v<version>` (once per version).
  This replaces the separate `release.yml` on `v*` tags.
- `develop → main` merges with a merge commit, so `main` no longer requires linear history; features squash into `develop`.
- Package layout: `recipewarden-<version>/{web,cli}` (both self-contained linux-x64), plus `install.sh`, the unit
  file and `build-info.json`. The unit runs `/opt/recipewarden/current/web/RecipeWarden.Web`.

## Consequences
- Every merge to `develop` produces an installable test build; the M7 installer takes these packages.
- Release files are downloaded on servers with plain `curl` (the repository is public since ADR 0005).
- About 2 extra Actions minutes per merge into `develop` or `main`.
