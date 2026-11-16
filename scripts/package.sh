#!/usr/bin/env bash
# Build one zip to copy into The Forest's folder: BepInEx 5.4.23.6, the ForestEntrypointFix patcher and all plugins
# (Release builds), plus each plugin's README (and KNOWN_ISSUES.md if it has one). No config, log or cache files: those
# are created on first start.
#
#   GAME_DIR=... scripts/package.sh      -> dist/ForestMods-<commit>.zip
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder (needed to compile against the game)}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BEPINEX_SRC="$ROOT/tools/downloads/BepInEx_win_x64_5.4.23.6"
[[ -d "$BEPINEX_SRC" ]] || { echo "Missing $BEPINEX_SRC - download BepInEx_win_x64_5.4.23.6.zip and extract it there"; exit 1; }
PLUGINS=(ArrowFixes VrFixes)

NAME="ForestMods-$(git -C "$ROOT" rev-parse --short HEAD)"
STAGE="$ROOT/dist/$NAME"
rm -rf "$STAGE" "$ROOT/dist/$NAME.zip"

dotnet build "$ROOT/mod/ForestEntrypointFix" -c Release -nologo -v q
mkdir -p "$STAGE/BepInEx/patchers/ForestEntrypointFix"
cp -r "$BEPINEX_SRC/BepInEx/core" "$STAGE/BepInEx/"
cp "$BEPINEX_SRC"/{winhttp.dll,doorstop_config.ini,.doorstop_version,changelog.txt} "$STAGE/"
cp "$ROOT/mod/ForestEntrypointFix/bin/Release/ForestEntrypointFix.dll" "$STAGE/BepInEx/patchers/ForestEntrypointFix/"

for plugin in "${PLUGINS[@]}"; do
  SRC="$ROOT/mod/$plugin"
  dotnet build "$SRC" -c Release -nologo -v q -p:GameDir="$GAME_DIR"
  mkdir -p "$STAGE/BepInEx/plugins/$plugin"
  cp "$SRC/bin/Release/$plugin.dll" "$STAGE/BepInEx/plugins/$plugin/"
  if [[ -f "$SRC/KNOWN_ISSUES.md" ]]; then cp "$SRC/KNOWN_ISSUES.md" "$STAGE/BepInEx/plugins/$plugin/"; fi
  cp "$SRC/README.txt" "$STAGE/$plugin-README.txt"
done

# Zip the folder's contents (so they unpack straight into the game folder).
powershell.exe -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$STAGE")\\*' -DestinationPath '$(cygpath -w "$ROOT/dist/$NAME.zip")' -Force"
rm -rf "$STAGE"
echo "Created dist/$NAME.zip"
