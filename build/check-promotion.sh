#!/usr/bin/env bash
# Pull-request rules for the branch flow (docs/branching.md):
#   feature/* | hotfix/* | dependabot/* -> develop -> main        hotfix/* -> main (then back-merged into develop)
# Usage: check-promotion.sh <base branch> <head branch>
set -euo pipefail
base="$1"; head="$2"
root="$(cd "$(dirname "$0")/.." && pwd)"
fail() { echo "::error::$1"; exit 1; }

case "$base" in
  develop) [[ $head =~ ^(feature|hotfix|dependabot)/ || $head == main ]] \
             || fail "Only feature/*, hotfix/*, dependabot/* (or a back-merge from main) may be merged into develop (got '$head')." ;;
  main)    [[ $head == develop || $head =~ ^hotfix/ ]] \
             || fail "Only develop (or hotfix/*) may be merged into main (got '$head')." ;;
  *)       echo "No promotion rule for base '$base'."; exit 0 ;;
esac

version=$(bash "$root/build/version.sh" --check)

if [[ $base == main ]]; then
  git fetch --tags --quiet origin 2>/dev/null || true
  if git rev-parse -q --verify "refs/tags/v$version" >/dev/null; then
    fail "v$version is already released. Bump <VersionPrefix> in Directory.Build.props and add a CHANGELOG section before merging into main."
  fi
  grep -q "^## $version\( \|$\)" "$root/CHANGELOG.md" || fail "CHANGELOG.md has no '## $version' section."
fi

echo "OK: $head -> $base (version $version)"
