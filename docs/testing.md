# Testing and test speed

Tests must stay fast as the suite grows. These rules are set from M0 so they never have to be retrofitted
(PennyWarden reached 45-minute runs before its 2.4.0 CI-speed work; ADR 0006).

## Commands
- Everything, with coverage gates: `build/test.sh` (add `--no-build --configuration Release` after a Release build).
- One project: `dotnet test --project tests/RecipeWarden.Web.Tests`.
- One class or test: `dotnet test --project tests/RecipeWarden.Web.Tests -- --filter-class "*AC_70*"`.
- Projects one after another (easier to read): `TEST_JOBS=1 build/test.sh`.

## How the suite runs in parallel
1. **Projects at the same time.** `build/test.sh` starts every test project at once, collects each log, then prints
   them grouped with a per-project time summary. Coverage gates stay per project (NFR-06).
2. **Test classes on every core.** `tests/xunit.runner.json` (linked into every test project) runs xUnit test
   collections in parallel with `maxParallelThreads: default` (one per core). A class is a collection: tests inside
   one class run in order, different classes run side by side. Tests over 10 s are reported as long-running.
3. **Jobs side by side in CI.** `ci.yml` runs promotion rules, build + tests, dependency checks, the secret scan and
   the installer matrix as separate jobs; the job `test` passes only when all of them did and is the single required
   check, so jobs can be added or split without changing the ruleset.
4. **Migrate once.** `tests/Shared/TestDatabaseTemplate.cs` migrates a database once per test process with the real
   `DatabaseInitializer`; every web host or database test starts from a file copy. One test still migrates from
   empty (`Fresh_empty_database_is_migrated_and_seeded`).
5. **Cheap password hashing in tests only.** Test hosts use one PBKDF2 iteration (`UseFastPasswordHashing`); the
   app's production hashing is unchanged.

## Rules for new tests
- **No shared mutable state between test classes.** Each class gets its own `RecipeWardenFactory` (its own temp data
  directory and database) through `IClassFixture`. Never write to a fixed path, port, environment variable or static.
  If a test truly must change process-wide state (e.g. `Console.SetIn`), restore it in `finally` and keep it short.
- **Split big classes.** Tests in one class run one after another; group by behaviour so classes stay under ~20 s.
- **Use unique data** (e.g. `$"user-{Guid.NewGuid():N}@example.com"`) when tests share a fixture.
- **No sleeps.** Wait on a condition with a timeout; use `TimeProvider` for time-based logic.
- **Real SQLite, temp files.** No in-memory provider; WAL, FTS5 and migrations must be what production runs.

## End-to-end tests (Playwright, from M1)
- Each Playwright worker gets **its own app instance** (own port and temp data directory, started from the
  migrated template), so workers can run in parallel: `workers = cores`. PennyWarden runs E2E with one worker
  against one shared server; RecipeWarden does not copy that.
- Install only the browser: `playwright install chromium` (no `--with-deps`; on Ubuntu runners the libraries are
  present, and an apt mirror outage broke PennyWarden's CI on 2026-11-11). `install-deps` is tried with a short
  timeout and may fail without failing the job.
- E2E runs as **its own CI job** beside build-test. When it passes ~4 minutes, shard it across a matrix
  (`--shard i/n`); the `test` job keeps the ruleset unchanged.

## Time budget
Whole `ci` workflow under 8 minutes (section 9.9). Each job's `timeout-minutes` is about 3-4x its normal run and
noted beside it in `ci.yml`; raise it with the measured normal time when a job grows.
