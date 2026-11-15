#!/usr/bin/env bash
# Decompile The Forest's game assembly into decompiled/Assembly-CSharp.
# Requires: dotnet tool install ilspycmd --version 9.1.0.7988 --tool-path tools
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder, e.g. export GAME_DIR='/path/to/steamapps/common/The Forest'}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

rm -rf "$ROOT/decompiled/Assembly-CSharp"
"$ROOT/tools/ilspycmd.exe" -p -o "$ROOT/decompiled/Assembly-CSharp" \
  "$GAME_DIR/TheForest_Data/Managed/Assembly-CSharp.dll"
