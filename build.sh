#!/usr/bin/env bash
set -euo pipefail

# Packaging pipeline (Cake). For regular local compile use ./build_mod.sh
dotnet run --project "./CakeBuild/CakeBuild.csproj" -- "$@"
