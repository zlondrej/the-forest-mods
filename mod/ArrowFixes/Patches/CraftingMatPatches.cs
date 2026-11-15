using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheForest.Items.Craft;
using TheForest.Items.Inventory;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// Moving arrows between the inventory and the crafting mat. The mat only counts arrows; each arrow's upgrade lives
    /// on its display object ("multiview") there. Vanilla mixes arrows up in two places:
    ///  - Putting arrows on the mat moves up to N arrows with the clicked arrow's upgrade, counted and picked by
    ///    ActiveBonus only, and labels all of them with the clicked arrow's properties. With the stored base, clicking a
    ///    flaming bone arrow also took flaming wooden arrows and turned them into flaming bone arrows.
    ///  - Taking arrows back removes N from the count but only the display objects that match the clicked arrow, so
    ///    a non-matching one stays on the mat without an arrow behind it. The next arrows put on the mat reuse it, and
    ///    crafting turned e.g. the 5th of 5 bone arrows into a wooden one.
    /// Here both directions use the full properties (upgrade and stored base).
    /// </summary>
    [HarmonyPatch] // class-level attribute required, or the class processor skips the method patches
    internal static class CraftingMatPatches
    {
        private static readonly AccessTools.FieldRef<CraftingCog, Dictionary<int, InventoryItemView>> ItemViewsCache =
            AccessTools.FieldRefAccess<CraftingCog, Dictionary<int, InventoryItemView>>("_itemViewsCache");

        // Set by InventoryItemView.Update to the clicked view right before it moves arrows to the mat.
        private static readonly FieldInfo CombiningItemId = AccessTools.Field(typeof(InventoryItemView), "CombiningItemId");
        private static readonly FieldInfo CombiningItemProperty = AccessTools.Field(typeof(InventoryItemView), "CombiningItemProperty");

        // ------------------------------------------------------------------ mat -> inventory

        /// <summary>Take back only as many arrows as there are matching display objects on the mat.</summary>
        [HarmonyPatch(typeof(CraftingCog), nameof(CraftingCog.Remove), typeof(int), typeof(int), typeof(ItemProperties))]
        [HarmonyPrefix]
        private static void RemovePrefix(CraftingCog __instance, int itemId, ref int amount, ItemProperties properties, out int __state)
        {
            __state = 0;
            if (properties == ItemProperties.Any || amount <= 0) return;
            var cache = ItemViewsCache(__instance);
            if (cache == null || !cache.TryGetValue(itemId, out var view) || !view || !view._allowMultiView) return;
            int matching = view.AmountOfMultiviewWithProperties(itemId, properties);
            int all = view.AmountOfMultiviewWithProperties(itemId, ItemProperties.Any);
            if (matching >= amount || matching == all) return; // nothing else on the mat would be taken
            Dbg.Log($"mat: taking back {matching} of {amount} requested ({Dbg.Props(properties)}); {all - matching} other arrow(s) stay");
            __state = amount - matching;
            amount = matching;
        }

        /// <summary>Report the arrows left on the mat as "not removed", so the caller adds only the taken ones.</summary>
        [HarmonyPatch(typeof(CraftingCog), nameof(CraftingCog.Remove), typeof(int), typeof(int), typeof(ItemProperties))]
        [HarmonyPostfix]
        private static void RemovePostfix(ref int __result, int __state) => __result += __state;

        // ------------------------------------------------------------------ inventory -> mat

        /// <summary>Count only arrows with the clicked arrow's full properties (e.g. flaming bone, not all flaming).</summary>
        [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.AmountOfItemWithBonus))]
        [HarmonyPostfix]
        private static void AmountOfItemWithBonusPostfix(PlayerInventory __instance, int itemId, ArrowBonus bonus, ref int __result)
        {
            if (!ArrowUtil.IsCoating(bonus) || __instance.CurrentView != PlayerInventory.PlayerViews.Inventory) return;
            if (!(CombiningItemId?.GetValue(null) is int id) || id != itemId) return;
            var clicked = CombiningItemProperty?.GetValue(null) as ItemProperties;
            if (clicked == null || clicked.ActiveBonus != bonus) return;
            if (!__instance.InventoryItemViewsCache.TryGetValue(itemId, out var list) || list == null || list.Count == 0) return;
            var item = list[0].ItemCache;
            if (item == null || item._maxAmount <= 0 || item._maxAmount > list.Count) return; // vanilla didn't count per view
            int n = 0;
            foreach (var v in list)
                if (v && v.gameObject.activeSelf && v.Properties.Match(clicked)) n++;
            if (n != __result) Dbg.Log($"mat: {n} arrow(s) like {Dbg.Props(clicked)} (vanilla counted {__result} with {bonus})");
            __result = n;
        }

        /// <summary>
        /// Before moving arrows, vanilla puts the ones with the clicked upgrade last (those are the ones removed from the
        /// inventory). Sort by the full properties instead, so the arrows that leave are the ones the mat receives.
        /// </summary>
        [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.SortInventoryViewsByBonus))]
        [HarmonyPrefix]
        private static bool SortPrefix(PlayerInventory __instance, InventoryItemView view, ArrowBonus activeBonus, bool setTargetViewFirst)
        {
            if (!view || !ArrowUtil.IsCoating(activeBonus) || view.ActiveBonus != activeBonus) return true;
            if (!__instance.InventoryItemViewsCache.TryGetValue(view._itemId, out var list) || list == null) return true;
            var key = view.Properties;
            int owned = Mathf.Max(__instance.AmountOf(view._itemId, false), 1);
            int moved = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var v = list[i + moved];
                if (v && v.gameObject.activeSelf && !v.Properties.Match(key))
                {
                    list.RemoveAt(i + moved);
                    list.Insert(0, v);
                    moved++;
                }
            }
            if (setTargetViewFirst && owned <= list.Count)
            {
                list.Remove(view);
                list.Insert(owned - 1, view);
            }
            return false;
        }
    }
}
