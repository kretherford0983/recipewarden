#!/usr/bin/env bash
# Publishes the packages in dist/ as a GitHub release (called by build.yml on pushes to develop and main).
#   develop -> pre-release v<version>-dev.<run>      main -> release v<version> (latest)
# Usage: publish-release.sh <full version> <release version>     needs GH_TOKEN, GITHUB_* from Actions
set -euo pipefail
version="$1"; release="$2"
tag="v$version"
commit="${GITHUB_SHA::7}"
base="https://github.com/$GITHUB_REPOSITORY/releases/download/$tag"
guide="https://github.com/$GITHUB_REPOSITORY/blob/$tag/docs/install.md"

if git rev-parse -q --verify "refs/tags/v$release" >/dev/null; then
  echo "::notice::v$release is already released - packages stay on this workflow run only. Bump <VersionPrefix> for the next release."
  exit 0
fi

changes=$(bash "$(dirname "$0")/release-notes.sh" "$release")

if [[ $GITHUB_REF_NAME == develop ]]; then
  cat > notes.md <<NOTES
Development build from \`develop\` (commit $commit). Install or upgrade on the **test** host:
\`\`\`bash
curl -fsSL $base/install.sh | sudo bash -s -- --version $tag
\`\`\`
cloudflared in its own container? Add \`--bind <this host's IP> --trusted-proxy <cloudflared IP>\` on the first install.
Options, settings and uninstall: [docs/install.md]($guide).

---

$changes
NOTES
  gh release create "$tag" dist/* --target "$GITHUB_SHA" --prerelease --title "RecipeWarden $version" --notes-file notes.md
else
  cat > notes.md <<NOTES
$changes

---
Release build (commit $commit). Install or upgrade on the server:
\`\`\`bash
curl -fsSL https://github.com/$GITHUB_REPOSITORY/releases/latest/download/install.sh | sudo bash
\`\`\`
Options, settings and uninstall: [docs/install.md]($guide).
NOTES
  gh release create "$tag" dist/* --target "$GITHUB_SHA" --latest --title "RecipeWarden $version" --notes-file notes.md
fi
