#!/usr/bin/env bash
# Build ArrowFixes and statically check it binds against both the flat-screen and VR game assemblies.
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder, e.g. export GAME_DIR='/path/to/steamapps/common/The Forest'}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

dotnet build "$ROOT/mod/ArrowFixes" -c Release -nologo -v q -p:GameDir="$GAME_DIR"
dotnet build "$ROOT/scripts/RefCheck" -c Release -nologo -v q

status=0
for data in TheForest_Data TheForestVR_Data; do
  echo "== $data"
  dotnet "$ROOT/scripts/RefCheck/bin/Release/net9.0/RefCheck.dll" \
    "$ROOT/mod/ArrowFixes/bin/Release/ArrowFixes.dll" \
    "$GAME_DIR/$data/Managed" \
    "$ROOT/mod/ArrowFixes/name-lookups.txt" || status=1
done
exit $status
