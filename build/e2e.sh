#!/usr/bin/env bash
# Runs the Playwright end-to-end tests (docs/testing.md, spec 9.8). They need a browser:
#   CI (job "e2e"):  pwsh tests/RecipeWarden.E2E.Tests/bin/<config>/net10.0/playwright.ps1 install --with-deps chromium
#   locally:         RW_E2E_BROWSER=/path/to/chrome build/e2e.sh   (add RW_E2E_NO_SANDBOX=1 when running as root)
# Usage: build/e2e.sh [--no-build] [--configuration Release]
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test --project tests/RecipeWarden.E2E.Tests "$@"
