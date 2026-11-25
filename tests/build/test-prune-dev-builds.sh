#!/usr/bin/env bash
# build/prune-dev-builds.sh against a fake gh (owner decision 2026-11-25: a release removes every earlier dev build).
# Run from the repository root: bash tests/build/test-prune-dev-builds.sh
set -euo pipefail
root=$(pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
cat > "$work/bin/gh" <<'GH'
#!/usr/bin/env bash
set -euo pipefail
case "$1 $2" in
  # newest first, as the script's jq sorts them; the fake ignores --json/--jq and prints the tags the jq would keep
  "release list") grep -E '^v[0-9]+\.[0-9]+\.[0-9]+-dev\.' "$TAGS" ;;
  "release delete") echo "$3" >> "$DELETED" ;;
  *) echo "unexpected gh $*" >&2; exit 2 ;;
esac
GH
chmod +x "$work/bin/gh"
export TAGS="$work/tags" DELETED="$work/deleted"
printf '%s\n' v1.1.0-dev.31 v1.0.0-dev.30 v1.0.0-dev.29 v1.0.0-dev.28 v1.0.0-dev.27 v0.7.0-dev.15 v0.6.0-dev.14 v1.0.0 > "$TAGS"

: > "$DELETED"
PATH="$work/bin:$PATH" bash "$root/build/prune-dev-builds.sh" 1.0.0 release > /dev/null
expected=$(printf '%s\n' v1.0.0-dev.30 v1.0.0-dev.29 v1.0.0-dev.28 v1.0.0-dev.27 v0.7.0-dev.15 v0.6.0-dev.14)
if [[ $(cat "$DELETED") == "$expected" ]]; then echo "ok - a release removes its own and earlier dev builds, not later ones or releases"
else echo "not ok - release prune removed:"; cat "$DELETED"; exit 1; fi

: > "$DELETED"
PATH="$work/bin:$PATH" bash "$root/build/prune-dev-builds.sh" 1.0.0 dev 3 > /dev/null
if [[ $(cat "$DELETED") == "v1.0.0-dev.27" ]]; then echo "ok - develop keeps the newest three builds of its version"
else echo "not ok - dev prune removed:"; cat "$DELETED"; exit 1; fi
