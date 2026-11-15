# ArrowFixes – known issues and limitations

## Not yet tested in game

Played briefly in VR only; the flat-screen build and multiplayer are only checked statically (the plugin builds, and `scripts/check-plugin.sh` confirms every game member it uses exists in both builds). Please report anything that behaves differently from what is described below. With `[Debug] VerboseLogging = true` in the plugin config, `BepInEx/LogOutput.log` records crafting, cycling, firing, pickups and lighting in detail.

## Gameplay

- **Arrows changed before installing stay as they are.** Bone arrows that already turned into normal arrows can't be restored. Cloth or poison arrows crafted from bone/modern arrows before installing don't remember their base, so they stay wooden.
- **One coating per arrow.** An arrow can be bone *or* modern, plus cloth *or* poison. Cloth and poison on the same arrow isn't possible.
- **Arrow-type cycling order.** Looking down and pressing Rotate now treats each coating + base as its own type, grouped by arrow: wooden, wooden cloth, wooden poison, bone, bone cloth, bone poison, modern, modern cloth, modern poison. Types you don't have are skipped.
- **Bone/modern fire arrows use a generated cloth wrap.** The game has a single cloth mesh, shaped for the wooden arrow, and it didn't fit the bone or modern models. The mod builds its own wrap for each (over the head on bone arrows, like the wooden one; behind the insert on modern arrows) and uses the game's cloth material. To check the shapes without the game, run `scripts/ClothPreview` and `scripts/render_cloth_preview.py` (see the top of the script).
- **Fire/poison tint on bone/modern arrows.** Coated bone/modern arrows get the same color tint the game uses for coated wooden arrows (red for cloth, cyan-green for poison), applied to their own model.
- **Fire and poison are moved on bone/modern arrows.** The game places them for the wooden arrow. On the bow, the flame sits at the centre of the bone/modern arrow's cloth wrap, like it sits on the wooden arrow's cloth. On a fired modern arrow, which is longer, fire and poison are moved to the same spot relative to the tip as on a wooden one.

## Multiplayer

- **Everyone needs the mod for the multiplayer fixes.**
  - Players without it see modern arrows stuck in enemies as normal arrows, as in vanilla.
  - Players without it get normal arrows back when they pick up a fired bone arrow.
- **Flaming bone/modern arrows look unlit to other players while in flight.** They still set enemies on fire for everyone. Only the wooden arrow has the part that shows the flame to other players, and adding it to other arrows would touch the game's networking.

## VR

- **Switching arrow type needs a steep look down.** As in vanilla, look down more than about 58° below the horizon and press Rotate (left trigger by default). The switch prompt may not show in VR; you hear a whoosh when it switches.
- **Lighting is button-only.** Holding Lighter for 0.5 s lights the arrow. There is no lighter animation, you don't need to hold the lighter in your hand, and with a cloth arrow on the bow the button doesn't take out the lighter.
- **Lighting by touching a fire is still broken.** The game's own way of lighting an arrow by holding it to a fire is untouched. It still doesn't work while an arrow is nocked.

## Not fixed

- **Arrows can occasionally gain an upgrade.** An emptied inventory slot keeps its last upgrade. If an arrow is later added without upgrade information (for example a freshly crafted one), it can appear as bone, modern or cloth. Clearing the slot when it empties would wipe the upgrade of arrows moved onto the crafting mat, so this is left as in vanilla.
- **Arrows stuck in enemies disappear after loading a save.** Vanilla behavior.
- **Crossbow bolts are not touched.**
- **The arrow basket holds one arrow type at a time.** You can't choose which type to put in or take out. Not looked into yet.

## Behavior changes compared to vanilla

- Switching arrow type while a cloth arrow is lit burns the cloth off. A bone/modern arrow now goes back to bone/modern instead of becoming a plain arrow. This now also happens when switching to another cloth type (e.g. cloth bone to cloth modern); before, the flame moved over to the other arrow.
- The crafting mat keeps arrow types apart. Clicking an arrow moves only arrows of exactly that type (e.g. cloth bone, not every cloth arrow) onto the mat, and taking arrows back leaves the other types on the mat. In vanilla a mixed mat could turn arrows into another type.
- Firing a cloth or poison bone/modern arrow returns the bone/modern arrow when you pick it up. Vanilla returned a plain arrow.

## Install and compatibility

- **TheForest.exe and TheForestVR.exe only.** `TheForest32.exe` isn't supported, because the bundled BepInEx is the 64-bit build.
- **Steam's "Verify integrity of game files" doesn't remove the mod.** BepInEx and the plugin are extra files, not changes to game files. Use `scripts/install.sh --uninstall` to remove them.
- **Uninstalling is safe.** Coated bone/modern arrows then behave like vanilla coated wooden arrows. The stored base is ignored but kept, so it comes back if you reinstall.
- **Build-specific code.** The plugin is compiled against the flat-screen build and finds the VR-only parts by name at runtime. After any game update, re-run `scripts/check-plugin.sh`.
- **Each fix can be turned off** in `BepInEx/config/forestmods.arrowfixes.cfg`. `BepInEx/LogOutput.log` lists which fixes were applied.
