using System;
using System.Reflection;
using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// VR handedness. The game holds every weapon in its "right hand": for the other hand it mirrors the VR hands
    /// (VRPlayerControl.ApplyMirroredHands: the game's right hand object then follows the left controller).
    /// - The bow follows only the bow option ("right handed bow"). Vanilla also mirrored it with left-handed weapons
    ///   (the bow is a weapon in the right hand slot), so with both options on the left side the bow stayed in the left
    ///   hand and only the draw trigger changed.
    /// - The body's arms follow the controllers on their own side. Vanilla left the body's right arm reaching for the
    ///   right hand object, wherever mirroring put it: across the chest to the other controller (crossed, twisted arms).
    ///   While mirrored, each arm reaches for the controller on its side, held the way that arm normally holds it.
    /// </summary>
    internal static class HandednessPatch
    {
        private static Func<bool> _holdingBow;
        private static Action<VRPlayerControl, bool> _mirror;
        private static FieldInfo _rightHandedBow, _rightHandedWeapon, _leftIk, _rightIk, _leftHand, _rightHand;

        /// <summary>VR build only.</summary>
        public static bool Apply(Harmony harmony)
        {
            var update = AccessTools.Method(typeof(VRPlayerControl), "UpdateWeaponHandedness");
            var ikSolve = AccessTools.Method(typeof(VRPlayerControl), "IkSolve");
            var holdingBow = AccessTools.Method(typeof(VRPlayerControl), "IsHoldingBowTypeWeapon");
            var mirror = AccessTools.Method(typeof(VRPlayerControl), "ApplyMirroredHands");
            _rightHandedBow = AccessTools.Field(typeof(PlayerPreferences), "VRUseRightHandedBow");
            _rightHandedWeapon = AccessTools.Field(typeof(PlayerPreferences), "VRUseRightHandedWeapon");
            _leftIk = AccessTools.Field(typeof(VRPlayerControl), "_leftArmIkSolve");
            _rightIk = AccessTools.Field(typeof(VRPlayerControl), "_rightArmIkSolve");
            _leftHand = AccessTools.Field(typeof(VRPlayerControl), "LeftHand");
            _rightHand = AccessTools.Field(typeof(VRPlayerControl), "RightHand");
            if (update == null || ikSolve == null || holdingBow == null || mirror == null || _rightHandedBow == null
                || _rightHandedWeapon == null || _leftIk == null || _rightIk == null || _leftHand == null || _rightHand == null)
                return false;
            _holdingBow = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), holdingBow);
            _mirror = (vr, on) => mirror.Invoke(vr, new object[] { on });

            harmony.Patch(update, prefix: new HarmonyMethod(typeof(HandednessPatch), nameof(UpdateHandednessPrefix)));
            harmony.Patch(ikSolve, prefix: new HarmonyMethod(typeof(HandednessPatch), nameof(IkSolvePrefix)));
            return true;
        }

        /// <summary>
        /// Replaces VRPlayerControl.UpdateWeaponHandedness: as vanilla, except a bow, crossbow or slingshot follows
        /// only the bow option.
        /// </summary>
        private static bool UpdateHandednessPrefix(VRPlayerControl __instance)
        {
            if (!ForestVR.Enabled) return true;
            bool mirrored;
            if (LocalPlayer.Inventory.CurrentView != PlayerInventory.PlayerViews.World) mirrored = false;
            else if (_holdingBow()) mirrored = (bool)_rightHandedBow.GetValue(null);
            else
            {
                var animator = LocalPlayer.Animator;
                mirrored = !(bool)_rightHandedWeapon.GetValue(null) && !animator.GetBool("flintLockHeld")
                           && !animator.GetBool("chainSawHeld") && !animator.GetBool("flaregunHeld")
                           && !LocalPlayer.Inventory.IsRightHandEmpty();
            }
            if (mirrored != __instance.RightHandedActive) _mirror(__instance, mirrored);
            return false;
        }

        // The arms' own targets, and each one's pose relative to its hand object (measured while not mirrored).
        private static Transform _leftTarget, _rightTarget, _leftProxy, _rightProxy;
        private static Vector3 _leftPos, _rightPos;
        private static Quaternion _leftRot, _rightRot;
        private static bool _measured;

        /// <summary>Before the arms are solved: while mirrored, each arm reaches for the controller on its side.</summary>
        private static void IkSolvePrefix(VRPlayerControl __instance)
        {
            if (!ForestVR.Enabled) return;
            var left = _leftIk.GetValue(__instance) as InverseKinematics;
            var right = _rightIk.GetValue(__instance) as InverseKinematics;
            var leftHand = (_leftHand.GetValue(__instance) as Component)?.transform;
            var rightHand = (_rightHand.GetValue(__instance) as Component)?.transform;
            if (!left || !right || !leftHand || !rightHand) return;

            // The arms' own targets (a new player has new ones).
            if (!_leftTarget || left.target != _leftProxy && left.target != _leftTarget)
            {
                _leftTarget = left.target;
                _measured = false;
            }
            if (!_rightTarget || right.target != _rightProxy && right.target != _rightTarget)
            {
                _rightTarget = right.target;
                _measured = false;
            }
            if (!_leftTarget || !_rightTarget) return;

            if (!__instance.RightHandedActive)
            {
                left.target = _leftTarget;
                right.target = _rightTarget;
                Measure(leftHand, _leftTarget, out _leftPos, out _leftRot);
                Measure(rightHand, _rightTarget, out _rightPos, out _rightRot);
                _measured = true;
                return;
            }

            // Mirrored: the right hand object follows the left controller and the left one the right controller.
            if (!_leftProxy) _leftProxy = NewProxy("VrTweaks left arm target", __instance.transform);
            if (!_rightProxy) _rightProxy = NewProxy("VrTweaks right arm target", __instance.transform);
            if (_measured)
            {
                Place(_leftProxy, rightHand, _leftPos, _leftRot);
                Place(_rightProxy, leftHand, _rightPos, _rightRot);
            }
            else
            {
                // Never seen unmirrored yet: at least each arm on its own side.
                _leftProxy.SetPositionAndRotation(_rightTarget.position, _rightTarget.rotation);
                _rightProxy.SetPositionAndRotation(_leftTarget.position, _leftTarget.rotation);
            }
            left.target = _leftProxy;
            right.target = _rightProxy;
        }

        private static void Measure(Transform hand, Transform target, out Vector3 position, out Quaternion rotation)
        {
            var inverse = Quaternion.Inverse(hand.rotation);
            position = inverse * (target.position - hand.position);
            rotation = inverse * target.rotation;
        }

        private static void Place(Transform proxy, Transform hand, Vector3 position, Quaternion rotation) =>
            proxy.SetPositionAndRotation(hand.position + hand.rotation * position, hand.rotation * rotation);

        private static Transform NewProxy(string name, Transform parent)
        {
            var proxy = new GameObject(name).transform;
            proxy.SetParent(parent, false);
            return proxy;
        }
    }
}
