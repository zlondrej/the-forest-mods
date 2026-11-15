#!/usr/bin/env bash
# Build the plugins and statically check they bind against both the flat-screen and VR game assemblies.
#
#   scripts/check-plugin.sh                 all plugins
#   scripts/check-plugin.sh VrFixes         just one
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder, e.g. export GAME_DIR='/path/to/steamapps/common/The Forest'}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PLUGINS=("$@")
[[ ${#PLUGINS[@]} -gt 0 ]] || PLUGINS=(ArrowFixes VrFixes)

dotnet build "$ROOT/scripts/RefCheck" -c Release -nologo -v q

status=0
for plugin in "${PLUGINS[@]}"; do
  dotnet build "$ROOT/mod/$plugin" -c Release -nologo -v q -p:GameDir="$GAME_DIR"
  for data in TheForest_Data TheForestVR_Data; do
    echo "== $plugin / $data"
    dotnet "$ROOT/scripts/RefCheck/bin/Release/net9.0/RefCheck.dll" \
      "$ROOT/mod/$plugin/bin/Release/$plugin.dll" \
      "$GAME_DIR/$data/Managed" \
      "$ROOT/mod/$plugin/name-lookups.txt" || status=1
  done
done
exit $status
