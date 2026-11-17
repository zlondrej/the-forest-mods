using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// [Debug] VerboseLogging only: log VR drop and throw attempts, and what drives the rope climbing animation (to see
    /// why the fast rope slide rarely starts in VR).
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
            harmony.Patch(AccessTools.Method(typeof(playerAnimatorControl), "Update"),
                postfix: new HarmonyMethod(typeof(DebugPatches), nameof(RopeStatePostfix)));
        }

        // ------------------------------------------------------------------ rope

        private static readonly AccessTools.FieldRef<playerAnimatorControl, float> VerticalSpeed =
            AccessTools.FieldRefAccess<playerAnimatorControl, float>("verticalSpeed");

        private static readonly AccessTools.FieldRef<playerAnimatorControl, float> YVel =
            AccessTools.FieldRefAccess<playerAnimatorControl, float>("yVel");

        private static float _nextRopeLog;

        /// <summary>
        /// While on a rope, log what drives the climbing animation (twice a second). The fast slide is picked by the
        /// animator from these values; in flat-screen sprint starts it, in VR it rarely does.
        /// </summary>
        private static void RopeStatePostfix(playerAnimatorControl __instance)
        {
            if (__instance != LocalPlayer.AnimControl || !__instance.onRope || Time.time < _nextRopeLog) return;
            _nextRopeLog = Time.time + 0.5f;
            var animator = LocalPlayer.Animator;
            var fpc = LocalPlayer.FpCharacter;
            object startedMoving = null;
            try
            {
                var vr = Traverse.Create(typeof(LocalPlayer)).Field("vrPlayerControl");
                if (!vr.FieldExists()) vr = Traverse.Create(typeof(LocalPlayer)).Property("vrPlayerControl");
                var control = vr.GetValue();
                if (control != null) startedMoving = Traverse.Create(control).Field("_startedMoving").GetValue();
            }
            catch { }
            Plugin.Debug($"rope state: animator vSpeed={(animator ? animator.GetFloat("vSpeed") : float.NaN):0.00}" +
                         $" climbSpeed={(animator ? animator.GetFloat("climbSpeed") : float.NaN):0.00}" +
                         $" verticalSpeed={VerticalSpeed(__instance):0.00} input={(fpc ? fpc.inputVelocity : Vector3.zero)}" +
                         $" run={(fpc ? fpc.run : false)} startedMoving={startedMoving ?? "n/a"} yVel={YVel(__instance):0.0}" +
                         $" stamina={(LocalPlayer.Stats ? LocalPlayer.Stats.Stamina : -1f):0.0}");
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
