#!/usr/bin/env bash
# SEC-81: fail on any high or critical vulnerable package, direct or transitive.
set -euo pipefail
cd "$(dirname "$0")/.."
report=$(dotnet list RecipeWarden.slnx package --vulnerable --include-transitive --format json)
bad=$(echo "$report" | jq -r '
  [.projects[]? | .frameworks[]? | (.topLevelPackages[]?, .transitivePackages[]?)
   | select(any(.vulnerabilities[]?; .severity == "High" or .severity == "Critical"))
   | "\(.id) \(.resolvedVersion)"] | unique | .[]')
if [[ -n "$bad" ]]; then
  echo "High or critical vulnerabilities found:"
  echo "$bad"
  exit 1
fi
echo "No high or critical vulnerable packages."
