using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// VR has no Esc: the pause menu is reached through "Options" on the inventory mat. Where the mat can't open
    /// (underwater, hanging in the cave, during animations), there was no way to the menu at all. Flat-screen's Esc opens
    /// the menu there, in the world view outside the survival book, build mode and the dead camera. Here a short press of
    /// the inventory button opens the pause menu whenever it doesn't open the mat, under the same conditions. (Holding
    /// the button still opens the quick select.)
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory))]
    internal static class MenuButtonPatch
    {
        // VR build only, like the input below (looked up in Prepare, so the flat-screen build doesn't fail on it).
        private static AccessTools.FieldRef<PlayerInventory, float> _inventoryToggledTime;

        private static float _pressedAt = -1f;

        // Input.GetButtonDown/Up(Action) and Input.Actions.Inventory exist in the VR build only (flat-screen uses
        // strings, and the VR string variants skip the VR controller bindings): looked up by reflection.
        private static System.Func<bool> _inventoryDown, _inventoryUp;

        private static bool Prepare()
        {
            var input = typeof(TheForest.Utils.Input);
            var actions = AccessTools.Inner(input, "Actions");
            var inventory = actions == null ? null : AccessTools.Field(actions, "Inventory");
            if (inventory == null) return true; // flat-screen build: the patch stays inert
            var action = inventory.GetValue(null);
            System.Reflection.MethodInfo Find(string name) => AccessTools.Method(input, name, new[] { inventory.FieldType });
            var down = Find("GetButtonDown");
            var up = Find("GetButtonUp");
            if (down == null || up == null || AccessTools.Field(typeof(PlayerInventory), "_inventoryToggledTime") == null) return true;
            _inventoryToggledTime = AccessTools.FieldRefAccess<PlayerInventory, float>("_inventoryToggledTime");
            _inventoryDown = () => (bool)down.Invoke(null, new[] { action });
            _inventoryUp = () => (bool)up.Invoke(null, new[] { action });
            return true;
        }

        /// <summary>
        /// The button would open the mat where the menu fits better: underwater (the game shows a "no inventory
        /// underwater" hint instead) and hanging upside down in the cave (once the lighter is lit the mat opens, upside
        /// down). Skip it, the menu opens instead.
        /// </summary>
        [HarmonyPatch(nameof(PlayerInventory.ToggleInventory))]
        [HarmonyPrefix]
        private static bool ToggleInventoryPrefix(PlayerInventory __instance)
        {
            if (!ForestVR.Enabled || _inventoryUp == null || __instance != LocalPlayer.Inventory || !_inventoryUp()) return true;
            if (__instance.CurrentView == PlayerInventory.PlayerViews.Inventory || (LocalPlayer.Stats && LocalPlayer.Stats.Dead)) return true;
            var water = AirHud.WaterVizNow();
            bool hanging = LocalPlayer.AnimControl && LocalPlayer.AnimControl.upsideDown;
            if (!(water && water.InWater) && !hanging) return true;
            _redirected = true; // the game counted this as a toggle: open the menu without the toggle check
            return false;
        }

        private static bool _redirected;

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        private static void UpdatePostfix(PlayerInventory __instance)
        {
            if (!ForestVR.Enabled || _inventoryUp == null || __instance != LocalPlayer.Inventory) return;
            bool redirected = _redirected;
            _redirected = false;
            if (_inventoryDown()) _pressedAt = Time.realtimeSinceStartup;
            if (!_inventoryUp()) return;
            float pressedFor = _pressedAt < 0f ? 0f : Time.realtimeSinceStartup - _pressedAt;
            _pressedAt = -1f;
            if (pressedFor >= 0.5f) return; // a hold: the quick select
            // The game's own Update ran first: if the press opened the mat (or anything else), the view isn't the world.
            if (__instance.CurrentView != PlayerInventory.PlayerViews.World) return;
            if (!redirected && Time.realtimeSinceStartup - _inventoryToggledTime(__instance) < 0.5f) return; // the mat just closed
            // Flat-screen's conditions for Esc.
            if ((LocalPlayer.Create && LocalPlayer.Create.CreateMode) || (LocalPlayer.PlayerDeadCam && LocalPlayer.PlayerDeadCam.activeSelf) ||
                __instance.QuickSelectGamepadSwitch)
                return;
            Plugin.Debug("menu button: the inventory can't open here, opening the pause menu");
            _restoreScene = true;
            __instance.TogglePauseMenu();
        }

        // ------------------------------------------------------------------ back from the menu, mid-scene

        private static bool _restoreScene, _saved;
        private static int _cullingMask;
        private static bool _locked, _canJump, _kinematic, _gravity;

        /// <summary>
        /// Opening the menu from a scene: remember what closing it resets. Closing sets the eye camera's layers to the
        /// default (a scene's own, e.g. the drag-away scene's, were lost) and unlocks the view and the body's physics
        /// (a scene that had locked them, e.g. hanging, would go on with the body loose).
        /// </summary>
        [HarmonyPatch(typeof(HudGui), nameof(HudGui.TogglePauseMenu))]
        [HarmonyPrefix]
        private static void TogglePauseMenuPrefix(bool on)
        {
            if (!ForestVR.Enabled) return;
            if (!on || !_restoreScene)
            {
                _restoreScene = false;
                return;
            }
            _restoreScene = false;
            var cam = LocalPlayer.MainCam;
            var fp = LocalPlayer.FpCharacter;
            var rb = LocalPlayer.Rigidbody;
            if (!cam || !fp || !rb) return;
            _cullingMask = cam.cullingMask;
            _locked = fp.Locked;
            _canJump = fp.CanJump;
            _kinematic = rb.isKinematic;
            _gravity = rb.useGravity;
            _saved = true;
        }

        [HarmonyPatch(typeof(HudGui), nameof(HudGui.TogglePauseMenu))]
        [HarmonyPostfix]
        private static void TogglePauseMenuPostfix(bool on)
        {
            if (on || !_saved) return;
            _saved = false;
            var cam = LocalPlayer.MainCam;
            var fp = LocalPlayer.FpCharacter;
            var rb = LocalPlayer.Rigidbody;
            if (cam) cam.cullingMask = _cullingMask;
            if (fp)
            {
                fp.Locked = _locked;
                fp.CanJump = _canJump;
            }
            if (rb)
            {
                rb.isKinematic = _kinematic;
                rb.useGravity = _gravity;
            }
            Plugin.Debug("menu button: menu closed, the scene's camera layers, view lock and body physics restored");
        }
    }
}
