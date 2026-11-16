Tweaks for The Forest (flat-screen and VR)
==========================================

INSTALL
  Copy everything in this zip into The Forest's game folder, so that winhttp.dll sits next to TheForest.exe.

  To check it works: start the game, then open BepInEx\LogOutput.log in the game folder. Further down there are
  "Tweaks ... applied" lines.

UNINSTALL
  Just this mod: delete BepInEx\plugins\Tweaks. Everything: see UNINSTALL in ArrowFixes-README.txt.

WHAT IT DOES
  - Rope: sliding down fast (sprint, or the stick fully down with VR auto-run) costs no stamina. Climbing up costs
    stamina as before. To make sliding cost a little, set [RopeSlide] StaminaCost in the config (0 = free,
    1 = vanilla).

  Each tweak can be turned off in BepInEx\config\forestmods.tweaks.cfg (created on first start).

CREDITS
  BepInEx 5.4.23.6 (https://github.com/BepInEx/BepInEx), LGPL-2.1, included unmodified.
