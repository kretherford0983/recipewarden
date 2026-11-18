#!/usr/bin/env bash
# Starts the packaged app from a clean environment and checks it reports the expected version.
# Usage: build/smoke-test.sh <package .tar.gz> <expected version>
set -euo pipefail
package="$1"; expected="$2"
work="$(mktemp -d)"
port=5089
trap 'kill "$pid" 2>/dev/null || true; rm -rf "$work"' EXIT

tar -xzf "$package" -C "$work"
app=$(find "$work" -maxdepth 1 -mindepth 1 -type d | head -1)
mkdir -p "$work/data"
cat > "$work/config.json" <<JSON
{ "Data": { "Directory": "$work/data" },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://127.0.0.1:$port" } } },
  "Serilog": { "WriteTo": [ { "Name": "Console" } ] } }
JSON

# Production environment, configured like an installed service (config path from RECIPEWARDEN_CONFIG).
env -i HOME="$work" PATH=/usr/bin:/bin ASPNETCORE_ENVIRONMENT=Production RECIPEWARDEN_CONFIG="$work/config.json" \
  "$app/web/RecipeWarden.Web" > "$work/web.log" 2>&1 &
pid=$!

for _ in $(seq 1 60); do
  [[ $(curl -fs "http://127.0.0.1:$port/health" 2>/dev/null) == ok ]] && break
  kill -0 "$pid" 2>/dev/null || { cat "$work/web.log"; echo "::error::app exited"; exit 1; }
  sleep 1
done
[[ $(curl -fs "http://127.0.0.1:$port/health") == ok ]] || { cat "$work/web.log"; echo "::error::/health did not answer ok"; exit 1; }

curl -fs "http://127.0.0.1:$port/" | grep -q "RecipeWarden $expected<" \
  || { echo "::error::home page does not show version $expected"; exit 1; }

cli_version=$("$app/cli/recipewarden" --version)
[[ ${cli_version%%+*} == "$expected" ]] || { echo "::error::CLI reports '$cli_version', expected $expected"; exit 1; }
"$app/cli/recipewarden" --config "$work/config.json" health

echo "Smoke test passed: web and CLI report $expected"
