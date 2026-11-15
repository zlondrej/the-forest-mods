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
                if (_holdStart >= 0f) Dbg.Log("light: Lighter released");
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
            _litBy = __instance;
            var attached = AttachFlameToHeldArrow(__instance);
            Dbg.Log($"light: lit, flame object {(__instance._activeFireArrowGO ? "created" : "MISSING")}, " +
                    $"{(attached ? "attached to the arrow in the VR hand" : "NOT attached to a VR arrow (no held arrow found)")}");
        }

        /// <summary>Null when the arrow can be lit, else the reason it can't.</summary>
        private static string WhyNotLight(BowController bow)
        {
            if (_holdStart == float.MaxValue) return "already lit with this press";
            if (bow._player == null || bow._player.CurrentView != PlayerInventory.PlayerViews.World) return "not in world view";
            if (LightingArrow(bow)) return "bow is already lighting the arrow";
            if (LighterControler.IsBusy) return "lighter is busy";
            if (bow._activeFireArrowGO) return "arrow is already burning";
            // Burning with no flame object happens when the VR hand destroyed the flame; allow relighting then.
            var active = ActiveAmmoBonus(bow);
            if (active != ArrowUtil.NoBonus && active != ArrowBonus.BurningAmmo) return $"bow's active arrow bonus is {active}";
            if (!bow._player.Owns(bow._ammoItemId, false)) return "no arrows";
            var arrow = BowUtil.CurrentArrow(bow);
            if (!arrow) return "no current arrow view";
            if (arrow.ActiveBonus != ArrowBonus.BurningAmmo) return $"current arrow is not a cloth arrow ({Dbg.Props(arrow.Properties)})";
            return null;
        }

        private static bool AttachFlameToHeldArrow(BowController bow)
        {
            var flame = bow._activeFireArrowGO;
            var heldArrow = HeldVrArrow(bow);
            if (!flame || !heldArrow) return false;

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
            return true;
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
