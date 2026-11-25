# ADR 0005: Public repository; servers use curl only

- Status: Accepted (2026-11-20)
- Requirements: section 9.5, 9.9, 5.9, FR-INST-01, FR-INST-03

## Context
Servers must be installable and upgradable with tools a minimal Debian/Ubuntu host already has. The product owner
does not install the GitHub CLI on servers. Release files of a private repository can only be downloaded with
authentication, which would mean a token on every server.

## Decision
- The repository is public, like PennyWarden. Release files are fetched with plain
  `curl -fsSLO https://github.com/kretherford0983/recipewarden/releases/download/<tag>/<file>` and verified with `sha256sum`.
- Every server-side command in docs, release notes and scripts uses only bash, coreutils, curl, tar and sha256sum.
  `gh` is used only inside GitHub Actions (publishing and pruning releases).
- The M7 installer follows the same rule (`curl -fsSL …/install.sh | sudo bash -s -- --version <tag>`).

## Consequences
- No token on servers; installs work like PennyWarden's.
- Code, docs and CI logs are public: no secrets, household data, real emails or internal details in the repo
  (gitleaks on every push and in the pre-commit hook; examples use documentation addresses).
- CodeQL is now available for free; adding it is a separate decision. GitHub-hosted Actions minutes are free.
- Self-hosted runners must never be registered: pull requests from forks could otherwise run code on the home lab.
