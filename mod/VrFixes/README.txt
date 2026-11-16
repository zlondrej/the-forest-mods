VR Fixes for The Forest VR
==========================

INSTALL
  Copy everything in this zip into The Forest's game folder, so that winhttp.dll sits next to TheForestVR.exe
  (Steam: right-click The Forest > Manage > Browse local files). Only does something in TheForestVR.exe; the
  flat-screen game is left as it is.

  To check it works: start the game, then open BepInEx\LogOutput.log in the game folder. Further down there are
  "VR Fixes ... applied" lines.

UNINSTALL
  Just this mod: delete BepInEx\plugins\VrFixes. Everything: see UNINSTALL in ArrowFixes-README.txt.

WHAT IT DOES
  - Auto-run: once you run out of stamina, stamina regenerates while you keep walking with the stick fully forward
    (in vanilla you had to ease off the stick first). To sprint again, ease off and push the stick again, as before.

  Each fix can be turned off in BepInEx\config\forestmods.vrfixes.cfg (created on first start).

CREDITS
  BepInEx 5.4.23.6 (https://github.com/BepInEx/BepInEx), LGPL-2.1, included unmodified.
