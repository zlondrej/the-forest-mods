using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// TEMPORARY diagnostics: in multiplayer a held tennis ball could at some point neither be dropped nor thrown. Logs,
    /// for the item in the right hand: each press of the drop button (and whether the game took it as a drop, with what
    /// would block one), and each throw attempt (the hand's throw force against the game's minimum, and whether
    /// ThrowOnRelease let it through). VR build only, by reflection (the input API differs between the builds).
    /// </summary>
    internal static class DropThrowDiagnostics
    {
        private static Func<bool> _dropDown;
        private static int _loggedDropFrame = -1;
        private static FieldInfo _minThrow;

        public static bool Apply(Harmony harmony)
        {
            var input = typeof(TheForest.Utils.Input);
            var actions = input.GetNestedType("Actions", BindingFlags.Public | BindingFlags.NonPublic);
            var drop = actions?.GetField("Drop", BindingFlags.Public | BindingFlags.Static);
            var getButtonDown = drop == null ? null : AccessTools.GetDeclaredMethods(input).FirstOrDefault(m =>
                m.Name == "GetButtonDown" && m.IsStatic && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == drop.FieldType);
            var getDropInput = AccessTools.Method(input, "GetDropInput");
            var throwProjectile = AccessTools.Method(typeof(VRThrowable), "ThrowProjectile");
            _minThrow = AccessTools.Field(typeof(VRThrowable), "MinThrowVelocity");
            if (getButtonDown == null || getDropInput == null || throwProjectile == null) return false;
            var dropAction = drop.GetValue(null);
            _dropDown = () => (bool)getButtonDown.Invoke(null, new[] { dropAction });
            harmony.Patch(getDropInput, postfix: new HarmonyMethod(typeof(DropThrowDiagnostics), nameof(DropInputPostfix)));
            harmony.Patch(throwProjectile, postfix: new HarmonyMethod(typeof(DropThrowDiagnostics), nameof(ThrowPostfix)));
            return true;
        }

        /// <summary>Every frame: the drop button pressed (before the game's 0.2 s hold counts it).</summary>
        public static void Tick()
        {
            if (_dropDown == null || !ForestVR.Enabled || !LocalPlayer.Inventory) return;
            if (_dropDown()) Plugin.Log.LogInfo($"DropThrow: drop button pressed; {State()}");
        }

        private static void DropInputPostfix(bool __result)
        {
            if (!__result || _loggedDropFrame == Time.frameCount || !ForestVR.Enabled || !LocalPlayer.Inventory) return;
            _loggedDropFrame = Time.frameCount;
            Plugin.Log.LogInfo($"DropThrow: drop taken (held 0.2 s); {State()}");
        }

        private static void ThrowPostfix(VRThrowable __instance, bool __runOriginal)
        {
            if (!ForestVR.Enabled) return;
            float min = _minThrow != null ? (float)_minThrow.GetValue(__instance) : 500f;
            string force = __runOriginal ? $"force {__instance.lastThrowForce.magnitude:0} (thrown from {min:0})" : "skipped (trigger still held: the press)";
            Plugin.Log.LogInfo($"DropThrow: throw attempt by {__instance.name}: {force}; {State()}");
        }

        private static string State()
        {
            var inventory = LocalPlayer.Inventory;
            var held = inventory.RightHand;
            var item = held ? held.ItemCache : null;
            var anim = LocalPlayer.AnimControl;
            return $"right hand {(item != null ? item._name : "empty")}, view {inventory.CurrentView}, "
                   + $"right hand locked {inventory.IsSlotLocked(TheForest.Items.Item.EquipmentSlot.RightHand)}, "
                   + $"block drop {inventory.BlockDrop}, item blocks put away {(item != null && item._blockPutAway)}, "
                   + $"VR blocks put away {(item != null && item._VRBlockPutAway)}, throwing {inventory.IsThrowing}, "
                   + $"upside down {(anim && anim.upsideDown)}, held object {(held && held._held ? held._held.name + (held._held.activeInHierarchy ? "" : " (inactive)") : "-")}";
        }
    }
}
