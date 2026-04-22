#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOD_DIR="$SCRIPT_DIR/HardcoreWater/bin/Debug/Mods/mod"
OUTPUT_ZIP="$MOD_DIR/hardcorewaterforked.zip"
MODS_DIR="${VINTAGE_STORY_MODS_DIR:-$HOME/.var/app/at.vintagestory.VintageStory/config/VintagestoryData/Mods}"

"$SCRIPT_DIR/build_mod.sh" Debug

cd "$MOD_DIR"
rm -f "$OUTPUT_ZIP"
zip -r "hardcorewaterforked.zip" .
mkdir -p "$MODS_DIR"
cp "$OUTPUT_ZIP" "$MODS_DIR/hardcorewaterforked.zip"

echo "Deployed: $MODS_DIR/hardcorewaterforked.zip"
