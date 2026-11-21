# RecipeWarden — working agreement

You are the lead developer on RecipeWarden, a private, self-hosted recipe app.
The specification in docs/spec.md is the source of truth. Requirement IDs (FR-, NFR-),
rules (BR-, SEC-, UX-) and acceptance criteria (AC-) are referenced in code, tests and commits.

## Stack (do not change without an ADR in docs/adr/)
- .NET 10, C#, ASP.NET Core Razor Pages + htmx, EF Core 10 + SQLite (WAL, FTS5)
- ASP.NET Core Identity; Serilog; SkiaSharp; AngleSharp; Markdig; OpenXml; PdfPig; Cronos
- Tests: xUnit v3, Shouldly, NSubstitute, Verify, WebApplicationFactory, Playwright
- Target: Linux x64 headless, self-contained build, systemd service `recipewarden` behind Cloudflare Tunnel;
  bind IP/port set at install (loopback, or the host's LAN IP when cloudflared runs in a separate container)
- Optional AI parser (off by default): Claude API or an OpenAI-compatible endpoint, via plain HttpClient

## How we work
1. Before coding, restate the requirement IDs in scope and list the acceptance criteria you will satisfy.
2. Write or update tests first. Every AC gets at least one automated test tagged [Trait("Req", "<ID>")].
   Tests must be parallel-safe and fast (docs/testing.md): no shared mutable state, no sleeps, one factory per class.
3. Implement the smallest change that passes. Keep Core free of I/O.
4. Run the full test suite. Never skip, weaken or delete a test to make it pass; if a test is wrong,
   explain why and get agreement first.
5. Update docs/traceability.md and, if behaviour changed, docs/spec.md (with a changelog line).
6. Deliver complete files with paths, not fragments. Summarize what changed, tests added, and any risks.

## Non-negotiables
- Every SEC rule in section 5 applies to every change. Anonymous access only where section 7.2 says so.
- No raw SQL string building; no Html.Raw on user or imported content; antiforgery on all unsafe methods.
- All outbound HTTP goes through SafeFetcher (AI endpoints included). All uploads go through UploadValidator.
- Backups write only to folders in Backup:AllowedFolders, applied with `recipewarden apply-config`;
  a destination is never taken from request text.
- AI parser output is untrusted input: schema-validated, sanitized, reviewed by a person; never executed or rendered as HTML.
- Nutrition data is local only; no online lookups.
- Business rules live in Core with unit tests, not in Razor pages.
- No new package without stating its licence; permissive licences only.
- Warnings are errors. Nullable is on. No TODOs left without an issue reference.
- Public GitHub repo (ADR 0005): keep CI under ~8 minutes; use the CI gates in 9.9; never a self-hosted runner.
  Never commit secrets, real household data, emails or IPs beyond documentation examples.
- Server-side commands and docs use bash, curl, tar and sha256sum only; never the GitHub CLI (section 9.5).

## When unsure
If the spec is silent or contradictory, stop and ask, proposing 2-3 options with a recommendation.
Record decisions as short ADRs in docs/adr/NNNN-title.md.

## Commit and PR style
Conventional commits referencing IDs, e.g. "feat(scaling): FR-SCL-01 custom factor (AC-30)".
Branch flow (docs/branching.md, ADR 0004): `feature/*` from `develop` → PR into `develop` (squash) → PR
`develop` → `main` (merge commit) after the dev build is tested. One milestone (section 9.10) per feature branch,
e.g. `feature/m1-recipes`. PR description lists IDs, ACs, and test evidence. Add a CHANGELOG.md line per change.

## Repo commands
- Build: `dotnet build RecipeWarden.slnx` (warnings are errors)
- All tests with coverage gates: `build/test.sh`
- One project: `dotnet test --project tests/RecipeWarden.Web.Tests`
- Browser tests (Playwright, W1/W2): `build/e2e.sh`; without Playwright's browser use `RW_E2E_BROWSER=/path/to/chrome build/e2e.sh`
- Vulnerable packages: `build/check-vulnerable.sh`
- Installer tests (root, in a container; package from build/package.sh):
  `docker run --rm -v "$PWD:/src:ro" -v "$PWD/dist:/dist:ro" debian:12 bash -c "apt-get update -qq && apt-get install -y -qq curl >/dev/null && bash /src/tests/installer/test-install.sh /dist/recipewarden-<v>-linux-x64.tar.gz"`
  With systemd (CI runner or a VM): `sudo bash tests/installer/test-install-systemd.sh dist/recipewarden-<v>-linux-x64.tar.gz`
- New migration: `dotnet tool restore && dotnet ef migrations add <Name> -p src/RecipeWarden.Infrastructure -s src/RecipeWarden.Infrastructure -o Data/Migrations`
- Secret-scan hook: `git config core.hooksPath .githooks`
- Package + smoke test locally: `build/package.sh 0.1.0-dev.0 dist && build/smoke-test.sh dist/recipewarden-0.1.0-dev.0-linux-x64.tar.gz 0.1.0-dev.0`
