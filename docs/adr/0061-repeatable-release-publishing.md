# ADR 0061: Release publishing that survives GitHub errors

- Status: Accepted (owner, 2026-11-25).
- Requirements: section 9.9 (build.yml); amends ADR 0004's publish step.

## Context
The build for v1.0.0-dev.29 uploaded every file, but GitHub answered one upload with an error. `gh release create`
then retried that file, got "ReleaseAsset.name already exists", tried to delete its draft, got a 502 and stopped.
The release was left as a draft, which only the owner can see, so the install command answered 404. Re-running the
job failed the same way. The owner fixed it by publishing the draft by hand.

## Decision
`build/publish-release.sh` publishes in steps that can be repeated:
1. Make a draft with the notes, or reuse the draft an earlier attempt left (an already published release is left alone).
2. Upload each file with `--clobber`, so a file that already arrived is replaced instead of failing, with up to four
   tries 10, 20 and 30 s apart.
3. Check that every file on the release has the size of the file in `dist/`. If one does not, fail and keep the draft.
4. Publish it as a pre-release (develop) or the latest release (main).

A re-run of the failed job therefore finishes a stuck draft. `tests/build/test-publish-release.sh` runs the script
against a fake `gh` in CI (dependency checks job): first publish, the dev.29 failure, a re-run, a short file, and an
already published release.

## Clean-up after a release (owner, 2026-11-25)
As PennyWarden does, publishing release v<version> from main removes every dev pre-release (and its tag) of that
version and of any earlier version, e.g. v1.0.0 removes v1.0.0-dev.*, v0.7.0-dev.15 and v0.6.0-dev.14. Dev builds of
a later version stay, because develop may already be testing the next one. Releases are never removed.
`tests/build/test-prune-dev-builds.sh` checks both modes.

## Consequences
A publish takes a few more API calls. A draft is still possible when GitHub fails four times in a row, but re-running
the job now finishes it.
