using System;
using System.Reflection;
using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Items.Special;
using TheForest.Items.World;
using TheForest.Utils;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// VR: the bow only reads the Lighter button outside VR (`... && !ForestVR.Enabled`). The VR-only path
    /// (VRLightFire trigger on the animated bow's arrow shaft) requires drawBowBool == false, which the VR Longbow
    /// sets to true whenever an arrow is nocked, and that trigger is not where the VR arrow is held.
    /// Here: in VR, holding Lighter for 0.5s lights the current cloth arrow (nocked or not), and a short press takes out
    /// the lighter as before (with a cloth arrow on the bow it comes out on release, not on press). The flame goes where the
    /// game puts it: on the bow's animated arrow, which in VR follows the arrow in your hand (BowController.LateUpdate),
    /// and is moved onto the projectile when it is fired (OnAmmoFired).
    /// </summary>
    internal static class VrArrowLighting
    {
        private const float HoldSeconds = 0.5f;

        private static readonly AccessTools.FieldRef<BowController, bool> LightingArrow =
            AccessTools.FieldRefAccess<BowController, bool>("_lightingArrow");

        private static readonly AccessTools.FieldRef<BowController, ArrowBonus> ActiveAmmoBonus =
            AccessTools.FieldRefAccess<BowController, ArrowBonus>("_activeAmmoBonus");

        private static readonly MethodInfo LightArrowMethod = AccessTools.Method(typeof(BowController), "LightArrow");

        private static readonly MethodInfo LighterOnActivating = AccessTools.Method(typeof(LighterControler), "OnActivating");

        private static Func<bool> _lighterPressed;
        private static float _holdStart = -1f;
        private static LighterControler _pendingLighter; // lighter held back on button-down, taken out on a short press
        private static bool _allowLighter;

        public static void Apply(Harmony harmony)
        {
            _lighterPressed = InputCompat.Button("Lighter");
            harmony.Patch(AccessTools.Method(typeof(BowController), "Update"),
                postfix: new HarmonyMethod(typeof(VrArrowLighting), nameof(BowUpdatePostfix)));
            // The same button takes out the lighter as soon as it is pressed. With a cloth arrow on the bow, wait for
            // the release instead: a short press takes out the lighter, holding only lights the arrow.
            harmony.Patch(LighterOnActivating,
                prefix: new HarmonyMethod(typeof(VrArrowLighting), nameof(LighterActivatingPrefix)));
        }

        private static bool LighterActivatingPrefix(LighterControler __instance)
        {
            if (_allowLighter || !ForestVR.Enabled || !(LocalPlayer.ActiveBurnableItem is BowController bow) || !bow || !bow.isActiveAndEnabled)
                return true;
            var arrow = BowUtil.CurrentArrow(bow);
            if (!arrow || arrow.ActiveBonus != ArrowBonus.BurningAmmo) return true;
            _pendingLighter = __instance;
            Dbg.Log("light: Lighter pressed with a cloth arrow; lighter only comes out on a short press");
            return false;
        }

        private static void TakeOutPendingLighter()
        {
            var lighter = _pendingLighter;
            _pendingLighter = null;
            if (!lighter) return;
            _allowLighter = true;
            try
            {
                LighterOnActivating.Invoke(lighter, null);
                Dbg.Log("light: short press, taking out the lighter");
            }
            finally
            {
                _allowLighter = false;
            }
        }

        private static void BowUpdatePostfix(BowController __instance)
        {
            if (!ForestVR.Enabled) return;

            if (!_lighterPressed())
            {
                if (_holdStart >= 0f)
                {
                    bool shortPress = _holdStart != float.MaxValue && Time.realtimeSinceStartup - _holdStart < HoldSeconds;
                    if (shortPress) TakeOutPendingLighter();
                    Dbg.Log("light: Lighter released");
                }
                _pendingLighter = null;
                _holdStart = -1f;
                return;
            }
            if (_holdStart < 0f)
            {
                _holdStart = Time.realtimeSinceStartup;
                Dbg.Forget("lightBlocked");
                Dbg.Log("light: Lighter pressed");
            }
            if (Time.realtimeSinceStartup - _holdStart < HoldSeconds) return;

            // Keep trying while the button is held (something may be busy for a moment), but light only once per press.
            var blocked = WhyNotLight(__instance);
            if (blocked != null)
            {
                Dbg.Changed("lightBlocked", "light: not lighting: " + blocked);
                return;
            }
            _holdStart = float.MaxValue;
            LightArrowMethod.Invoke(__instance, null);
            var flame = __instance._activeFireArrowGO;
            Dbg.Log(flame
                ? $"light: lit, flame on '{(flame.transform.parent ? flame.transform.parent.name : "-")}'"
                : "light: lit, but no flame object was created");
        }

        /// <summary>Null when the arrow can be lit, else the reason it can't.</summary>
        private static string WhyNotLight(BowController bow)
        {
            if (_holdStart == float.MaxValue) return "already lit with this press";
            if (bow._player == null || bow._player.CurrentView != PlayerInventory.PlayerViews.World) return "not in world view";
            if (LightingArrow(bow)) return "bow is already lighting the arrow";
            if (LighterControler.IsBusy) return "lighter is busy";
            if (bow._activeFireArrowGO) return "arrow is already burning";
            // Burning with no flame object (e.g. the flame was destroyed): allow relighting.
            var active = ActiveAmmoBonus(bow);
            if (active != ArrowUtil.NoBonus && active != ArrowBonus.BurningAmmo) return $"bow's active arrow bonus is {active}";
            if (!bow._player.Owns(bow._ammoItemId, false)) return "no arrows";
            var arrow = BowUtil.CurrentArrow(bow);
            if (!arrow) return "no current arrow view";
            if (arrow.ActiveBonus != ArrowBonus.BurningAmmo) return $"current arrow is not a cloth arrow ({Dbg.Props(arrow.Properties)})";
            return null;
        }
    }
}
