#!/usr/bin/env bash
# Builds the Linux server package (section 9.5, D-05: self-contained linux-x64).
# Usage: build/package.sh <full version, e.g. 0.1.0 or 0.1.0-dev.12> [output dir, default dist]
# Output:
#   recipewarden-<version>-linux-x64.tar.gz   recipewarden-<version>/{web,cli,python,install.sh,recipewarden.service,build-info.json}
#   (python: the bundled CPython and worker from build/python.sh, ADR 0051; needs uv)
#   install.sh  build-info.json  SHA256SUMS.txt
set -euo pipefail
cd "$(dirname "$0")/.."
version="$1"
out="${2:-dist}"
name="recipewarden-$version"
stage="$(mktemp -d)/$name"
commit="${GITHUB_SHA:-$(git rev-parse HEAD 2>/dev/null || echo unknown)}"
branch="${GITHUB_REF_NAME:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo local)}"

publish() {
  dotnet publish "$1" --configuration Release --runtime linux-x64 --self-contained true \
    -p:Version="$version" -p:SourceRevisionId="$commit" -p:DebugType=none -p:GenerateDocumentationFile=false \
    --output "$2"
}

rm -rf "$out"; mkdir -p "$out"
publish src/RecipeWarden.Web "$stage/web"
publish src/RecipeWarden.Cli "$stage/cli"
rm -f "$stage"/web/appsettings.Development.json
build/python.sh package "$stage/python"

cp deploy/install.sh deploy/recipewarden.service "$stage/"
printf '{"version":"%s","branch":"%s","commit":"%s","run":"%s","built_at":"%s"}\n' \
  "$version" "$branch" "$commit" "${GITHUB_RUN_NUMBER:-local}" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$stage/build-info.json"

tar -C "$(dirname "$stage")" -czf "$out/$name-linux-x64.tar.gz" "$name"
cp deploy/install.sh "$stage/build-info.json" "$out/"
(cd "$out" && sha256sum -- * > SHA256SUMS.txt)
rm -rf "$(dirname "$stage")"
ls -l "$out"
