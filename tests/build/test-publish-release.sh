#!/usr/bin/env bash
# ADR 0061: build/publish-release.sh against a fake gh. Covers a first publish, a GitHub error on an upload that had
# already arrived (the v1.0.0-dev.29 failure), a re-run that finishes a stuck draft, and a size mismatch that keeps
# the draft. Run from the repository root: bash tests/build/test-publish-release.sh
set -euo pipefail
root=$(pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# A fake gh: the release lives in $STATE (draft flag + one file per asset with its size). FAIL_UPLOAD_ONCE makes the
# first upload of that file store it and still fail, as GitHub did. SHORT makes the stored size of that file wrong.
mkdir -p "$work/bin"
cat > "$work/bin/gh" <<'GH'
#!/usr/bin/env bash
set -euo pipefail
cmd="$1 $2"; shift 2
case "$cmd" in
  "release view")
    [[ -f $STATE/exists ]] || { echo "release not found" >&2; exit 1; }
    case "$*" in
      *isDraft*) cat "$STATE/draft" ;;
      *assets*)  name=$(sed -E 's/.*\.name == "([^"]+)".*/\1/' <<<"$*"); cat "$STATE/assets/$name" 2>/dev/null || true ;;
    esac ;;
  "release create")
    touch "$STATE/exists"; echo true > "$STATE/draft"; mkdir -p "$STATE/assets"; echo "create $*" >> "$STATE/log" ;;
  "release upload")
    file="$2"; name=$(basename "$file")
    [[ " $* " == *" --clobber "* ]] || { [[ -f $STATE/assets/$name ]] && { echo "ReleaseAsset.name already exists" >&2; exit 1; }; }
    size=$(stat -c %s "$file"); [[ ${SHORT:-} == "$name" ]] && size=1
    echo "$size" > "$STATE/assets/$name"
    if [[ ${FAIL_UPLOAD_ONCE:-} == "$name" && ! -f $STATE/failed-once ]]; then touch "$STATE/failed-once"; echo "HTTP 502" >&2; exit 1; fi ;;
  "release edit")
    [[ " $* " == *" --draft=false "* ]] && echo false > "$STATE/draft"; echo "edit $*" >> "$STATE/log" ;;
  "release delete") rm -rf "${STATE:?}"/* ;;
  *) echo "unexpected gh $cmd $*" >&2; exit 2 ;;
esac
GH
chmod +x "$work/bin/gh"
# The script waits between retries; the test does not.
printf '#!/usr/bin/env bash\nexit 0\n' > "$work/bin/sleep"; chmod +x "$work/bin/sleep"

run() { # run <scenario> [env...]: publish v9.9.9-dev.1 from a fresh dist/ and state
  local name="$1"; shift
  export STATE="$work/$name/state"
  mkdir -p "$STATE" "$work/$name/dist"
  head -c 5000 /dev/urandom > "$work/$name/dist/recipewarden-9.9.9-dev.1-linux-x64.tar.gz"
  echo "abc  x" > "$work/$name/dist/SHA256SUMS.txt"
  (cd "$work/$name" && env PATH="$work/bin:$PATH" GITHUB_SHA=0123456789abcdef GITHUB_REPOSITORY=o/r GITHUB_REF_NAME=develop "$@" \
    bash "$root/build/publish-release.sh" 9.9.9-dev.1 9.9.9 >"$work/$name/out" 2>&1)
}
pass() { echo "ok - $1"; }
fail() { echo "not ok - $1"; cat "$work/$2/out"; exit 1; }

# The release-notes script reads git; give it what it needs.
git -C "$root" rev-parse HEAD >/dev/null

if run first && [[ $(cat "$work/first/state/draft") == false ]]; then
  pass "a first publish ends published"
else fail "first publish" first; fi

if run hiccup FAIL_UPLOAD_ONCE=recipewarden-9.9.9-dev.1-linux-x64.tar.gz \
  && [[ $(cat "$work/hiccup/state/draft") == false ]] && grep -q "retrying" "$work/hiccup/out"; then
  pass "an upload error on a file that arrived is retried and the release published"
else fail "upload hiccup" hiccup; fi

# A draft left by an earlier failed attempt, with the files already there.
export STATE="$work/rerun/state"; mkdir -p "$STATE/assets"; touch "$STATE/exists"; echo true > "$STATE/draft"
if run rerun && [[ $(cat "$work/rerun/state/draft") == false ]] && ! grep -q "^create" "$work/rerun/state/log" \
  && grep -q "Finishing the existing draft" "$work/rerun/out"; then
  pass "a re-run finishes a stuck draft instead of failing"
else fail "re-run" rerun; fi

if run short SHORT=SHA256SUMS.txt; then fail "a short file must fail" short; fi
if [[ $(cat "$work/short/state/draft") == true ]] && grep -q "expected" "$work/short/out"; then
  pass "a file with the wrong size keeps the draft and fails"
else fail "size check" short; fi

export STATE="$work/published/state"; mkdir -p "$STATE/assets"; touch "$STATE/exists"; echo false > "$STATE/draft"
if run published && grep -q "already published" "$work/published/out"; then
  pass "an already published release is left alone"
else fail "published" published; fi
