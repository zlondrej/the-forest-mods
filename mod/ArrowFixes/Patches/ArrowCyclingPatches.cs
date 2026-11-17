using System;
using System.Collections.Generic;
using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using TheForest.Utils;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>An arrow type as the player sees it: its upgrade plus, for cloth/poison, the bone/modern base.</summary>
    internal struct ArrowKey : IEquatable<ArrowKey>
    {
        public readonly ArrowBonus Bonus;
        public readonly ArrowBonus Base;

        public ArrowKey(ArrowBonus bonus, ArrowBonus @base)
        {
            Bonus = bonus;
            Base = ArrowUtil.IsCoating(bonus) && ArrowUtil.IsBase(@base) ? @base : ArrowUtil.NoBonus;
        }

        public static ArrowKey Of(ItemProperties p) =>
            p == null ? new ArrowKey(ArrowUtil.NoBonus, ArrowUtil.NoBonus) : new ArrowKey(p.ActiveBonus, ArrowUtil.GetBase(p));

        public bool Equals(ArrowKey other) => Bonus == other.Bonus && Base == other.Base;
        public override bool Equals(object obj) => obj is ArrowKey k && Equals(k);
        public override int GetHashCode() => ((int)Bonus * 397) ^ (int)Base;
        public static bool operator ==(ArrowKey a, ArrowKey b) => a.Equals(b);
        public static bool operator !=(ArrowKey a, ArrowKey b) => !a.Equals(b);
        public override string ToString() => Base == ArrowUtil.NoBonus ? Bonus.ToString() : $"{Bonus}+{Base}";

        /// <summary>Grouped by arrow: wooden, bone, modern; each plain, then cloth (flaming), then poison.</summary>
        public static readonly ArrowKey[] CycleOrder =
        {
            new ArrowKey(ArrowUtil.NoBonus, ArrowUtil.NoBonus),
            new ArrowKey(ArrowBonus.BurningAmmo, ArrowUtil.NoBonus),
            new ArrowKey(ArrowBonus.PoisonnedAmmo, ArrowUtil.NoBonus),
            new ArrowKey(ArrowBonus.BoneAmmo, ArrowUtil.NoBonus),
            new ArrowKey(ArrowBonus.BurningAmmo, ArrowBonus.BoneAmmo),
            new ArrowKey(ArrowBonus.PoisonnedAmmo, ArrowBonus.BoneAmmo),
            new ArrowKey(ArrowBonus.ModernAmmo, ArrowUtil.NoBonus),
            new ArrowKey(ArrowBonus.BurningAmmo, ArrowBonus.ModernAmmo),
            new ArrowKey(ArrowBonus.PoisonnedAmmo, ArrowBonus.ModernAmmo),
        };
    }

    /// <summary>
    /// Vanilla cycles arrow types (look down + Rotate) by ActiveBonus only, so e.g. flaming bone and flaming wooden
    /// arrows were one group and you could not pick which fires. The bow's selection is now a full ArrowKey: the bow
    /// view's ActiveBonus as before, plus the selected base in its ActiveBonusValue (the crafting "set ammo on bow"
    /// upgrade already copies an arrow's properties, value included, onto the bow).
    /// </summary>
    [HarmonyPatch] // class-level attribute required, or the class processor skips the method patches
    internal static class ArrowCyclingPatches
    {
        private static readonly Action<BowController, ArrowBonus> SetActiveBowBonus =
            AccessTools.MethodDelegate<Action<BowController, ArrowBonus>>(AccessTools.Method(typeof(BowController), "SetActiveBowBonus"));

        private static readonly Action<BowController> UpdateArrowRenderer =
            AccessTools.MethodDelegate<Action<BowController>>(AccessTools.Method(typeof(BowController), "UpdateArrowRenderer"));

        private static readonly AccessTools.FieldRef<BowController, ArrowBonus> ActiveAmmoBonus =
            AccessTools.FieldRefAccess<BowController, ArrowBonus>("_activeAmmoBonus");

        private static Func<bool> _rotatePressed;
        private static bool _iconShownByUs;
        private static readonly Dictionary<UITexture, Texture> PromptOriginals = new Dictionary<UITexture, Texture>();

        /// <summary>Called by Harmony before patching (HarmonyPrepare).</summary>
        private static bool Prepare()
        {
            _rotatePressed = InputCompat.ButtonDown("Rotate");
            return true;
        }

        /// <summary>Disable vanilla's coating-only cycle: "nothing else available" hides its prompt and rotation.</summary>
        [HarmonyPatch(typeof(BowController), "NextAvailableArrowBonus")]
        [HarmonyPrefix]
        private static bool NextAvailableArrowBonusPrefix(ArrowBonus current, ref ArrowBonus __result)
        {
            __result = current;
            return false;
        }

        [HarmonyPatch(typeof(BowController), "Update")]
        [HarmonyPostfix]
        private static void BowUpdatePostfix(BowController __instance)
        {
            var player = __instance._player;
            if (player == null || player.CurrentView != PlayerInventory.PlayerViews.World) return;
            HideArrowWhenNoneLeft(player, __instance._ammoItemId);
            var bowView = BowView(__instance);
            if (!bowView) return;

            KeepCurrentArrowOnSelection(__instance, bowView);
            HandleRotate(__instance, bowView);
        }

        /// <summary>
        /// The arrow on the bow is only hidden when firing (or unequipping); taking the last arrow away otherwise (into
        /// an arrow basket, onto the crafting mat) left it on the bow until the next shot. Hide it as soon as none are
        /// left, like after firing the last one (the fired arrow leaves the inventory at release, so a drawn last arrow
        /// stays visible until then).
        /// </summary>
        private static void HideArrowWhenNoneLeft(PlayerInventory player, int ammoItemId)
        {
            if (player.Owns(ammoItemId, false)) return;
            if (!player.InventoryItemViewsCache.TryGetValue(ammoItemId, out var views) || views == null || views.Count == 0) return;
            var held = views[0]._held;
            if (!held || !held.activeSelf) return;
            player.ToggleAmmo(ammoItemId, false);
            Dbg.Log("bow: no arrows left, hid the arrow on the bow");
        }

        /// <summary>Vanilla only re-sorts when the bonus differs; also re-sort when only the base differs.</summary>
        private static void KeepCurrentArrowOnSelection(BowController bow, InventoryItemView bowView)
        {
            if (!LocalPlayer.Inventory.Owns(bow._ammoItemId, false)) return;
            var current = BowUtil.CurrentArrow(bow);
            if (!current || current.ActiveBonus != bowView.ActiveBonus) return; // vanilla handles bonus changes
            var selected = ArrowKey.Of(bowView.Properties);
            if (ArrowKey.Of(current.Properties) == selected) return;

            SortByKey(bow._ammoItemId, selected);
            current = BowUtil.CurrentArrow(bow);
            var actual = ArrowKey.Of(current ? current.Properties : null);
            if (actual != selected) SetSelectedBase(bowView, actual.Base); // none of the selected base left
            Dbg.Log($"cycle: next arrow did not match selection {selected}; re-sorted, next arrow is now {actual}");
            UpdateArrowRenderer(bow);
        }

        private static void HandleRotate(BowController bow, InventoryItemView bowView)
        {
            bool grabbing = LocalPlayer.Create.Grabber.Target;
            bool drawing = LocalPlayer.Animator.GetBool("drawBowBool");
            bool atBasket = ArrowBasket.Focused; // the basket uses Rotate to pick its arrow type
            bool looking = LookingToCycle(bow, bowView, out string lookInfo);
            bool canCycle = !grabbing && looking && !drawing && !atBasket;
            var selected = ArrowKey.Of(bowView.Properties);
            var next = canCycle ? NextAvailable(bow._ammoItemId, selected) : selected;
            bool pressed = _rotatePressed();
            if (pressed && Dbg.On)
            {
                Dbg.Log($"cycle: Rotate pressed. {lookInfo}, grabbing={grabbing}, drawing={drawing}; " +
                        $"selected {selected}, next {next}; carrying {Dbg.Arrows(bow._ammoItemId)}");
            }
            if (next == selected)
            {
                SetIcon(false);
                return;
            }
            SetIcon(true, selected);
            if (!pressed) return;

            LocalPlayer.Sfx.PlayWhoosh();
            PutOutLitArrow(bow, next);
            SetActiveBowBonus(bow, next.Bonus);
            SetSelectedBase(bowView, next.Base);
            SortByKey(bow._ammoItemId, next);
            UpdateArrowRenderer(bow);
            SetIcon(false);
            var now = BowUtil.CurrentArrow(bow);
            Dbg.Log($"cycle: switched to {next}; bow {Dbg.Props(bowView.Properties)}, next arrow {(now ? Dbg.Props(now.Properties) : "none")}");
        }

        /// <summary>VR: switch arrow type while looking at the bow (else the vanilla look down).</summary>
        public static bool VrLookAtBow = true;

        /// <summary>Max angle between the view and the bow, in VR, to switch arrow type.</summary>
        public static float VrBowLookAngle = 25f;

        /// <summary>
        /// Flat-screen: look down steeply, as in vanilla. VR: look at the bow in your hand (vanilla's steep look down
        /// is uncomfortable with a headset).
        /// </summary>
        private static bool LookingToCycle(BowController bow, InventoryItemView bowView, out string info)
        {
            var cam = LocalPlayer.MainCamTr;
            if (!ForestVR.Enabled || !VrLookAtBow || VrBowLookAngle <= 0f)
            {
                float lookY = cam.forward.y;
                info = $"look-down={lookY:0.00} (needs < -0.85)";
                return lookY < -0.85f;
            }
            var bowTr = bowView._held ? bowView._held.transform : bow.transform;
            var toBow = bowTr.position - cam.position;
            float angle = toBow.sqrMagnitude > 1e-6f ? Vector3.Angle(cam.forward, toBow) : 0f;
            info = $"angle to bow={angle:0}° (needs < {VrBowLookAngle:0}°)";
            return angle < VrBowLookAngle;
        }

        /// <summary>
        /// Switching arrow type while a cloth arrow is lit: put the flame out, and the lit arrow keeps its cloth so it
        /// can be lit again, like when the bow is put away. Vanilla burned the cloth off when switching to a type that
        /// isn't flaming, and between two flaming types (e.g. bone -> modern) the flame moved onto the other arrow.
        /// </summary>
        private static void PutOutLitArrow(BowController bow, ArrowKey next)
        {
            var flame = bow._activeFireArrowGO;
            if (!flame) return;
            UnityEngine.Object.Destroy(flame.gameObject);
            bow._activeFireArrowGO = null; // also keeps vanilla SetActiveBowBonus from burning the cloth off
            LocalPlayer.Inventory.IsWeaponBurning = false;
            LocalPlayer.ScriptSetup.targetInfo.arrowFire = false;
            if (ActiveAmmoBonus(bow) == ArrowBonus.BurningAmmo) ActiveAmmoBonus(bow) = ArrowUtil.NoBonus; // unlit again
            Dbg.Log("cycle: switched away from a lit arrow; flame put out, cloth kept");
        }

        /// <summary>The "switch arrow type" prompt; while shown, its arrow icon is the currently selected type.</summary>
        private static void SetIcon(bool show, ArrowKey current = default(ArrowKey))
        {
            var icon = Scene.HudGui ? Scene.HudGui.ToggleArrowBonusIcon : null;
            if (!icon) return;
            if (show)
            {
                ArrowIconTextures.Swap(icon.GetComponent<UITexture>(), ArrowIconTextures.For(current), PromptOriginals);
                if (!icon.activeSelf) icon.SetActive(true);
                _iconShownByUs = true;
            }
            else if (_iconShownByUs)
            {
                icon.SetActive(false);
                _iconShownByUs = false;
            }
        }

        private static InventoryItemView BowView(BowController bow)
        {
            var cache = LocalPlayer.Inventory ? LocalPlayer.Inventory.InventoryItemViewsCache : null;
            return cache != null && cache.TryGetValue(bow._bowItemId, out var views) && views != null && views.Count > 0
                ? views[0]
                : null;
        }

        private static void SetSelectedBase(InventoryItemView bowView, ArrowBonus @base)
        {
            var value = ArrowUtil.IsCoating(bowView.ActiveBonus) && ArrowUtil.IsBase(@base) ? (float)(int)@base : 0f;
            if (!Mathf.Approximately(bowView.Properties.ActiveBonusValue, value)) bowView.Properties.ActiveBonusValue = value;
        }

        private static ArrowKey NextAvailable(int ammoItemId, ArrowKey current)
        {
            var available = new HashSet<ArrowKey>();
            foreach (var view in ActiveArrowViews(ammoItemId)) available.Add(ArrowKey.Of(view.Properties));
            if (available.Count == 0) return current;

            var order = ArrowKey.CycleOrder;
            int start = Array.IndexOf(order, current);
            for (int step = 1; step <= order.Length; step++)
            {
                var candidate = order[((start < 0 ? -1 : start) + step) % order.Length];
                if (available.Contains(candidate)) return candidate;
            }
            return current;
        }

        private static IEnumerable<InventoryItemView> ActiveArrowViews(int ammoItemId)
        {
            if (!LocalPlayer.Inventory.InventoryItemViewsCache.TryGetValue(ammoItemId, out var list) || list == null) yield break;
            foreach (var view in list)
                if (view && view.gameObject.activeSelf) yield return view;
        }

        /// <summary>
        /// Same as PlayerInventory.SortInventoryViewsByBonus, but by full key: moves non-matching owned arrows to the
        /// front, so the arrow fired next (the last owned one) matches the selection when possible.
        /// </summary>
        internal static void SortByKey(int ammoItemId, ArrowKey key)
        {
            if (!LocalPlayer.Inventory.InventoryItemViewsCache.TryGetValue(ammoItemId, out var list) || list == null) return;
            int moved = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var view = list[i + moved];
                if (view && view.gameObject.activeSelf && ArrowKey.Of(view.Properties) != key)
                {
                    list.RemoveAt(i + moved);
                    list.Insert(0, view);
                    moved++;
                }
            }
        }
    }
}
