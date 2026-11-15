using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HarmonyLib;
using TheForest.Items;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using TheForest.Player;
using UnityEngine;
using TheForest.Utils;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>Verbose logging for in-game troubleshooting ([Debug] VerboseLogging in the plugin config).</summary>
    internal static class Dbg
    {
        public static bool On;

        private static readonly Dictionary<string, string> Last = new Dictionary<string, string>();

        public static void Log(string message)
        {
            if (On) Plugin.Log.LogInfo("[debug] " + message);
        }

        /// <summary>Log only when the message for this key differs from the previous one (for per-frame checks).</summary>
        public static void Changed(string key, string message)
        {
            if (!On) return;
            if (Last.TryGetValue(key, out var previous) && previous == message) return;
            Last[key] = message;
            Log(message);
        }

        public static void Forget(string key) => Last.Remove(key);

        public static string Props(ItemProperties p) =>
            p == null ? "null" : $"{Name(p.ActiveBonus)}/value={p.ActiveBonusValue:0.##}";

        public static string Name(ArrowBonus b) => b == ArrowUtil.NoBonus ? "none" : b.ToString();

        public static string ItemName(int itemId)
        {
            var item = ItemDatabase.ItemById(itemId);
            return item != null && !string.IsNullOrEmpty(item._name) ? item._name : "item#" + itemId;
        }

        /// <summary>"3x none, 2x BurningAmmo+BoneAmmo, ..." for the arrows the player carries.</summary>
        public static string Arrows(int ammoItemId)
        {
            var cache = LocalPlayer.Inventory ? LocalPlayer.Inventory.InventoryItemViewsCache : null;
            if (cache == null || !cache.TryGetValue(ammoItemId, out var list) || list == null) return "(no arrow views)";
            var counts = list.Where(v => v && v.gameObject.activeSelf)
                .GroupBy(v => Props(v.Properties))
                .Select(g => $"{g.Count()}x {g.Key}");
            var text = string.Join(", ", counts.ToArray());
            return text.Length == 0 ? "(none carried)" : text;
        }

        /// <summary>Short call stack (outermost last) to see which game code triggered a change.</summary>
        public static string Caller(int skip = 2, int frames = 6)
        {
            var sb = new StringBuilder();
            var trace = new StackTrace(skip, false);
            for (int i = 0; i < trace.FrameCount && i < frames; i++)
            {
                var m = trace.GetFrame(i).GetMethod();
                if (m == null) continue;
                if (sb.Length > 0) sb.Append(" < ");
                sb.Append(m.DeclaringType != null ? m.DeclaringType.Name + "." : "").Append(m.Name);
            }
            return sb.ToString();
        }
    }

    /// <summary>Debug only: log every arrow-related bonus change with the game code that made it.</summary>
    internal static class DebugBonusChangePatches
    {
        private static bool IsArrowType(ArrowBonus b) => ArrowUtil.IsBase(b) || ArrowUtil.IsCoating(b);

        [HarmonyPatch(typeof(ItemProperties), nameof(ItemProperties.ActiveBonus), MethodType.Setter)]
        [HarmonyPrefix]
        private static void BonusPrefix(ItemProperties __instance, out ArrowBonus __state) => __state = __instance.ActiveBonus;

        [HarmonyPatch(typeof(ItemProperties), nameof(ItemProperties.ActiveBonus), MethodType.Setter)]
        [HarmonyPostfix]
        private static void BonusPostfix(ItemProperties __instance, ArrowBonus __state)
        {
            var now = __instance.ActiveBonus;
            if (now == __state || !(IsArrowType(now) || IsArrowType(__state))) return;
            Dbg.Log($"bonus {Dbg.Name(__state)} -> {Dbg.Props(__instance)}  via {Dbg.Caller()}");
        }

        [HarmonyPatch(typeof(ItemProperties), nameof(ItemProperties.ActiveBonusValue), MethodType.Setter)]
        [HarmonyPrefix]
        private static void ValuePrefix(ItemProperties __instance, out float __state) => __state = __instance.ActiveBonusValue;

        [HarmonyPatch(typeof(ItemProperties), nameof(ItemProperties.ActiveBonusValue), MethodType.Setter)]
        [HarmonyPostfix]
        private static void ValuePostfix(ItemProperties __instance, float __state)
        {
            if (__state == __instance.ActiveBonusValue || !IsArrowType(__instance.ActiveBonus)) return;
            Dbg.Log($"value {__state:0.##} -> {Dbg.Props(__instance)}  via {Dbg.Caller()}");
        }

        private static bool IsArrowItem(int itemId) =>
            Dbg.ItemName(itemId).IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Picking up an arrow from the world (a missed shot, or one stuck in an enemy).</summary>
        [HarmonyPatch(typeof(PickUp), "Collect")]
        [HarmonyPrefix]
        private static void CollectPrefix(PickUp __instance, out bool __state)
        {
            __state = IsArrowItem(__instance._itemId) || (__instance._properties != null && IsArrowType(__instance._properties.ActiveBonus));
            if (__state)
                Dbg.Log($"pickup: '{__instance.name}' gives {Dbg.ItemName(__instance._itemId)} {Dbg.Props(__instance._properties)}");
        }

        [HarmonyPatch(typeof(PickUp), "Collect")]
        [HarmonyPostfix]
        private static void CollectPostfix(PickUp __instance, bool __state)
        {
            if (__state) Dbg.Log($"pickup: now carrying {Dbg.Arrows(__instance._itemId)}");
        }

        /// <summary>A fired projectile: which prefab spawned, what it gives back when picked up, which effects it carries.</summary>
        [HarmonyPatch(typeof(BowController), "OnAmmoFired")]
        [HarmonyPostfix]
        private static void AmmoFiredPostfix(BowController __instance, GameObject Ammo)
        {
            if (!Ammo) return;
            var pickup = Ammo.GetComponentsInChildren<PickUp>(true);
            var gives = pickup.Length > 0 ? Dbg.Props(pickup[0]._properties) : "(no pickup)";
            var effects = new List<string>();
            foreach (Transform child in Ammo.transform)
                if (child.GetComponentInChildren<WeaponBonus>(true)) effects.Add(child.name);
            Dbg.Log($"fired: projectile '{Ammo.name}', pickup gives {gives}, effects [{string.Join(", ", effects.ToArray())}]; " +
                    $"left: {Dbg.Arrows(__instance._ammoItemId)}");
        }
    }
}
