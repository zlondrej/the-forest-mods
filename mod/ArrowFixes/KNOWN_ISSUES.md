# ArrowFixes – known issues and limitations

## Not yet tested in game

None of the fixes have been played yet. They are only checked statically: the plugin builds, and `scripts/check-plugin.sh` confirms every game member it uses exists in both the flat-screen and VR builds. Please report anything that behaves differently from what is described below.

## Gameplay

- **Arrows changed before installing stay as they are.** Bone arrows that already turned into normal arrows can't be restored. Cloth or poison arrows crafted from bone/modern arrows before installing don't remember their base, so they stay wooden.
- **One coating per arrow.** An arrow can be bone *or* modern, plus cloth *or* poison. Cloth and poison on the same arrow isn't possible.
- **Arrow-type cycling order.** Looking down and pressing Rotate now treats each coating + base as its own type. The order is: plain, cloth, cloth bone, cloth modern, poison, poison bone, poison modern, bone, modern. Types you don't have are skipped.
- **Cloth wrap position on bone/modern arrows is unverified.** The game has one cloth mesh per arrow, made for the wooden arrow. Bone and modern arrows reuse it, both in the inventory and on the bow, so it may not sit exactly on their tip.
- **Fire/poison tint on bone/modern arrows.** Coated bone/modern arrows get the same color tint the game uses for coated wooden arrows (red for cloth, cyan-green for poison), applied to their own model.
- **Modern arrow effects are repositioned.** On modern arrows, fire and poison are moved to the same spot relative to the tip as on wooden arrows, because modern arrows are longer. If they look misplaced, this is why.

## Multiplayer

- **Everyone needs the mod for the multiplayer fixes.**
  - Players without it see modern arrows stuck in enemies as normal arrows, as in vanilla.
  - Players without it get normal arrows back when they pick up a fired bone arrow.
- **Flaming bone/modern arrows look unlit to other players while in flight.** They still set enemies on fire for everyone. Only the wooden arrow has the part that shows the flame to other players, and adding it to other arrows would touch the game's networking.

## VR

- **The flame's position on the held arrow is estimated** from the arrow's size and may look off.
- **Lighting is button-only.** Holding Lighter for 0.5 s lights the arrow. There is no lighter animation, and you don't need to hold the lighter in your hand.
- **Lighting by touching a fire is still broken.** The game's own way of lighting an arrow by holding it to a fire is untouched. It still doesn't work while an arrow is nocked.
- **A lit arrow can lose its flame visually.** If the VR hand removes the arrow object without firing it (for example when you drop the bow), the flame is gone but the arrow still counts as burning. Hold Lighter again to relight it.

## Not fixed

- **Arrows can occasionally gain an upgrade.** An emptied inventory slot keeps its last upgrade. If an arrow is later added without upgrade information (for example a freshly crafted one), it can appear as bone, modern or cloth. Clearing the slot when it empties would wipe the upgrade of arrows moved onto the crafting mat, so this is left as in vanilla.
- **Arrows stuck in enemies disappear after loading a save.** Vanilla behavior.
- **Crossbow bolts are not touched.**

## Behavior changes compared to vanilla

- Switching arrow type while a cloth arrow is lit burns the cloth off. A bone/modern arrow now goes back to bone/modern instead of becoming a plain arrow.
- Firing a cloth or poison bone/modern arrow returns the bone/modern arrow when you pick it up. Vanilla returned a plain arrow.

## Install and compatibility

- **TheForest.exe and TheForestVR.exe only.** `TheForest32.exe` isn't supported, because the bundled BepInEx is the 64-bit build.
- **Steam's "Verify integrity of game files" doesn't remove the mod.** BepInEx and the plugin are extra files, not changes to game files. Use `scripts/install.sh --uninstall` to remove them.
- **Uninstalling is safe.** Coated bone/modern arrows then behave like vanilla coated wooden arrows. The stored base is ignored but kept, so it comes back if you reinstall.
- **Build-specific code.** The plugin is compiled against the flat-screen build and finds the VR-only parts by name at runtime. After any game update, re-run `scripts/check-plugin.sh`.
- **Each fix can be turned off** in `BepInEx/config/forestmods.arrowfixes.cfg`. `BepInEx/LogOutput.log` lists which fixes were applied.
