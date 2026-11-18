# ADR 0003: Microsoft Testing Platform and coverlet.MTP

- Status: Accepted (M0, 2026-11-17)
- Requirements: NFR-06, AC-97, section 9.8

## Context
The .NET 10 SDK no longer runs Microsoft.Testing.Platform projects (xUnit v3) through the VSTest
target. Section 9.1 lists coverlet without saying which integration.

## Decision
- `global.json` selects `"test": { "runner": "Microsoft.Testing.Platform" }`; test projects use
  xUnit v3's native MTP runner (no Microsoft.NET.Test.Sdk, no VSTest adapter).
- Coverage uses `coverlet.MTP` (MIT) with its `--coverlet-threshold` gate.
- `build/test.sh` runs each test project once with its own gate: Core 90% line (and 85% branch,
  read from the Cobertura report), other projects 80% line.

## Consequences
- `dotnet test --project <path>` is the command; `dotnet test <path>` without `--project` is VSTest-style syntax and fails.
- "80% overall" (NFR-06) is enforced per project rather than as one merged figure. A merged report
  can be added later with ReportGenerator if wanted.
