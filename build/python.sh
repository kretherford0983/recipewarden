#!/usr/bin/env bash
# The Python worker's environments (ADR 0051).
#   build/python.sh dev             python/.venv from uv.lock and the NLTK model in python/.nltk_data; quick when
#                                   nothing changed (build/test.sh calls it)
#   build/python.sh check           ruff, pytest with its 85% coverage gate, licences and source builds (AC-108),
#                                   pip-audit (SEC-95)
#   build/python.sh package <dir>   a relocatable runtime in <dir> for the Linux package: standalone CPython, the
#                                   locked wheels, rw_worker and the NLTK model, trimmed and precompiled
# Needs uv (https://docs.astral.sh/uv/); CI pins it with astral-sh/setup-uv. Downloads are pinned: the CPython
# build by version (uv checks its SHA-256 against the list built into the pinned uv), the wheels by the hashes in
# uv.lock, and the NLTK model by URL and SHA-256 below.
set -euo pipefail
cd "$(dirname "$0")/.."

UV_VERSION=0.13.9
PYTHON_VERSION=3.13.18 # python-build-standalone 20261110, as listed by uv $UV_VERSION
NLTK_TAGGER_URL=https://raw.githubusercontent.com/nltk/nltk_data/550b6625bcef1f2abff2ff770a5a0d272c9c6b2a/packages/taggers/averaged_perceptron_tagger_eng.zip
NLTK_TAGGER_SHA256=6025f530624335c67d6547d44757b357b4e79bae030a0383e9887a92c1718f0b
cache="${RW_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/recipewarden}"

log() { printf 'python.sh: %s\n' "$*"; }
die() { printf 'python.sh: error: %s\n' "$*" >&2; exit 1; }

need_uv() {
  command -v uv >/dev/null || die "uv is not installed (https://docs.astral.sh/uv/getting-started/installation/)"
  local have; have=$(uv --version | awk '{print $2}')
  [[ $have == "$UV_VERSION" ]] || log "warning: uv $have, CI uses $UV_VERSION"
}

# The NLTK part-of-speech model ingredient-parser needs (spike F2, F12): fetched with curl, checked, never by nltk.
nltk_model() {
  local dest="$1" zip="$cache/averaged_perceptron_tagger_eng-${NLTK_TAGGER_SHA256:0:12}.zip"
  [[ -f "$dest/taggers/averaged_perceptron_tagger_eng/averaged_perceptron_tagger_eng.weights.json" ]] && return 0
  mkdir -p "$cache" "$dest/taggers"
  if ! echo "$NLTK_TAGGER_SHA256  $zip" | sha256sum -c --status 2>/dev/null; then
    log "downloading the NLTK tagger model"
    curl -fsSL --retry 3 -o "$zip.part" "$NLTK_TAGGER_URL"
    echo "$NLTK_TAGGER_SHA256  $zip.part" | sha256sum -c --status || { rm -f "$zip.part"; die "NLTK model checksum mismatch"; }
    mv "$zip.part" "$zip"
  fi
  python3 -I -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$zip" "$dest/taggers"
}

dev() {
  need_uv
  local stamp=python/.venv/.rw-stamp want
  want=$(cat python/uv.lock python/pyproject.toml | sha256sum | cut -c1-16)
  if [[ ! -f $stamp || $(cat "$stamp") != "$want" ]]; then
    log "syncing python/.venv from uv.lock"
    (cd python && uv sync --frozen --python "${PYTHON_VERSION%.*}" --quiet)
    echo "$want" > "$stamp"
  fi
  nltk_model python/.nltk_data
}

check() {
  dev
  cd python
  local py=.venv/bin/python
  .venv/bin/ruff check .
  .venv/bin/ruff format --check .
  NLTK_DATA="$PWD/.nltk_data" "$py" -m pytest --cov --cov-report=term-missing -q
  "$py" tools/check_supply_chain.py
  local req; req=$(mktemp)
  uv export --frozen --no-dev --no-emit-project --format requirements-txt --quiet -o "$req"
  .venv/bin/pip-audit --requirement "$req" --disable-pip --strict --progress-spinner off
  rm -f "$req"
}

