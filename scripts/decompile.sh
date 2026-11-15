#!/usr/bin/env bash
# Decompile The Forest's game assemblies (flat-screen and VR builds differ) into decompiled/.
# Requires: dotnet tool install ilspycmd --version 9.1.0.7988 --tool-path tools
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder, e.g. export GAME_DIR='/path/to/steamapps/common/The Forest'}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

decompile() {  # <data dir> <output name>
  rm -rf "$ROOT/decompiled/$2"
  "$ROOT/tools/ilspycmd.exe" -p -o "$ROOT/decompiled/$2" \
    "$GAME_DIR/$1/Managed/Assembly-CSharp.dll"
  strip_local_paths "$ROOT/decompiled/$2/Assembly-CSharp.csproj"
}

# ILSpy writes absolute reference paths; make them relative to $(GAME_DIR) (MSBuild reads env vars).
# \x5c is a backslash (literal ones get collapsed when Git Bash passes args to native programs).
strip_local_paths() {
  perl -Mopen=IO,:raw -pi -e 's{<HintPath>[^<]*\x5c(TheForest(?:VR)?_Data\x5cManaged\x5c)}{<HintPath>\$(GAME_DIR)\x5c$1}g' "$1"
}

decompile TheForest_Data Assembly-CSharp
decompile TheForestVR_Data Assembly-CSharp-VR
