## What & why
<!-- Requirement IDs and acceptance criteria, e.g. FR-REC-01 (AC-01, AC-02), and a one-line summary -->

## Branch flow
- [ ] feature/* · hotfix/* · dependabot/* → **develop** (squash)   ·   develop → **main** (*Create a merge commit*)

## Checklist
- [ ] Tests added/updated and tagged `[Trait("Req", "<ID>")]`; CI green
- [ ] `docs/traceability.md` updated; `docs/spec.md` + changelog line if behaviour changed
- [ ] First change of a new release: `<VersionPrefix>` bumped and `CHANGELOG.md` section added
- [ ] For develop → main: the latest `v<version>-dev.<n>` build installed and accepted on the test host
