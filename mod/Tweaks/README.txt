Tweaks for The Forest (flat-screen and VR)
==========================================

INSTALL
  Copy everything in this zip into The Forest's game folder, so that winhttp.dll sits next to TheForest.exe.

  To check it works: start the game, then open BepInEx\LogOutput.log in the game folder. Further down there are
  "Tweaks ... applied" lines.

UNINSTALL
  Just this mod: delete BepInEx\plugins\Tweaks. Everything: see UNINSTALL in ArrowFixes-README.txt.

WHAT IT DOES
  - Rope: moving down a rope (climbing or sliding down fast) costs no stamina; climbing up costs stamina as before.
    [RopeSlide] StaminaCost in BepInEx\config\forestmods.tweaks.cfg (created on first start) sets the cost as a
    share of vanilla: 0 = free, 0.5 = half, 1 = vanilla (the game is then left unpatched).

CREDITS
  BepInEx 5.4.23.6 (https://github.com/BepInEx/BepInEx), LGPL-2.1, included unmodified.
