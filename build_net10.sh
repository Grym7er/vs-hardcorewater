#!/usr/bin/env bash
set -euo pipefail



script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_dir="$script_dir/HardcoreWater/bin/Debug/Mods/mod"
rm -rf "$mod_dir"
dotnet build -f net10.0 -c Debug
output_zip="$mod_dir/hcw.zip"
mods_dir="${VINTAGE_STORY_MODS_DIR:-$HOME/.config/VintagestoryData/Mods}"

cd "$mod_dir"
zip -r hcw.zip .
mv "$output_zip" "$mods_dir/hcw.zip"