package() {
  local dest="${1:?usage: build/python.sh package <dir>}"
  need_uv
  [[ ! -e $dest ]] || die "$dest already exists"
  mkdir -p "$cache/pythons"

  # 1. Standalone CPython (ADR 0051, D1). uv verifies the download against its own pinned list.
  if [[ -n ${RW_PYTHON_RUNTIME:-} ]]; then
    log "using the runtime in RW_PYTHON_RUNTIME"; cp -a "$RW_PYTHON_RUNTIME" "$dest"
  else
    UV_PYTHON_INSTALL_DIR="$cache/pythons" uv python install --quiet "$PYTHON_VERSION"
    local exe; exe=$(UV_PYTHON_INSTALL_DIR="$cache/pythons" uv python find --managed-python "$PYTHON_VERSION")
    cp -a "$(dirname "$(dirname "$(readlink -f "$exe")")")" "$dest"
  fi
  local py="$dest/bin/python3"
  [[ $("$py" -I -c 'import platform; print(platform.python_version())') == "$PYTHON_VERSION" ]] || die "runtime is not $PYTHON_VERSION"

  # 2. Locked wheels only (SEC-95), then the named source-only packages from their hashed archives.
  local req wheels sdists; req=$(mktemp); wheels=$(mktemp); sdists=$(mktemp)
  (cd python && uv export --frozen --no-dev --no-emit-project --format requirements-txt --quiet -o "$req")
  python3 -I - "$req" python/sdist-allowed.txt "$wheels" "$sdists" <<'PY'
import re, sys
req, allowed_file, wheels, sdists = sys.argv[1:]
allowed = {l.split("#")[0].strip().lower() for l in open(allowed_file)} - {""}
blocks, current = [], []
for line in open(req):
    if re.match(r"^[A-Za-z0-9]", line):
        current = [line]; blocks.append(current)
    elif current and (line.startswith(" ") or line.startswith("\t")):
        current.append(line)
with open(wheels, "w") as w, open(sdists, "w") as s:
    for block in blocks:
        name = re.split(r"[=;\s]", block[0], maxsplit=1)[0].lower().replace("_", "-")
        (s if name in allowed else w).write("".join(block))
PY
  local install=(uv pip install --quiet --python "$py" --break-system-packages --require-hashes --no-deps --no-cache)
  "${install[@]}" --only-binary :all: -r "$wheels"
  [[ ! -s $sdists ]] || "${install[@]}" -r "$sdists"
  rm -f "$req" "$wheels" "$sdists"

  # 3. The worker itself, and the NLTK model (SEC-90: nothing is downloaded at run time).
  local site; site=$("$py" -I -c 'import sysconfig; print(sysconfig.get_paths()["purelib"])')
  cp -r python/src/rw_worker "$site/"
  nltk_model "$dest/nltk_data"

  # 4. Trim (NFR-10): tests, tooling and GUI parts of the runtime nothing here uses.
  find "$site" -mindepth 2 -type d \( -name tests -o -name test \) -prune -exec rm -rf {} +
  rm -rf "$site"/pip "$site"/pip-* "$dest"/include "$dest"/share
  local std; std=$("$py" -I -c 'import sysconfig; print(sysconfig.get_paths()["stdlib"])')
  rm -rf "$std"/test "$std"/idlelib "$std"/tkinter "$std"/turtledemo "$std"/ensurepip "$std"/lib2to3 \
    "$dest"/lib/tcl* "$dest"/lib/tk* "$dest"/lib/itcl* "$dest"/lib/thread*
  find "$dest" -type d -name __pycache__ -prune -exec rm -rf {} +

  # 5. Precompile once; -B at run time means nothing is written in the read-only release (SEC-94).
  "$py" -I -m compileall -q -j 0 --invalidation-mode unchecked-hash "$dest/lib" >/dev/null

  # 6. Prove it works offline, and record what was built.
  local selftest; selftest=$(env -i LANG=C.UTF-8 NLTK_DATA="$dest/nltk_data" PYTHONHASHSEED=0 "$py" -I -B -m rw_worker selftest </dev/null)
  grep -q '"passed":true' <<<"$selftest" || die "packaged worker self-test failed: $selftest"
  "$py" -I -c 'import json, sys; e = json.loads(sys.argv[1])["engine"]; e["uv"] = sys.argv[2]; print(json.dumps(e))' \
    "$selftest" "$UV_VERSION" > "$dest/RECIPEWARDEN-PYTHON.json"
  log "packaged $(du -sh "$dest" | cut -f1) at $dest: $(cat "$dest/RECIPEWARDEN-PYTHON.json")"
}

case "${1:-}" in
  dev) dev ;;
  check) check ;;
  package) shift; package "$@" ;;
  *) echo "usage: build/python.sh dev|check|package <dir>" >&2; exit 2 ;;
esac
