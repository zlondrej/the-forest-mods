VR Tweaks for The Forest VR
===========================

INSTALL
  Copy everything in this zip into The Forest's game folder, so that winhttp.dll sits next to TheForestVR.exe
  (Steam: right-click The Forest > Manage > Browse local files). Only does something in TheForestVR.exe; the
  flat-screen game is left as it is.

  To check it works: start the game, then open BepInEx\LogOutput.log in the game folder. Further down there are
  "VR Tweaks ... applied" lines.

UNINSTALL
  Just this mod: delete BepInEx\plugins\VrTweaks. Everything: see UNINSTALL in ArrowFixes-README.txt.

WHAT IT DOES
  - Immersive camera: scripted scenes (crawling into caves, getting on and off ropes, drinking, the raft, skinning,
    keycard doors, the artifact, story scenes, waking up hanging in the cave, dying) play in first person, seen
    through the character's eyes as in flat-screen, instead of on a screen in a dark room (vanilla VR's "theatre
    mode"). Your headset turns the view; its position follows the character's head, smoothed so the animation's
    small movements don't shake it. Your body is visible (not the head). On a rope you look around and lean
    freely, at the character's height.
  - Pause menu: a short press of the inventory button opens the pause menu where the inventory can't open
    (underwater, hanging in the cave, during animations), as Esc does in flat-screen. Holding it still opens the
    quick select.
  - Auto-run: once you run out of stamina, stamina regenerates while you keep walking with the stick fully forward
    (in vanilla you had to ease off the stick first). To sprint again, ease off and push the stick again, as before.
    Not on ropes, where holding sprint while exhausted doesn't regenerate stamina, as in flat-screen.
  - Underwater air bar: shown in front of your view while you hold your breath (vanilla VR never shows it), sharp
    or blurred by the underwater blur.
  - Throwing (spear, molotov, bombs, rocks, balls) only throws when you release the throw trigger. Vanilla also
    threw when you pressed it while your hand was moving, e.g. backwards while winding up.
  - Ropes: sprint + stick down starts the fast slide, as in flat-screen (vanilla VR almost never started it).
  - Building placement: while you hold the rotate button, twisting the aiming hand like a knob turns the building
    (a quarter turn of the hand turns it half around).

SETTINGS
  In BepInEx\config\forestmods.vrtweaks.cfg (created on first start; read when the game starts):
  - [ImmersiveCamera]: each scene can go back to theatre mode (false), and the smoothing of the first-person view
    can be tuned.
  - [UnderwaterAirBar]: on/off, distance, height, size and blur of the air bar.
  - [Movement] SprintThreshold: how far to push the stick to sprint with auto-run (0.85 = vanilla).
  - [BuildingRotation]: how much the hand twist turns a building (0 = off), and whether only while the rotate button
    is held.
  The other fixes are always on.

CREDITS
  BepInEx 5.4.23.6 (https://github.com/BepInEx/BepInEx), LGPL-2.1, included unmodified.
