#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/HardcoreWater/HardcoreWater.csproj"

dotnet build "$PROJECT" -c "$CONFIGURATION"

echo "Built HardcoreWater ($CONFIGURATION)."
echo "Output: $SCRIPT_DIR/HardcoreWater/bin/$CONFIGURATION/Mods/mod"
