using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// VR building placement: twisting the aiming controller (the one whose ray places the building, so the right hand
    /// right-handed, the left one left-handed) around its pointing direction turns the building, like turning a knob:
    /// <see cref="Ratio"/> building degrees per degree of twist (2: a quarter turn of the hand turns it half around).
    /// Twisting right turns it clockwise seen from above. Only while the rotate button is held
    /// (<see cref="OnlyWhileRotateHeld"/>, the stick's turning paused meanwhile), or all the time on top of it.
    /// </summary>
    internal static class BuildingRotationPatch
    {
        public static float Ratio = 2f;
        public static bool OnlyWhileRotateHeld = true;

        private static AccessTools.FieldRef<KeepAboveTerrain, float> _turn;
        private static System.Func<bool> _rotateHeld;
        private static int _lastFrame = -10;

        /// <summary>VR build only (the flat-screen build turns buildings inside Update, without UpdateRotateInput).</summary>
        public static bool Apply(Harmony harmony)
        {
            var update = AccessTools.Method(typeof(KeepAboveTerrain), "UpdateRotateInput");
            var input = typeof(TheForest.Utils.Input);
            var actions = AccessTools.Inner(input, "Actions");
            var rotate = actions == null ? null : AccessTools.Field(actions, "Rotate");
            var getButton = rotate == null ? null : AccessTools.Method(input, "GetButton", new[] { rotate.FieldType });
            if (update == null || getButton == null) return false;
            var action = rotate.GetValue(null);
            _rotateHeld = () => (bool)getButton.Invoke(null, new[] { action });
            _turn = AccessTools.FieldRefAccess<KeepAboveTerrain, float>("turn");
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(BuildingRotationPatch), nameof(Postfix)));
            return true;
        }

        private static void Postfix(KeepAboveTerrain __instance)
        {
            if (!ForestVR.Enabled || Ratio == 0f) return;
            var events = LocalPlayer.InventoryMouseEventsVR;
            var hand = events ? events.RayOverride : null;
            if (!hand) return;
            bool held = _rotateHeld();
            bool active = !OnlyWhileRotateHeld || held;
            if (active && OnlyWhileRotateHeld) _turn(__instance) = 0f; // the hand turns it now, not the stick
            bool hasRoll = Roll(hand, out float roll);
            // Start measuring afresh after a pause (rotate button released, placement stopped, a skipped frame, the
            // hand pointing straight up or down).
            if (active && hasRoll && _lastFrame == Time.frameCount - 1)
            {
                // A positive roll is counter-clockwise seen from behind the hand: twisting right (clockwise) turns
                // the building clockwise seen from above (positive yaw).
                _turn(__instance) -= Ratio * Mathf.DeltaAngle(_lastRoll, roll);
            }
            _lastRoll = roll;
            _lastFrame = active && hasRoll ? Time.frameCount : -10;
        }

        private static float _lastRoll;

        /// <summary>
        /// The hand's roll: how far its up direction is turned around its pointing direction from the world's up (as
        /// an aircraft's roll). Where it points (aiming) doesn't change it, only twisting the wrist does. Undefined
        /// when pointing almost straight up or down.
        /// </summary>
        private static bool Roll(Transform hand, out float roll)
        {
            roll = 0f;
            var forward = hand.forward;
            var reference = Vector3.up - forward * Vector3.Dot(Vector3.up, forward); // world up, across the pointing
            if (reference.sqrMagnitude < 0.04f) return false; // pointing within ~12° of straight up or down
            var up = hand.up;
            roll = Mathf.Atan2(Vector3.Dot(Vector3.Cross(reference, up), forward), Vector3.Dot(reference, up)) * Mathf.Rad2Deg;
            return true;
        }
    }
}
