#!/usr/bin/env bash
# Install BepInEx (if missing), the ForestEntrypointFix patcher and the plugins into The Forest. Works for
# TheForest.exe and TheForestVR.exe (both 64-bit, same folder, one shared BepInEx). TheForest32.exe is not supported
# by this x64 BepInEx build.
#
#   scripts/install.sh              build + install all plugins
#   scripts/install.sh VrTweaks      build + install just these plugins
#   scripts/install.sh --uninstall  remove the plugins and BepInEx (leaves game files untouched)
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder, e.g. export GAME_DIR='/path/to/steamapps/common/The Forest'}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BEPINEX_SRC="$ROOT/tools/downloads/BepInEx_win_x64_5.4.23.6"
PLUGINS=()
[[ "${1:-}" == "--uninstall" ]] || PLUGINS=("$@")
[[ ${#PLUGINS[@]} -gt 0 ]] || PLUGINS=(ArrowFixes VrTweaks)
PATCHER_DIR="$GAME_DIR/BepInEx/patchers/ForestEntrypointFix"
# Everything BepInEx adds to the game root (removed again by --uninstall).
BEPINEX_ROOT_FILES=(winhttp.dll doorstop_config.ini .doorstop_version changelog.txt)

if [[ "${1:-}" == "--uninstall" ]]; then
  rm -rf "$GAME_DIR/BepInEx"
  for f in "${BEPINEX_ROOT_FILES[@]}"; do rm -f "$GAME_DIR/$f"; done
  echo "Removed BepInEx and its plugins from $GAME_DIR"
  exit 0
fi

[[ -f "$GAME_DIR/TheForest.exe" ]] || { echo "Game not found at $GAME_DIR (set GAME_DIR)"; exit 1; }

if [[ ! -f "$GAME_DIR/winhttp.dll" ]]; then
  [[ -d "$BEPINEX_SRC" ]] || { echo "Missing $BEPINEX_SRC - download BepInEx_win_x64_5.4.23.6.zip and extract it there"; exit 1; }
  cp -r "$BEPINEX_SRC/BepInEx" "$GAME_DIR/"
  for f in "${BEPINEX_ROOT_FILES[@]}"; do cp "$BEPINEX_SRC/$f" "$GAME_DIR/"; done
  echo "Installed BepInEx 5.4.23.6"
fi

# Both game builds share this BepInEx install; the patcher makes BepInEx hook the right Unity assembly for each.
dotnet build "$ROOT/mod/ForestEntrypointFix" -c Release -nologo -v q
mkdir -p "$PATCHER_DIR"
cp "$ROOT/mod/ForestEntrypointFix/bin/Release/ForestEntrypointFix.dll" "$PATCHER_DIR/"
echo "Installed ForestEntrypointFix to $PATCHER_DIR"

for plugin in "${PLUGINS[@]}"; do
  dotnet build "$ROOT/mod/$plugin" -c Release -nologo -v q -p:GameDir="$GAME_DIR"
  mkdir -p "$GAME_DIR/BepInEx/plugins/$plugin"
  cp "$ROOT/mod/$plugin/bin/Release/$plugin.dll" "$GAME_DIR/BepInEx/plugins/$plugin/"
  echo "Installed $plugin to $GAME_DIR/BepInEx/plugins/$plugin"
done
