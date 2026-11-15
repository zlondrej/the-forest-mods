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

## Arrow basket

- **Choosing the type.** Looking at an arrow basket, Rotate cycles through the arrow types you carry and the types in the basket; the selected type's icon is shown next to the take/put prompt. Put puts in an arrow of that type (if you carry one), take takes one out (if the basket has one). The icon's position next to the prompt is untested and may need adjusting.
- **Arrows in the basket show their type** (model, cloth, tint). Baskets you haven't looked at yet update once your inventory is loaded.
- **Multiplayer needs the mod on the host.** The host keeps the basket's arrow types and saves them. Players with the mod use typed baskets as soon as the host answers (when they first look at a basket); with a host without the mod, the basket works as in vanilla (one generic arrow type). Players without the mod can still use a basket the vanilla way; what they put in counts as plain arrows, and they take out plain arrows. Dedicated servers need the mod installed too.
- **Arrows put in before installing** count as plain arrows, unless the game had kept their type (single player).

## Multiplayer

- **Everyone needs the mod for the multiplayer fixes.**
  - Players without it see modern arrows stuck in enemies as normal arrows, as in vanilla.
  - Players without it get normal arrows back when they pick up a fired bone arrow.
- **Flaming bone/modern arrows look unlit to other players while in flight.** They still set enemies on fire for everyone. Only the wooden arrow has the part that shows the flame to other players, and adding it to other arrows would touch the game's networking.

## VR

- **Switching arrow type needs a steep look down.** As in vanilla, look down more than about 58° below the horizon and press Rotate (left trigger by default). The switch prompt may not show in VR; you hear a whoosh when it switches.
- **Lighting is button-only.** Holding Lighter for 0.5 s lights the arrow, without taking out the lighter: there is no lighter animation and you don't need the lighter in your hand. A short press still takes out the lighter, but with a cloth arrow on the bow it comes out when you release the button, not when you press it (the press could still turn into a hold). Flat-screen is unchanged: there, pressing L takes out the lighter right away and holding it also lights the arrow with the lighter.
- **Lighting by touching a fire is still broken.** The game's own way of lighting an arrow by holding it to a fire is untouched. It still doesn't work while an arrow is nocked.

## Not fixed

- **Arrows can occasionally gain an upgrade.** An emptied inventory slot keeps its last upgrade. If an arrow is later added without upgrade information (for example a freshly crafted one), it can appear as bone, modern or cloth. Clearing the slot when it empties would wipe the upgrade of arrows moved onto the crafting mat, so this is left as in vanilla.
- **Arrows stuck in enemies disappear after loading a save.** Vanilla behavior.
- **Crossbow bolts are not touched.**

## Behavior changes compared to vanilla

- Switching arrow type while a cloth arrow is lit just puts the flame out; the arrow keeps its cloth and can be lit again, like when you put the bow away. In vanilla the cloth burned off (the arrow became a plain arrow), and switching between two cloth types would have moved the flame to the other arrow. If something else still burns the cloth off a lit bone/modern arrow, it goes back to bone/modern instead of becoming a plain arrow.
- The crafting mat keeps arrow types apart. Clicking an arrow moves only arrows of exactly that type (e.g. cloth bone, not every cloth arrow) onto the mat, and taking arrows back leaves the other types on the mat. In vanilla a mixed mat could turn arrows into another type.
- Firing a cloth or poison bone/modern arrow returns the bone/modern arrow when you pick it up. Vanilla returned a plain arrow.

## Install and compatibility

- **TheForest.exe and TheForestVR.exe only.** `TheForest32.exe` isn't supported, because the bundled BepInEx is the 64-bit build.
- **Both builds share one BepInEx install.** The flat-screen game (Unity 5.6) and the VR game (Unity 2017.4) need BepInEx to hook a different Unity assembly, but BepInEx saves that choice in `BepInEx.cfg` for whichever build starts first, so the other build silently loaded no plugins. The small `ForestEntrypointFix` patcher (installed to `BepInEx/patchers`) sets it for the build that is starting. If the mod seems inactive, check that `BepInEx/LogOutput.log` was written by the build you played (its first line names `TheForest` or `TheForestVR`).
- **Steam's "Verify integrity of game files" doesn't remove the mod.** BepInEx and the plugin are extra files, not changes to game files. Use `scripts/install.sh --uninstall` to remove them.
- **Uninstalling is safe.** Coated bone/modern arrows then behave like vanilla coated wooden arrows. The stored base is ignored but kept, so it comes back if you reinstall.
- **Build-specific code.** The plugin is compiled against the flat-screen build and finds the VR-only parts by name at runtime. After any game update, re-run `scripts/check-plugin.sh`.
- **Each fix can be turned off** in `BepInEx/config/forestmods.arrowfixes.cfg`. `BepInEx/LogOutput.log` lists which fixes were applied.
