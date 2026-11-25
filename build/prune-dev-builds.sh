#!/usr/bin/env bash
# Keeps develop pre-releases tidy (never touches production releases).
#   prune-dev-builds.sh <version> dev [keep]   keep the newest <keep> (default 3) v<version>-dev.* pre-releases
#   prune-dev-builds.sh <version> release      v<version> is out: remove every dev pre-release of v<version> and of
#                                              any earlier version (owner, 2026-11-25, as PennyWarden does); dev
#                                              builds of a later version (develop moved on) stay
set -euo pipefail
version="$1"; mode="$2"; keep="${3:-3}"
mapfile -t prereleases < <(gh release list --limit 200 --json tagName,isPrerelease,createdAt \
  --jq '[.[] | select(.isPrerelease and (.tagName | test("^v[0-9]+\\.[0-9]+\\.[0-9]+-dev\\.")))] | sort_by(.createdAt) | reverse | .[].tagName')

remove() { echo "Removing $1"; gh release delete "$1" --yes --cleanup-tag; }

if [[ $mode == dev ]]; then
  i=0
  for tag in "${prereleases[@]}"; do
    [[ $tag == "v$version-dev."* ]] || continue
    if (( i >= keep )); then remove "$tag"; else echo "Keeping $tag"; fi
    i=$((i + 1))
  done
  exit 0
fi

for tag in "${prereleases[@]}"; do
  base=${tag#v}; base=${base%%-dev.*}
  # sort -V puts the lower version first: remove when the build's version is not newer than the release.
  if [[ $(printf '%s\n%s\n' "$base" "$version" | sort -V | tail -n1) == "$version" ]]; then
    remove "$tag"
  else
    echo "Keeping $tag (a later version than v$version)"
  fi
done
