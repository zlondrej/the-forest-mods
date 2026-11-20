Arrow Fixes for The Forest (flat-screen and VR)
===============================================

INSTALL
  Copy everything in this zip into The Forest's game folder, so that winhttp.dll sits next to TheForest.exe
  (Steam: right-click The Forest > Manage > Browse local files). Works for TheForest.exe and TheForestVR.exe.
  Not for TheForest32.exe.

  To check it works: start the game, then open BepInEx\LogOutput.log in the game folder. Its first line names the game
  you started (TheForest or TheForestVR), and further down there are "Arrow Fixes ... applied" lines.

UNINSTALL
  Just this mod: delete BepInEx\plugins\ArrowFixes.
  Everything: delete from the game folder the BepInEx folder, winhttp.dll, doorstop_config.ini, .doorstop_version,
  changelog.txt and the *-README.txt files. Steam's "Verify integrity of game files" does not remove them.

WHAT IT DOES
  - Fired bone arrows give bone arrows back (and stick in enemies as bone arrows).
  - Flaming (cloth) and poisoned bone/modern arrows keep their bone/modern base: damage, model, and what you get back.
    They get their own cloth wrap and tint, in the inventory and on the bow.
  - Arrow-type switching treats e.g. flaming bone and flaming wooden arrows as separate types, and the prompt shows
    an icon of the selected type (badges for the coating and for bone/modern). Press Rotate while looking down, or in
    VR while looking at the hand you press Rotate with.
  - Arrow basket: Rotate picks the arrow type to put in / take out, the arrows in the basket show their type.
  - Crafting mat keeps arrow types apart (no more arrows turning into other types).
  - Switching arrow type while an arrow is lit puts the flame out and keeps the cloth.
  - VR: holding the Lighter button lights a cloth arrow (a short press still takes out the lighter).
  - Multiplayer: everyone should have the mod; the host needs it for typed arrow baskets.

  Each fix can be turned off in BepInEx\config\forestmods.arrowfixes.cfg (created on first start).
  Known issues and limitations: BepInEx\plugins\ArrowFixes\KNOWN_ISSUES.md

CREDITS
  BepInEx 5.4.23.6 (https://github.com/BepInEx/BepInEx), LGPL-2.1, included unmodified.
