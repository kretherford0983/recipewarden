#!/usr/bin/env bash
# Runs every test project with its NFR-06 coverage gate, all projects at the same time (docs/testing.md):
#   Core:            90% line, 85% branch
#   everything else: 80% line
# Inside each project xUnit runs test classes in parallel on every core (tests/xunit.runner.json).
# Usage: build/test.sh [--no-build] [--configuration Release]
#   TEST_JOBS=1 build/test.sh ...   run the projects one after another (e.g. to read one log at a time)
set -euo pipefail
cd "$(dirname "$0")/.."

extra=("$@")
out="artifacts/coverage"
logs="artifacts/test-logs"
rm -rf "$out" "$logs"; mkdir -p "$out" "$logs"
jobs="${TEST_JOBS:-0}"   # 0 = all projects at once

# project | coverage include filter | line threshold
projects=(
  "RecipeWarden.Core.Tests|[RecipeWarden.Core]*|90"
  "RecipeWarden.Infrastructure.Tests|[RecipeWarden.Infrastructure]*|80"
  "RecipeWarden.Web.Tests|[RecipeWarden.Web]*|80"
  "RecipeWarden.Cli.Tests|[recipewarden]*|80"
)

run_one() {
  local project="$1" include="$2" threshold="$3" start=$SECONDS rc=0
  dotnet test --project "tests/$project" "${extra[@]}" -- \
    --coverlet --coverlet-output-format cobertura \
    --coverlet-include "$include" \
    --coverlet-exclude-by-attribute GeneratedCodeAttribute --coverlet-exclude-by-attribute CompilerGeneratedAttribute \
    --coverlet-exclude-by-file "**/Migrations/*.cs" \
    --coverlet-threshold "$threshold" --coverlet-threshold-type line --coverlet-threshold-stat total \
    --results-directory "$PWD/$out/$project" > "$logs/$project.log" 2>&1 || rc=$?
  echo "$rc $((SECONDS - start))" > "$logs/$project.status"
}

start=$SECONDS
pids=()
for entry in "${projects[@]}"; do
  IFS='|' read -r project include threshold <<<"$entry"
  run_one "$project" "$include" "$threshold" &
  pids+=($!)
  if (( jobs > 0 && ${#pids[@]} >= jobs )); then wait "${pids[0]}"; pids=("${pids[@]:1}"); fi
done
wait

status=0
for entry in "${projects[@]}"; do
  IFS='|' read -r project _ _ <<<"$entry"
  read -r rc secs < "$logs/$project.status"
  echo "::group::$project (${secs}s, exit $rc)"
  cat "$logs/$project.log"
  echo "::endgroup::"
  if (( rc != 0 )); then status=1; echo "::error::$project failed (exit $rc; 14 = coverage threshold not met)"; fi
done

# Core also needs 85% branch coverage; coverlet takes one threshold per run, so check the report.
branch=$(grep -o -m1 'branch-rate="[0-9.]*"' "$out"/RecipeWarden.Core.Tests/*.cobertura*.xml | head -1 | grep -o '[0-9.]*' || echo 0)
if awk -v b="$branch" 'BEGIN { exit !(b < 0.85) }'; then
  echo "::error::Core branch coverage $branch is below 0.85 (NFR-06)"; status=1
else
  echo "Core branch coverage $branch (>= 0.85)"
fi

echo "Summary (wall clock $((SECONDS - start))s):"
for entry in "${projects[@]}"; do
  IFS='|' read -r project _ _ <<<"$entry"
  read -r rc secs < "$logs/$project.status"
  printf '  %-36s %4ss  %s  %s\n' "$project" "$secs" "$([[ $rc == 0 ]] && echo pass || echo FAIL)" \
    "$(grep -m1 -E '^\s+total:' "$logs/$project.log" | tr -s ' ' || true)"
done
exit $status
