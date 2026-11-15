#!/usr/bin/env bash
# Build one zip per plugin to copy into The Forest's folder: BepInEx 5.4.23.6, the ForestEntrypointFix patcher and the
# plugin (Release builds), plus its README (and KNOWN_ISSUES.md if it has one). Each zip works on its own; several
# unpacked into the same game folder share the BepInEx files. No config, log or cache files: those are created on
# first start.
#
#   GAME_DIR=... scripts/package.sh              -> dist/<Plugin>-<version>-<commit>.zip for every plugin
#   GAME_DIR=... scripts/package.sh VrFixes      -> just that one
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder (needed to compile against the game)}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BEPINEX_SRC="$ROOT/tools/downloads/BepInEx_win_x64_5.4.23.6"
[[ -d "$BEPINEX_SRC" ]] || { echo "Missing $BEPINEX_SRC - download BepInEx_win_x64_5.4.23.6.zip and extract it there"; exit 1; }
PLUGINS=("$@")
[[ ${#PLUGINS[@]} -gt 0 ]] || PLUGINS=(ArrowFixes VrFixes)
COMMIT="$(git -C "$ROOT" rev-parse --short HEAD)"

dotnet build "$ROOT/mod/ForestEntrypointFix" -c Release -nologo -v q

for plugin in "${PLUGINS[@]}"; do
  SRC="$ROOT/mod/$plugin"
  VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/$plugin.csproj" | head -1)"
  NAME="$plugin-$VERSION-$COMMIT"
  STAGE="$ROOT/dist/$NAME"

  dotnet build "$SRC" -c Release -nologo -v q -p:GameDir="$GAME_DIR"

  rm -rf "$STAGE" "$ROOT/dist/$NAME.zip"
  mkdir -p "$STAGE/BepInEx/patchers/ForestEntrypointFix" "$STAGE/BepInEx/plugins/$plugin"
  cp -r "$BEPINEX_SRC/BepInEx/core" "$STAGE/BepInEx/"
  cp "$BEPINEX_SRC"/{winhttp.dll,doorstop_config.ini,.doorstop_version,changelog.txt} "$STAGE/"
  cp "$ROOT/mod/ForestEntrypointFix/bin/Release/ForestEntrypointFix.dll" "$STAGE/BepInEx/patchers/ForestEntrypointFix/"
  cp "$SRC/bin/Release/$plugin.dll" "$STAGE/BepInEx/plugins/$plugin/"
  [[ -f "$SRC/KNOWN_ISSUES.md" ]] && cp "$SRC/KNOWN_ISSUES.md" "$STAGE/BepInEx/plugins/$plugin/"
  cp "$SRC/README.txt" "$STAGE/$plugin-README.txt"

  # Zip the folder's contents (so they unpack straight into the game folder).
  powershell.exe -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$STAGE")\\*' -DestinationPath '$(cygpath -w "$ROOT/dist/$NAME.zip")' -Force"
  rm -rf "$STAGE"
  echo "Created dist/$NAME.zip"
done
