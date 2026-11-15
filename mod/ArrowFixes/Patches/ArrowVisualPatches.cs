using System;
using System.Collections.Generic;
using HarmonyLib;
using TheForest.Items.Inventory;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;
using Object = UnityEngine.Object;

namespace ArrowFixes
{
    /// <summary>
    /// How arrows look in the inventory / crafting mat (from the game assets):
    ///  - ActiveBonusListener(bone=26 / modern=29) shows the 'BoneAmmo' / 'ModernArrow' mesh;
    ///  - ActiveBonusListenerMulti([26, 29], inverse) hides the wooden mesh for bone/modern;
    ///  - ActiveBonusListener(cloth=10) shows the separate 'ArrowCloth' mesh;
    ///  - ActiveBonusListenerMat tints the wooden mesh: cloth = red, poison = cyan-green (same texture, _Color only).
    /// All of them match a single ActiveBonus, so a coated bone/modern arrow looked like a coated wooden one.
    /// Here a coated arrow matches both its coating and its base, and the base mesh gets the coating's tint. The cloth
    /// wrap mesh is shaped for the wooden arrow, so it stays hidden on bone/modern models.
    /// </summary>
    [HarmonyPatch] // class-level attribute required, or the class processor skips the method patches
    internal static class ArrowVisualPatches
    {
        private static readonly AccessTools.FieldRef<ItemProperties, Action<ArrowBonus>> ActiveBonusChangedEvent =
            AccessTools.FieldRefAccess<ItemProperties, Action<ArrowBonus>>("ActiveBonusChanged");

        // Vanilla BowFireMat / BowPoisonMat colors, used when no ActiveBonusListenerMat is reachable.
        private static readonly Color FallbackFireTint = new Color(1f, 0.54f, 0.54f, 1f);
        private static readonly Color FallbackPoisonTint = new Color(0.54f, 1f, 0.9f, 1f);
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static MaterialPropertyBlock _block;

        [HarmonyPatch(typeof(ActiveBonusListener), "OnActiveBonusChanged")]
        [HarmonyPrefix]
        private static bool ListenerPrefix(ActiveBonusListener __instance)
        {
            var view = __instance._itemView;
            if (!view) return true; // bow/held listeners: unchanged
            var p = view.Properties;
            var b = ArrowUtil.GetBase(p);
            bool combo = ArrowUtil.IsCoating(p.ActiveBonus) && ArrowUtil.IsBase(b);
            bool targetIsBase = ArrowUtil.IsBase(__instance._bonusToActivate);

            if (!combo)
            {
                if (targetIsBase) SetTint(__instance._targetGOs, __instance._targetObjects, null);
                return true;
            }

            // The cloth wrap mesh is shaped for the wooden arrow: leave it off on bone/modern models (the tint shows the coating).
            bool flag = __instance._bonusToActivate == b ||
                        (__instance._bonusToActivate == p.ActiveBonus && __instance._bonusToActivate != ArrowBonus.BurningAmmo);
            if (__instance._inverse) flag = !flag;
            Apply(__instance._targetGOs, __instance._targetObjects, flag);
            if (targetIsBase) SetTint(__instance._targetGOs, __instance._targetObjects, flag ? TintFor(view, p.ActiveBonus) : (Color?)null);
            return false;
        }

        [HarmonyPatch(typeof(ActiveBonusListenerMulti), "OnActiveBonusChanged")]
        [HarmonyPrefix]
        private static bool MultiListenerPrefix(ActiveBonusListenerMulti __instance)
        {
            var view = __instance._itemView;
            if (!view) return true;
            var p = view.Properties;
            var b = ArrowUtil.GetBase(p);
            if (!ArrowUtil.IsCoating(p.ActiveBonus) || !ArrowUtil.IsBase(b)) return true;

            bool flag = Array.IndexOf(__instance._bonusToActivate, p.ActiveBonus) >= 0 ||
                        Array.IndexOf(__instance._bonusToActivate, b) >= 0;
            if (__instance._inverse) flag = !flag;
            Apply(__instance._targetGOs, __instance._targetObjects, flag);
            return false;
        }

        /// <summary>
        /// ItemProperties.Copy and the crafting patch set ActiveBonus first and the stored base second, so listeners
        /// ran before the base was known. Re-notify them when the base of a coated item changes.
        /// </summary>
        [HarmonyPatch(typeof(ItemProperties), nameof(ItemProperties.ActiveBonusValue), MethodType.Setter)]
        [HarmonyPrefix]
        private static void BonusValuePrefix(ItemProperties __instance, out float __state) => __state = __instance.ActiveBonusValue;

        [HarmonyPatch(typeof(ItemProperties), nameof(ItemProperties.ActiveBonusValue), MethodType.Setter)]
        [HarmonyPostfix]
        private static void BonusValuePostfix(ItemProperties __instance, float __state)
        {
            if (!ArrowUtil.IsCoating(__instance.ActiveBonus) || Mathf.Approximately(__state, __instance.ActiveBonusValue)) return;
            ActiveBonusChangedEvent(__instance)?.Invoke(__instance.ActiveBonus);
        }

        private static void Apply(List<GameObject> gos, List<Object> objects, bool flag)
        {
            if (gos != null)
                foreach (var go in gos)
                    if (go && go.activeSelf != flag) go.SetActive(flag);
            if (objects != null)
                foreach (var o in objects)
                {
                    if (o is Renderer r) r.enabled = flag;
                    else if (o is Behaviour bh) bh.enabled = flag;
                }
        }

        /// <summary>Color of the vanilla cloth/poison arrow material for this view (falls back to the vanilla values).</summary>
        internal static Color TintFor(InventoryItemView view, ArrowBonus coating)
        {
            var mats = view && view._bonusListener ? view._bonusListener._bonuses : null;
            if (mats != null)
                foreach (var m in mats)
                    if (m != null && m._bonusToActivate == coating && m._material && m._material.HasProperty(ColorId))
                        return m._material.color;
            return coating == ArrowBonus.PoisonnedAmmo ? FallbackPoisonTint : FallbackFireTint;
        }

        /// <summary>Tint (or with null, un-tint) every renderer under the targets via a property block.</summary>
        internal static void SetTint(List<GameObject> gos, List<Object> objects, Color? tint)
        {
            if (gos != null)
                foreach (var go in gos)
                    if (go)
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) SetTint(r, tint);
            if (objects != null)
                foreach (var o in objects)
                    if (o is Renderer r) SetTint(r, tint);
        }

        internal static void SetTint(Renderer r, Color? tint)
        {
            if (!r) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            _block.Clear();
            if (tint.HasValue) _block.SetColor(ColorId, tint.Value);
            r.SetPropertyBlock(_block);
        }
    }
}
