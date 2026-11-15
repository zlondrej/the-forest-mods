using System;
using System.Collections.Generic;
using HarmonyLib;
using TheForest.Items;
using TheForest.Items.Craft;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using TheForest.Utils;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    internal static class BowUtil
    {
        /// <summary>Same view BowController.CurrentArrowItemView returns (the arrow that will be fired next).</summary>
        public static InventoryItemView CurrentArrow(PlayerInventory inventory, int ammoItemId)
        {
            if (!inventory) return null;
            if (!inventory.InventoryItemViewsCache.TryGetValue(ammoItemId, out var views) || views == null || views.Count == 0)
                return null;
            int index = Mathf.Max(inventory.AmountOf(ammoItemId, false) - 1, 0);
            return index < views.Count ? views[index] : null;
        }

        public static InventoryItemView CurrentArrow(BowController bow) =>
            CurrentArrow(LocalPlayer.Inventory, bow._ammoItemId);
    }

    /// <summary>
    /// Crafting: remember each arrow's base before a cloth/poison/bone/modern upgrade is applied, and store it in
    /// ActiveBonusValue afterwards if the arrow ended up coated.
    /// </summary>
    [HarmonyPatch(typeof(CraftingCog), nameof(CraftingCog.ApplyWeaponStatsUpgrades))]
    internal static class CraftComboPatch
    {
        private static readonly AccessTools.FieldRef<CraftingCog, Dictionary<int, InventoryItemView>> ItemViewsCache =
            AccessTools.FieldRefAccess<CraftingCog, Dictionary<int, InventoryItemView>>("_itemViewsCache");

        private static readonly AccessTools.FieldRef<InventoryItemView, List<InventoryItemView>> MultiViews =
            AccessTools.FieldRefAccess<InventoryItemView, List<InventoryItemView>>("_multiViews");

        private static void Prefix(CraftingCog __instance, int productItemId, WeaponStatUpgrade[] bonuses, bool batched,
            out List<KeyValuePair<InventoryItemView, ArrowBonus>> __state)
        {
            __state = null;
            if (bonuses == null || !Array.Exists(bonuses, b => b != null && (ArrowUtil.IsBase(b._type) || ArrowUtil.IsCoating(b._type))))
                return;
            var cache = ItemViewsCache(__instance);
            if (cache == null || !cache.TryGetValue(productItemId, out var view) || !view) return;

            __state = new List<KeyValuePair<InventoryItemView, ArrowBonus>>();
            // Mirrors which views ApplyWeaponStatsUpgrades touches for ammo bonuses.
            if (batched && view._allowMultiView)
            {
                var multi = MultiViews(view);
                if (multi != null)
                {
                    foreach (var mv in multi)
                        if (mv) __state.Add(new KeyValuePair<InventoryItemView, ArrowBonus>(mv, ArrowUtil.GetBase(mv.Properties)));
                }
            }
            else
            {
                __state.Add(new KeyValuePair<InventoryItemView, ArrowBonus>(view, ArrowUtil.GetBase(view.Properties)));
            }
        }

        private static void Postfix(List<KeyValuePair<InventoryItemView, ArrowBonus>> __state)
        {
            if (__state == null) return;
            foreach (var entry in __state)
            {
                var view = entry.Key;
                if (!view) continue;
                var p = view.Properties;
                if (ArrowUtil.IsCoating(p.ActiveBonus))
                    p.ActiveBonusValue = ArrowUtil.IsBase(entry.Value) ? (float)(int)entry.Value : 0f;
                else if (ArrowUtil.IsBase(p.ActiveBonus) || p.ActiveBonus == ArrowUtil.NoBonus)
                    p.ActiveBonusValue = 0f;
            }
        }
    }

    /// <summary>
    /// Firing: the projectile prefab is picked from the bow's bonus (Burning/Poisoned have no prefab of their own and
    /// fall back to the wooden 'Arrow'). For a coated bone/modern arrow, spawn the base arrow's prefab instead;
    /// BowController.OnAmmoFired still attaches the fire / poison effect to it.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "FireRangedWeapon")]
    internal static class FireBasePrefabPatch
    {
        internal static ArrowBonus? Override;

        private static void Prefix(PlayerInventory __instance)
        {
            Override = null;
            var weapon = __instance.RightHand;
            if (!weapon || weapon.ItemCache == null) return;
            var arrow = BowUtil.CurrentArrow(__instance, weapon.ItemCache._ammoItemId);
            if (!arrow || !ArrowUtil.IsCoating(arrow.ActiveBonus)) return;
            var b = ArrowUtil.GetBase(arrow.Properties);
            if (ArrowUtil.IsBase(b)) Override = b;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Override = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Item.ItemPrefabs), nameof(Item.ItemPrefabs.GetPrefabForBonus))]
    internal static class PrefabForBonusPatch
    {
        private static void Prefix(ref ArrowBonus bonus)
        {
            if (FireBasePrefabPatch.Override.HasValue && ArrowUtil.IsCoating(bonus))
                bonus = FireBasePrefabPatch.Override.Value;
        }
    }

    /// <summary>Show the bone/modern arrow model on the bow for coated bone/modern arrows.</summary>
    [HarmonyPatch(typeof(BowController), "UpdateArrowRenderer")]
    internal static class BowArrowRendererPatch
    {
        private static void Postfix(BowController __instance)
        {
            var arrow = BowUtil.CurrentArrow(__instance);
            if (!arrow || !ArrowUtil.IsCoating(arrow.ActiveBonus)) return;
            var b = ArrowUtil.GetBase(arrow.Properties);
            if (!ArrowUtil.IsBase(b)) return;
            if (__instance._ammoAnimationRenderer) __instance._ammoAnimationRenderer.enabled = false;
            if (__instance._boneAmmoAnimationRenderer) __instance._boneAmmoAnimationRenderer.enabled = b == ArrowBonus.BoneAmmo;
            if (__instance._modernAmmoAnimationRenderer) __instance._modernAmmoAnimationRenderer.enabled = b == ArrowBonus.ModernAmmo;
        }
    }

    /// <summary>
    /// Switching arrow type while a cloth arrow is lit burns the cloth off (vanilla sets the arrow's bonus to none).
    /// A burnt-off bone/modern arrow should stay bone/modern.
    /// </summary>
    [HarmonyPatch(typeof(BowController), "SetActiveBowBonus")]
    internal static class BowSwitchAwayFromLitArrowPatch
    {
        private static void Prefix(BowController __instance, out KeyValuePair<InventoryItemView, ArrowBonus> __state)
        {
            var arrow = BowUtil.CurrentArrow(__instance);
            __state = arrow && arrow.ActiveBonus == ArrowBonus.BurningAmmo
                ? new KeyValuePair<InventoryItemView, ArrowBonus>(arrow, ArrowUtil.GetBase(arrow.Properties))
                : default(KeyValuePair<InventoryItemView, ArrowBonus>);
        }

        private static void Postfix(KeyValuePair<InventoryItemView, ArrowBonus> __state)
        {
            var arrow = __state.Key;
            if (!arrow || arrow.ActiveBonus != ArrowUtil.NoBonus || !ArrowUtil.IsBase(__state.Value)) return;
            arrow.Properties.ActiveBonusValue = 0f;
            arrow.ActiveBonus = __state.Value;
        }
    }

    /// <summary>The fired arrow's inventory slot gets its bonus cleared by vanilla; clear the stored base too.</summary>
    [HarmonyPatch(typeof(BowController), "OnAmmoFired")]
    internal static class OnAmmoFiredCleanupPatch
    {
        private static readonly AccessTools.FieldRef<BowController, InventoryItemView> CurrentAmmo =
            AccessTools.FieldRefAccess<BowController, InventoryItemView>("_currentAmmo");

        private static void Postfix(BowController __instance)
        {
            var ammo = CurrentAmmo(__instance);
            if (ammo && ammo.ActiveBonus == ArrowUtil.NoBonus) ammo.Properties.ActiveBonusValue = 0f;
        }
    }
}
