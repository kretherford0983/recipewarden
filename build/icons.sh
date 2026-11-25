#!/usr/bin/env bash
# ADR 0058: regenerates the favicon, Apple touch icon and web app manifest icons from build/icons/*.svg.
# Needs rsvg-convert (librsvg) and ImageMagick's convert. The outputs are committed; run this only after changing the SVGs.
set -euo pipefail
cd "$(dirname "$0")/.."
src=build/icons
out=src/RecipeWarden.Web/wwwroot
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
cp "$src/favicon.svg" "$out/favicon.svg"
for size in 16 32 48; do rsvg-convert -w "$size" -h "$size" "$src/favicon.svg" -o "$tmp/fav-$size.png"; done
convert "$tmp/fav-16.png" "$tmp/fav-32.png" "$tmp/fav-48.png" "$out/favicon.ico"
rsvg-convert -w 180 -h 180 "$src/maskable.svg" -o "$out/apple-touch-icon.png"
for size in 192 512; do
  rsvg-convert -w "$size" -h "$size" "$src/icon.svg" -o "$out/icons/icon-$size.png"
  rsvg-convert -w "$size" -h "$size" "$src/maskable.svg" -o "$out/icons/maskable-$size.png"
done
echo "icons written to $out"
