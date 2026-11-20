# Branching, builds and releases

Modelled on PennyWarden's flow, without the `test` branch: `develop` builds are the test builds (ADR 0004).

```
 feature/m1-recipes ──┐
 feature/...        ──┼─► develop ──► main
 dependabot/...     ──┘
 hotfix/...  ─────────────────────► main   (then back-merged into develop)
```

| Branch | Purpose | On every push/merge GitHub Actions… | Install with |
|---|---|---|---|
| `feature/*` | one milestone or change, branched from `develop` | runs `ci` | hand-run **build** (artifact only) |
| `develop` | integration; what you test | **build**: pre-release `v<version>-dev.<run>` (newest 3 per version kept) | test host: the `curl … | sudo bash` command in the release notes (docs/install.md) |
| `main` | production | **build**: release `v<version>` + tag (once per version) | production host |

Code reaches `develop` and `main` only through a pull request whose `ci` checks passed on the up-to-date branch,
so push builds don't re-run the tests. Every package is smoke-tested (started from a clean environment, `/health`
and the version checked for the web app and the CLI) before it is published. To try a feature branch's package
without publishing anything: Actions → build → Run workflow on that branch (runs the tests first; artifact kept 7 days).

Packages in every build: `recipewarden-<version>-linux-x64.tar.gz` (self-contained web app, CLI, `install.sh`,
systemd unit, `build-info.json`), plus `install.sh`, `build-info.json` and `SHA256SUMS.txt`.

## Rules enforced by the pipeline (`build/check-promotion.sh`, check `promotion-rules`)
- Into **develop**: `feature/*`, `hotfix/*`, `dependabot/*`, or a back-merge from `main`.
- Into **main**: `develop` or `hotfix/*`, and the version (`<VersionPrefix>` in `Directory.Build.props`) must not be
  released yet and must have a `## <version>` section in `CHANGELOG.md`.
- A version is released once. After a release, `develop` builds are packaged but not published until the version is bumped.

## Day-to-day flow
1. **Start work**: branch `feature/<milestone-or-change>` from an up-to-date `develop`. The first feature of a new
   release bumps `<VersionPrefix>` and adds `## <version> — unreleased` to `CHANGELOG.md`.
2. **Push** the branch → `ci` runs.
3. **Pull request feature → develop**; merge with **Squash** when green. The build publishes `v<version>-dev.<n>`.
4. **Test**: run the install command from that pre-release's notes on the test host (docs/install.md).
5. **Release**: pull request **develop → main**, merge with **Create a merge commit**. The build publishes `v<version>`
   (notes = that version's CHANGELOG section) and removes its development pre-releases.
6. **Hotfix**: branch `hotfix/<name>` from `main`, bump the patch number, pull request into `main`; then pull request
   `main → develop` (merge commit) so the fix is not lost.

Versions are `MAJOR.MINOR.PATCH`: MINOR for each milestone release while in 0.x, PATCH for fixes; 1.0.0 is M8 (v1.0).

## Dependabot
Weekly pull requests into `develop` for NuGet and GitHub Actions. Major versions of Microsoft, System.CommandLine and
Serilog packages are not proposed: they follow .NET majors and need an ADR (the stack is .NET 10 LTS). Close a pull
request you don't want; add an `ignore` rule to skip a version for good. Dependabot reads its config from `main`.

## One-time GitHub settings
1. **General → Default branch**: `main`. Choose `develop` as the base when opening a feature pull request.
2. **General → Pull Requests**: allow **merge commits** (develop → main) and **squash** (features).
3. **Rules → Rulesets → `protect-main`**, targets **`main` and `develop`**:
   - Restrict deletions; Block force pushes. **No** "Require linear history" (develop → main uses a merge commit).
   - Require a pull request, **0** approvals, allowed merge methods **Merge and Squash**.
   - Require status checks, **up to date** before merging: **`test`** only. It is the `ci` job that passes when
     every other job (promotion rules, build and tests, dependency checks, secrets, installer matrix) passed (ADR 0006).
4. **Actions → General → Workflow permissions**: *Read repository contents* (the release job asks for write itself).
