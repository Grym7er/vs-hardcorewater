#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODS_PATH="$SCRIPT_DIR/HardcoreWater/bin/Debug/Mods"
RUN_SH="/home/dewet/Games/vintagestory/run.sh"

"$SCRIPT_DIR/build_mod.sh" Debug

exec "$RUN_SH" \
  --tracelog \
  --addModPath "$MODS_PATH" \
  --playStyle creativebuilding \
  --openWorld "creative-modtest"
