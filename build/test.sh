#!/usr/bin/env bash
# Runs every test project once, with the NFR-06 coverage gate for each.
#   Core:            90% line, 85% branch
#   everything else: 80% line
# Usage: build/test.sh [--no-build] [--configuration Release]
set -euo pipefail
cd "$(dirname "$0")/.."

extra=("$@")
out="artifacts/coverage"
rm -rf "$out"; mkdir -p "$out"
status=0

run() {
  local project="$1" include="$2" threshold="$3" type="$4"
  echo "::group::$project"
  if ! dotnet test --project "tests/$project" "${extra[@]}" -- \
      --coverlet --coverlet-output-format cobertura \
      --coverlet-include "$include" \
      --coverlet-exclude-by-attribute GeneratedCodeAttribute --coverlet-exclude-by-attribute CompilerGeneratedAttribute \
      --coverlet-exclude-by-file "**/Migrations/*.cs" \
      --coverlet-threshold "$threshold" --coverlet-threshold-type "$type" --coverlet-threshold-stat total \
      --results-directory "$PWD/$out/$project"; then
    status=1
  fi
  echo "::endgroup::"
}

run RecipeWarden.Core.Tests           "[RecipeWarden.Core]*"           90 line
# Core also needs 85% branch coverage; coverlet takes one threshold per run, so check the report.
branch=$(grep -o -m1 'branch-rate="[0-9.]*"' "$out"/RecipeWarden.Core.Tests/*.cobertura*.xml | head -1 | grep -o '[0-9.]*')
if awk -v b="$branch" 'BEGIN { exit !(b < 0.85) }'; then
  echo "Core branch coverage $branch is below 0.85 (NFR-06)"; status=1
else
  echo "Core branch coverage $branch (>= 0.85)"
fi
run RecipeWarden.Infrastructure.Tests "[RecipeWarden.Infrastructure]*" 80 line
run RecipeWarden.Web.Tests            "[RecipeWarden.Web]*"            80 line
run RecipeWarden.Cli.Tests            "[recipewarden]*"                80 line

exit $status
