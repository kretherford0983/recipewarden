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

# ADR 0061: publish in steps that can be repeated, so a GitHub hiccup never leaves a build stuck as a draft.
# 1. Make (or reuse) a draft. 2. Upload each file, replacing one that already arrived, with retries.
# 3. Check every file's size on GitHub against dist/. 4. Publish. A re-run of the job finishes a stuck draft.
attempts=4
retry() { # retry <command...>: up to $attempts tries, waiting 10, 20, 30 s between them
  local n=1
  until "$@"; do
    if (( n >= attempts )); then return 1; fi
    echo "::warning::attempt $n failed, retrying in $((n * 10)) s: $*"
    sleep $((n * 10))
    n=$((n + 1))
  done
}

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
  kind=(--prerelease)
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
  kind=(--latest)
fi

# 1. The draft. "gh release view" also finds a draft left by an earlier, failed attempt.
if gh release view "$tag" --json isDraft --jq .isDraft >/dev/null 2>&1; then
  if [[ $(gh release view "$tag" --json isDraft --jq .isDraft) == false ]]; then
    echo "::notice::$tag is already published."
    exit 0
  fi
  echo "Finishing the existing draft of $tag"
  retry gh release edit "$tag" --notes-file notes.md --title "RecipeWarden $version"
else
  retry gh release create "$tag" --draft --target "$GITHUB_SHA" "${kind[@]}" --title "RecipeWarden $version" --notes-file notes.md
fi

# 2. The files; --clobber replaces a file that already arrived (the dev.29 failure: "ReleaseAsset.name already exists").
for file in dist/*; do
  retry gh release upload "$tag" "$file" --clobber
done

# 3. Every file is on the release at its full size.
for file in dist/*; do
  name=$(basename "$file")
  want=$(stat -c %s "$file")
  got=$(gh release view "$tag" --json assets --jq ".assets[] | select(.name == \"$name\") | .size")
  if [[ "$got" != "$want" ]]; then
    echo "::error::$name is $got bytes on the release, expected $want; the draft is kept for a re-run."
    exit 1
  fi
done

# 4. Publish.
retry gh release edit "$tag" --draft=false "${kind[@]}"
echo "Published $tag"
