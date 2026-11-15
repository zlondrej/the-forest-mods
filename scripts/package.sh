#!/usr/bin/env bash
# Build a zip to copy into The Forest's folder: BepInEx 5.4.23.6, the ForestEntrypointFix patcher and the ArrowFixes
# plugin (Release builds), plus README and KNOWN_ISSUES. No config, log or cache files: those are created on first start.
#
#   GAME_DIR=... scripts/package.sh      -> dist/ArrowFixes-<version>-<commit>.zip
set -euo pipefail

GAME_DIR="${GAME_DIR:?Set GAME_DIR to The Forest install folder (needed to compile against the game)}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BEPINEX_SRC="$ROOT/tools/downloads/BepInEx_win_x64_5.4.23.6"
[[ -d "$BEPINEX_SRC" ]] || { echo "Missing $BEPINEX_SRC - download BepInEx_win_x64_5.4.23.6.zip and extract it there"; exit 1; }

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/mod/ArrowFixes/ArrowFixes.csproj" | head -1)"
COMMIT="$(git -C "$ROOT" rev-parse --short HEAD)"
NAME="ArrowFixes-$VERSION-$COMMIT"
STAGE="$ROOT/dist/$NAME"

dotnet build "$ROOT/mod/ForestEntrypointFix" -c Release -nologo -v q
dotnet build "$ROOT/mod/ArrowFixes" -c Release -nologo -v q -p:GameDir="$GAME_DIR"

rm -rf "$STAGE" "$ROOT/dist/$NAME.zip"
mkdir -p "$STAGE/BepInEx/patchers/ForestEntrypointFix" "$STAGE/BepInEx/plugins/ArrowFixes"
cp -r "$BEPINEX_SRC/BepInEx/core" "$STAGE/BepInEx/"
cp "$BEPINEX_SRC"/{winhttp.dll,doorstop_config.ini,.doorstop_version,changelog.txt} "$STAGE/"
cp "$ROOT/mod/ForestEntrypointFix/bin/Release/ForestEntrypointFix.dll" "$STAGE/BepInEx/patchers/ForestEntrypointFix/"
cp "$ROOT/mod/ArrowFixes/bin/Release/ArrowFixes.dll" "$STAGE/BepInEx/plugins/ArrowFixes/"
cp "$ROOT/mod/ArrowFixes/KNOWN_ISSUES.md" "$STAGE/BepInEx/plugins/ArrowFixes/"
cp "$ROOT/mod/README.txt" "$STAGE/ArrowFixes-README.txt"

# Zip the folder's contents (so they unpack straight into the game folder).
powershell.exe -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$STAGE")\\*' -DestinationPath '$(cygpath -w "$ROOT/dist/$NAME.zip")' -Force"
rm -rf "$STAGE"
echo "Created dist/$NAME.zip"
