using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// [Debug] VerboseLogging only: log VR drop and throw attempts, to see why trophies (animal/creepy heads) don't drop
    /// and why the time bomb doesn't throw.
    /// </summary>
    internal static class DebugPatches
    {
        public static void Apply(Harmony harmony)
        {
            // VR build only (the flat build has no GetDropInput).
            var getDrop = AccessTools.Method(typeof(TheForest.Utils.Input), "GetDropInput");
            if (getDrop != null)
                harmony.Patch(getDrop, postfix: new HarmonyMethod(typeof(DebugPatches), nameof(GetDropInputPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), nameof(PlayerInventory.DropEquipedWeapon)),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(DropPrefix)));
            harmony.Patch(AccessTools.Method(typeof(VRThrowable), "ThrowProjectile"),
                postfix: new HarmonyMethod(typeof(DebugPatches), nameof(ThrowPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), "ThrowProjectile"),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(InventoryThrowPrefix)));
        }

        private static int _lastDropFrame = -1;

        private static void GetDropInputPostfix(bool __result)
        {
            if (!__result || _lastDropFrame == Time.frameCount || !ForestVR.Enabled) return;
            _lastDropFrame = Time.frameCount;
            var inv = LocalPlayer.Inventory;
            if (!inv) return;
            var held = inv.EquipmentSlots != null ? inv.EquipmentSlots[0] : null;
            var item = held ? held.ItemCache : null;
            Plugin.Debug($"drop pressed: view={inv.CurrentView}, right hand={(item != null ? item._name : "empty")}" +
                         (item != null ? $" (max {item._maxAmount}, droppable {item.MatchType(TheForest.Items.Item.Types.Droppable)}, " +
                                         $"VRBlockPutAway {item._VRBlockPutAway}, blockPutAway {item._blockPutAway})" : "") +
                         $", BlockDrop={inv.BlockDrop}, UseAltWorldPrefab={inv.UseAltWorldPrefab}, upsideDown={LocalPlayer.AnimControl?.upsideDown}" +
                         $", carry={LocalPlayer.AnimControl?.carry}, logs={inv.Logs?.Amount}");
        }

        private static void DropPrefix(bool equipPrevious)
        {
            var held = LocalPlayer.Inventory?.EquipmentSlots?[0];
            Plugin.Debug($"drop: dropping {(held && held.ItemCache != null ? held.ItemCache._name : "?")} (equipPrevious {equipPrevious})");
        }

        private static void ThrowPostfix(VRThrowable __instance)
        {
            float force = __instance.lastThrowForce.magnitude;
            Plugin.Debug($"throw: '{__instance.name}' force {force:0} (needs 500 to throw){(force < 500f ? " - too slow, not thrown" : "")}");
        }

        private static void InventoryThrowPrefix(PlayerInventory __instance)
        {
            var held = __instance.EquipmentSlots?[0];
            Plugin.Debug($"throw: inventory throws {(held && held.ItemCache != null ? held.ItemCache._name : "nothing (right hand empty)")}");
        }
    }
}
