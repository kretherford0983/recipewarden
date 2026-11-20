#!/usr/bin/env bash
# Prints the release version (VersionPrefix in Directory.Build.props), e.g. 0.1.0.
#   --check   also fail unless it is a plain MAJOR.MINOR.PATCH number
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
version=$(sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' "$root/Directory.Build.props" | head -1)
[[ -n $version ]] || { echo "::error::No <VersionPrefix> in Directory.Build.props" >&2; exit 1; }
if [[ ${1:-} == --check && ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "::error::Version '$version' must be MAJOR.MINOR.PATCH" >&2; exit 1
fi
echo "$version"
