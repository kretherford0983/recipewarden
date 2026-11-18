#!/usr/bin/env bash
# Keeps develop pre-releases tidy (never touches production releases or other versions).
#   prune-dev-builds.sh <version> dev [keep]   keep the newest <keep> (default 3) v<version>-dev.* pre-releases
#   prune-dev-builds.sh <version> release      remove every v<version>-dev.* pre-release (v<version> is out)
set -euo pipefail
version="$1"; mode="$2"; keep="${3:-3}"
[[ $mode == dev ]] || keep=0
mapfile -t tags < <(gh release list --limit 200 --json tagName,isPrerelease,createdAt \
  --jq "[.[] | select(.isPrerelease and (.tagName | startswith(\"v$version-dev.\")))] | sort_by(.createdAt) | reverse | .[].tagName")
for i in "${!tags[@]}"; do
  if (( i >= keep )); then
    echo "Removing ${tags[$i]}"
    gh release delete "${tags[$i]}" --yes --cleanup-tag
  else
    echo "Keeping ${tags[$i]}"
  fi
done
