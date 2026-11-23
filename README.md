# The Forest mods

[BepInEx](https://github.com/BepInEx/BepInEx) mods for The Forest. They work with the flat-screen game (`TheForest.exe`) and the VR game (`TheForestVR.exe`), which share one install.

| Mod | Game | What it does |
|---|---|---|
| [Arrow Fixes](mod/ArrowFixes/README.txt) | flat-screen and VR | Bone, modern, flaming and poisoned arrows keep their type when crafted, picked up, stored in the arrow basket and fired, also in multiplayer. Switching arrow type cycles through every combination and shows an icon of the selected type. |
| [Meat Badges](mod/MeatBadges/README.txt) | flat-screen and VR | An "L" or "S" badge tells meat and small meat apart in the fire, drying rack and stew pot. |
| [VR Tweaks](mod/VrTweaks/README.txt) | VR only | Scripted scenes in first person instead of the theatre screen. Your body drawn upright under your head, with the outfit's arms and hands, thrown back by explosions and riding the turtle shell as animated. Arms that follow their own controller with left-handed weapons. Building placement by twisting your hand. Fixes for auto-run, ropes, throwing, the underwater air bar, the pause menu and the VR keyboard. |

Each mod's README lists everything it changes and its settings.

## Install

1. Download `ForestMods-<version>.zip` from [Releases](https://github.com/zlondrej/the-forest-mods/releases). It includes BepInEx 5.4.23.6.
2. Unzip it into The Forest's game folder (Steam: right-click The Forest → Manage → Browse local files), so that `winhttp.dll` sits next to `TheForest.exe`.
3. Start the game. Settings are created in `BepInEx\config` on first start; VR Tweaks picks up changes to its settings while the game runs.

To keep only some of the mods, delete the others' folders from `BepInEx\plugins`. Uninstalling everything is described in `ArrowFixes-README.txt`.

`TheForest32.exe` is not supported.

## Building from source

Requirements:
- Windows with The Forest installed
- the .NET SDK (9 or newer)
- Git Bash, or another bash, for the scripts
- [BepInEx 5.4.23.6 (win x64)](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6) extracted to `tools/downloads/BepInEx_win_x64_5.4.23.6/`

The mods compile against the game's own assemblies, so set `GAME_DIR` to the game folder first:

```bash
export GAME_DIR='/path/to/steamapps/common/The Forest'

scripts/install.sh               # build and install BepInEx (if missing) and all mods into the game
scripts/install.sh VrTweaks      # just one mod
scripts/install.sh --uninstall   # remove BepInEx and the mods again
scripts/check-plugin.sh          # check every mod binds against both game builds
scripts/package.sh               # build the release zip into dist/
```

The two game builds run different Unity versions (5.6 flat-screen, 2017.4 VR) and their game code differs slightly. The mods compile against the flat-screen build, and `check-plugin.sh` checks every game member they use against both builds. Members a mod looks up by name at runtime are listed in its `name-lookups.txt`.

## Repository layout

| Path | Contents |
|---|---|
| `mod/ArrowFixes`, `mod/MeatBadges`, `mod/VrTweaks` | The mods (BepInEx plugins). |
| `mod/DevCheats` | For testing only, not in the release zip: god mode, free building, unlimited items and the schematic pages in every game (`scripts/install.sh DevCheats`; delete `BepInEx\plugins\DevCheats` to remove). |
| `mod/ForestEntrypointFix` | A BepInEx preloader patcher, shipped with the mods. Both game builds share one BepInEx config, but they need a different Unity assembly as BepInEx's entry point; without the patcher, mods load in only one of the builds. |
| `scripts/RefCheck` | The member check behind `check-plugin.sh`. |
| `scripts/chalk-icons.html` | Renders the chalk-style icons (the building twist arrow and the meat badges); open it in a browser. |
| `scripts/decompile.sh` | Decompiles the game's code into `decompiled/` for reference. It needs `ilspycmd`, and the output is kept out of the repository. |
| `scripts/*.py`, `scripts/ClothPreview` | Asset inspection and arrow cloth preview tools (Python with UnityPy). |

## Credits

- BepInEx 5.4.23.6 (LGPL-2.1) is included unmodified in the release zip.
- The flame and poison icons in `mod/ArrowFixes/Icons` are royalty-free images.
- The Forest is by Endnight Games. These mods are unofficial and contain none of the game's code or assets.
