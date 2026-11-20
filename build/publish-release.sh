#!/usr/bin/env bash
# Publishes the packages in dist/ as a GitHub release (called by build.yml on pushes to develop and main).
#   develop -> pre-release v<version>-dev.<run>      main -> release v<version> (latest)
# Usage: publish-release.sh <full version> <release version>     needs GH_TOKEN, GITHUB_* from Actions
set -euo pipefail
version="$1"; release="$2"
tag="v$version"
pkg="recipewarden-$version-linux-x64.tar.gz"
commit="${GITHUB_SHA::7}"
base="https://github.com/$GITHUB_REPOSITORY/releases/download/$tag"
guide="https://github.com/$GITHUB_REPOSITORY/blob/$tag/docs/test-install.md"

if git rev-parse -q --verify "refs/tags/v$release" >/dev/null; then
  echo "::notice::v$release is already released - packages stay on this workflow run only. Bump <VersionPrefix> for the next release."
  exit 0
fi

changes=$(bash "$(dirname "$0")/release-notes.sh" "$release")

if [[ $GITHUB_REF_NAME == develop ]]; then
  cat > notes.md <<NOTES
Development build from \`develop\` (commit $commit). Download on the **test** host (curl only, section 9.5):
\`\`\`bash
cd /tmp
curl -fsSLO $base/$pkg
curl -fsSLO $base/SHA256SUMS.txt
sha256sum -c --ignore-missing SHA256SUMS.txt
\`\`\`
Then install or upgrade with [docs/test-install.md]($guide) (V=$version).

---

$changes
NOTES
  gh release create "$tag" dist/* --target "$GITHUB_SHA" --prerelease --title "RecipeWarden $version" --notes-file notes.md
else
  cat > notes.md <<NOTES
$changes

---
Release build (commit $commit). Download on the server (curl only, section 9.5):
\`\`\`bash
cd /tmp
curl -fsSLO $base/$pkg
curl -fsSLO $base/SHA256SUMS.txt
sha256sum -c --ignore-missing SHA256SUMS.txt
\`\`\`
Then install or upgrade with [docs/test-install.md]($guide) (V=$version) until the M7 installer.
NOTES
  gh release create "$tag" dist/* --target "$GITHUB_SHA" --latest --title "RecipeWarden $version" --notes-file notes.md
fi
