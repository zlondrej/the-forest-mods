using System;
using System.Linq;
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
    /// Here: in VR, holding Lighter for 0.5s lights the current cloth arrow (nocked or not), and the flame is shown on
    /// the arrow in the VR hand.
    /// </summary>
    internal static class VrArrowLighting
    {
        private const float HoldSeconds = 0.5f;

        private static readonly AccessTools.FieldRef<BowController, bool> LightingArrow =
            AccessTools.FieldRefAccess<BowController, bool>("_lightingArrow");

        private static readonly AccessTools.FieldRef<BowController, ArrowBonus> ActiveAmmoBonus =
            AccessTools.FieldRefAccess<BowController, ArrowBonus>("_activeAmmoBonus");

        private static readonly MethodInfo LightArrowMethod = AccessTools.Method(typeof(BowController), "LightArrow");

        // BowController._arrowHandVr has a different type in each build, so it is read by name.
        private static readonly FieldInfo ArrowHandField = AccessTools.Field(typeof(BowController), "_arrowHandVr");

        private static Func<bool> _lighterPressed;
        private static float _holdStart = -1f;
        private static BowController _litBy;

        public static void Apply(Harmony harmony)
        {
            _lighterPressed = InputCompat.Button("Lighter");
            harmony.Patch(AccessTools.Method(typeof(BowController), "Update"),
                postfix: new HarmonyMethod(typeof(VrArrowLighting), nameof(BowUpdatePostfix)));

            // The VR hand destroys its arrow object on release, which would take the flame with it before
            // OnAmmoFired moves the flame onto the projectile. Patch whichever ArrowHand this build has.
            foreach (var typeName in new[] { "TheForest.VR.InteractionSystem.ArrowHand", "Valve.VR.InteractionSystem.ArrowHand" })
            {
                var fire = AccessTools.TypeByName(typeName) is Type t ? AccessTools.Method(t, "FireArrow") : null;
                if (fire == null) continue;
                harmony.Patch(fire, prefix: new HarmonyMethod(typeof(VrArrowLighting), nameof(ArrowHandFireArrowPrefix)));
                Plugin.Log.LogInfo($"VrLighterIgnitesArrow: patched {typeName}.FireArrow");
            }
        }

        private static void BowUpdatePostfix(BowController __instance)
        {
            if (!ForestVR.Enabled) return;

            if (!_lighterPressed())
            {
                _holdStart = -1f;
                return;
            }
            if (_holdStart < 0f) _holdStart = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _holdStart < HoldSeconds) return;
            _holdStart = float.MaxValue; // fire once per press

            if (!CanLight(__instance)) return;
            LightArrowMethod.Invoke(__instance, null);
            _litBy = __instance;
            AttachFlameToHeldArrow(__instance);
        }

        private static bool CanLight(BowController bow)
        {
            if (bow._player == null || bow._player.CurrentView != PlayerInventory.PlayerViews.World) return false;
            if (LightingArrow(bow) || LighterControler.IsBusy || bow._activeFireArrowGO) return false;
            // Burning with no flame object happens when the VR hand destroyed the flame; allow relighting then.
            var active = ActiveAmmoBonus(bow);
            if (active != ArrowUtil.NoBonus && active != ArrowBonus.BurningAmmo) return false;
            if (!bow._player.Owns(bow._ammoItemId, false)) return false;
            var arrow = BowUtil.CurrentArrow(bow);
            return arrow && arrow.ActiveBonus == ArrowBonus.BurningAmmo;
        }

        private static void AttachFlameToHeldArrow(BowController bow)
        {
            var flame = bow._activeFireArrowGO;
            var heldArrow = HeldVrArrow(bow);
            if (!flame || !heldArrow) return;

            var t = heldArrow.transform;
            var tip = t.position + t.forward * 0.3f;
            var renderer = heldArrow.GetComponentInChildren<Renderer>();
            if (renderer)
            {
                // Point of the arrow's bounds furthest along its forward axis.
                var b = renderer.bounds;
                var f = t.forward;
                var halfLength = Mathf.Abs(f.x) * b.extents.x + Mathf.Abs(f.y) * b.extents.y + Mathf.Abs(f.z) * b.extents.z;
                tip = b.center + f * halfLength * 0.8f;
            }
            flame.transform.SetParent(t, true);
            flame.transform.position = tip;
            flame.transform.rotation = t.rotation;
        }

        private static GameObject HeldVrArrow(BowController bow)
        {
            var hand = ArrowHandField?.GetValue(bow) as Component;
            if (!hand) return null;
            return Traverse.Create(hand).Field("currentArrow").GetValue<GameObject>();
        }

        private static void ArrowHandFireArrowPrefix(Component __instance)
        {
            var bow = _litBy;
            if (!bow || !bow._activeFireArrowGO || !bow._ammoAnimated) return;
            var held = Traverse.Create(__instance).Field("currentArrow").GetValue<GameObject>();
            if (held && bow._activeFireArrowGO.transform.IsChildOf(held.transform))
            {
                bow._activeFireArrowGO.transform.SetParent(bow._ammoAnimated.transform, true);
            }
        }
    }
}
