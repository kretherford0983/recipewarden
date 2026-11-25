# Worker contract fixtures (ADR 0051, plan 4.2)

Shared by both test suites:
- `python/tests` (pytest) runs each `*.request.json` through the real worker and checks that the answer has the shape
  and the key values of the matching `*.response.json`.
- `RecipeWarden.Infrastructure.Tests` (xUnit) checks that the .NET client accepts every `*.response.json` and rejects
  every file in `invalid/` (SEC-93).

Confidences and library versions in the response files are examples; the tests compare shape, types and the
values named in each test, not every number.
