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
    Not on ropes, where holding sprint while exhausted doesn't regenerate stamina, as in flat-screen.
  - Underwater air bar: shown in front of your view while you hold your breath (vanilla VR never shows it), sharp
    or blurred by the underwater blur. Its distance, height, size and blur can be changed in the config
    ([UnderwaterAirBar] section).
  - Throwing (spear, molotov, bombs, rocks, balls) only throws when you release the throw trigger. Vanilla also
    threw when you pressed it while your hand was moving, e.g. backwards while winding up.
  - Ropes: sprint + stick down starts the fast slide, as in flat-screen (vanilla VR almost never started it).

  Each fix can be turned off in BepInEx\config\forestmods.vrfixes.cfg (created on first start).

CREDITS
  BepInEx 5.4.23.6 (https://github.com/BepInEx/BepInEx), LGPL-2.1, included unmodified.
