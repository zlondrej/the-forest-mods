using System;
using System.Collections.Generic;
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
    /// - The quick-select backpack always opens in the left hand: the hands aren't mirrored while it's open (the held
    ///   weapon moves to the right hand meanwhile), as they aren't on the crafting mat.
    /// - The body's arms follow the controllers on their own side. Vanilla left the body's right arm reaching for the
    ///   right hand object, wherever mirroring put it: across the chest to the other controller (crossed, twisted arms).
    ///   While mirrored, each arm reaches for the controller on its side, held the way that arm normally holds it, and
    ///   the arms' own mirroring (forearms, hands mesh, watch) is undone: a left hand on the left, with the right
    ///   hand's finger pose (its grip) moved across to it.
    /// - What the hands hold still changes hands while mirrored (a weapon held left-handed goes to the left controller).
    /// - Elbows always fold down and away from the body, yours and other VR players' (as your game shows them).
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
            FindMirrorVisuals();

            harmony.Patch(update, prefix: new HarmonyMethod(typeof(HandednessPatch), nameof(UpdateHandednessPrefix)));
            harmony.Patch(ikSolve, prefix: new HarmonyMethod(typeof(HandednessPatch), nameof(IkSolvePrefix)));
            harmony.Patch(AccessTools.Method(typeof(InverseKinematics), nameof(InverseKinematics.Solve)),
                prefix: new HarmonyMethod(typeof(HandednessPatch), nameof(FoldElbowPrefix)));
            return true;
        }

        // PlayerInventory.ShowingVrQuickSelect (VR build only): the quick-select backpack is open.
        private static readonly PropertyInfo _quickSelect = AccessTools.Property(typeof(PlayerInventory), "ShowingVrQuickSelect");

        /// <summary>
        /// Replaces VRPlayerControl.UpdateWeaponHandedness: as vanilla, except a bow, crossbow or slingshot follows
        /// only the bow option, and the hands aren't mirrored while the quick-select backpack is open (it hangs from
        /// the game's left hand object: mirrored, it opened in the right hand).
        /// </summary>
        private static bool UpdateHandednessPrefix(VRPlayerControl __instance)
        {
            if (!ForestVR.Enabled) return true;
            bool mirrored;
            if (LocalPlayer.Inventory.CurrentView != PlayerInventory.PlayerViews.World) mirrored = false;
            else if (_quickSelect != null && (bool)_quickSelect.GetValue(LocalPlayer.Inventory, null)) mirrored = false;
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
                LeftHeld.Home(left);
                RightHeld.Home(right);
                Fingers.Restore();
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
            UnmirrorArms(__instance);
            // What the hands hold goes where vanilla's mirrored arms took it: the right hand's items (a weapon held
            // left-handed) to the left controller, the left hand's (the lighter) to the right one.
            RightHeld.Mirror(right, _rightTarget, __instance.transform);
            LeftHeld.Mirror(left, _leftTarget, __instance.transform);
        }

        private static readonly HeldItems LeftHeld = new HeldItems(), RightHeld = new HeldItems();

        /// <summary>
        /// A body hand's held items ("rightHandHeld"/"leftHandHeld", under the hand bone: weapons, the lighter, food).
        /// Vanilla mirrored, the arm carried them to the other controller, mirrored (the forearm scaled -1). With each
        /// arm at its own controller they'd stay put, so while mirrored they hang from a stand-in for vanilla's
        /// mirrored hand instead: at the arm's own target (the other controller), turned as the hand would be
        /// (target rotation, then the arm's hand offset), scaled -1 on x, holding them as the hand bone does.
        /// </summary>
        private sealed class HeldItems
        {
            private Transform _held, _home, _hand, _standIn;
            private Vector3 _homePosition, _homeScale;
            private Quaternion _homeRotation;

            /// <summary>Unmirrored: back on the hand.</summary>
            public void Home(InverseKinematics arm)
            {
                if (!Find(arm) || _held.parent == _home) return;
                _held.SetParent(_home, false);
                _held.localPosition = _homePosition;
                _held.localRotation = _homeRotation;
                _held.localScale = _homeScale;
            }

            /// <summary>
            /// Mirrored: on the stand-in for vanilla's mirrored hand, held as the hand holds them right now (the weapon
            /// bone they hang from moves with the animations, e.g. in and out of the inventory).
            /// </summary>
            public void Mirror(InverseKinematics arm, Transform vanillaTarget, Transform parent)
            {
                if (!vanillaTarget || !Find(arm)) return;
                if (!_standIn)
                {
                    _standIn = NewProxy("VrTweaks mirrored hand", parent);
                    _standIn.localScale = new Vector3(-1f, 1f, 1f);
                }
                _standIn.SetPositionAndRotation(vanillaTarget.position, vanillaTarget.rotation * Quaternion.Euler(arm.hand_OffsetRotation));
                if (_held.parent != _standIn) _held.SetParent(_standIn, false);
                var local = _hand.worldToLocalMatrix * _home.localToWorldMatrix * Matrix4x4.TRS(_homePosition, _homeRotation, _homeScale);
                _held.localPosition = local.GetColumn(3);
                _held.localRotation = Quaternion.LookRotation(local.GetColumn(2), local.GetColumn(1));
                _held.localScale = new Vector3(local.GetColumn(0).magnitude, local.GetColumn(1).magnitude, local.GetColumn(2).magnitude);
            }

            private bool Find(InverseKinematics arm)
            {
                if (_held && _hand == arm.hand) return true;
                _hand = arm.hand;
                _held = null;
                if (!_hand) return false;
                foreach (var t in _hand.GetComponentsInChildren<Transform>(true))
                    if (t.name.EndsWith("HandHeld", StringComparison.OrdinalIgnoreCase)) { _held = t; break; }
                if (!_held) return false;
                _home = _held.parent;
                _homePosition = _held.localPosition;
                _homeRotation = _held.localRotation;
                _homeScale = _held.localScale;
                return true;
            }
        }

        // Where an elbow points, from the middle of its arm (shoulder to hand), in metres: down, out to its side, back.
        private const float ElbowDown = 0.5f, ElbowOut = 0.35f, ElbowBack = 0.15f;
        private static readonly Dictionary<InverseKinematics, Transform> ElbowHints = new Dictionary<InverseKinematics, Transform>();
        private static readonly Dictionary<InverseKinematics, Transform> Bodies = new Dictionary<InverseKinematics, Transform>();

        /// <summary>
        /// Before any VR arm is posed (yours, and other VR players' as your game poses them from their headset and
        /// controllers): the arms bend their elbows towards a hint point each, and the game's sit on the body (or, for
        /// other players, follow a few fixed directions), letting elbows point up or in when the hands are raised or
        /// crossed. Here each elbow's hint is below the middle of its arm, out to its own side and a little back,
        /// wherever the hand is: elbows fold down and away from the body.
        /// </summary>
        private static void FoldElbowPrefix(InverseKinematics __instance)
        {
            if (!ForestVR.Enabled) return;
            var arm = __instance;
            if (!arm.upperArm || !arm.target) return;
            var body = Body(arm);
            var outward = body.right;
            outward.y = 0f;
            var back = -body.forward;
            back.y = 0f;
            if (outward.sqrMagnitude < 1e-4f || back.sqrMagnitude < 1e-4f) return;
            if (arm.upperArm.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0) outward = -outward;
            if (!ElbowHints.TryGetValue(arm, out var hint) || !hint)
            {
                if (ElbowHints.Count > 16) Forget();
                ElbowHints[arm] = hint = NewProxy("VrTweaks elbow hint", arm.transform);
            }
            var middle = (arm.upperArm.position + arm.target.position) / 2f;
            hint.position = middle + Vector3.down * ElbowDown + outward.normalized * ElbowOut + back.normalized * ElbowBack;
            arm.elbow = hint;
        }

        /// <summary>The player an arm belongs to (its facing): another player's network body, else you.</summary>
        private static Transform Body(InverseKinematics arm)
        {
            if (Bodies.TryGetValue(arm, out var body) && body) return body;
            var sync = arm.GetComponentInParent<VRCoopPlayerSync>();
            body = sync && !(sync.entity && sync.entity.isOwner) ? sync.transform : LocalPlayer.Transform ? LocalPlayer.Transform : arm.transform.root;
            Bodies[arm] = body;
            return body;
        }

        /// <summary>Players come and go: drop the arms that are gone.</summary>
        private static void Forget()
        {
            foreach (var arm in new List<InverseKinematics>(ElbowHints.Keys))
                if (!arm) ElbowHints.Remove(arm);
            foreach (var arm in new List<InverseKinematics>(Bodies.Keys))
                if (!arm) Bodies.Remove(arm);
        }

        /// <summary>
        /// The game also mirrors the arms themselves for the right arm to pass for a left one at the left controller:
        /// the forearm bones and the VR hands mesh scaled -1 on x, the watch turned round. With each arm at its own
        /// controller that made a mirrored left hand on the left (looking like a right one), inside-out sleeves and
        /// faces: set back as unmirrored. The finger poses are swapped across instead (see Fingers).
        /// </summary>
        private static void UnmirrorArms(VRPlayerControl vr)
        {
            if (_mirrorVisuals == null) return;
            SetScale(_leftForearm.GetValue(vr) as Transform, Vector3.one);
            SetScale(_rightForearm.GetValue(vr) as Transform, Vector3.one);
            var adapter = LocalPlayer.vrAdapter;
            if (adapter)
            {
                if (adapter.VRPlayerHands) SetScale(adapter.VRPlayerHands.transform, Vector3.one);
                var watch = adapter.VRWatchParentGo ? adapter.VRWatchParentGo.transform : null;
                if (watch)
                {
                    watch.localRotation = (Quaternion)_watchRotation.GetValue(vr);
                    SetScale(watch, (Vector3)_watchScale.GetValue(vr));
                }
            }
            Fingers.Swap(vr);
        }

        /// <summary>
        /// The fingers' poses, swapped between the hands while mirrored. Vanilla showed the right hand at the left
        /// controller: its grip on the held weapon, and the finger curls of the left controller (the game feeds each
        /// controller's curls to the other hand while mirrored). With each arm at its own controller, those poses
        /// belong on the left hand, and the left hand's on the right. The rig is mirrored left to right with equal
        /// local rotations, so a pose moves across as it is. Bones the animation doesn't set every frame keep their
        /// own pose (not swapped back and forth).
        /// </summary>
        private static class Fingers
        {
            private sealed class Pair
            {
                public Transform Left, Right;
                public Quaternion LeftOwn, RightOwn, LeftSet, RightSet;
                public bool Set;
            }

            private static readonly List<Pair> Pairs = new List<Pair>();
            private static Transform _leftHand;
            private static bool _swapped;

            public static void Swap(VRPlayerControl vr)
            {
                if (!Find(vr)) return;
                foreach (var p in Pairs)
                {
                    if (!p.Left || !p.Right) continue;
                    // This frame's own pose: the animation's, unless it left what was set last frame.
                    if (!p.Set || p.Left.localRotation != p.LeftSet) p.LeftOwn = p.Left.localRotation;
                    if (!p.Set || p.Right.localRotation != p.RightSet) p.RightOwn = p.Right.localRotation;
                    p.Left.localRotation = p.LeftSet = p.RightOwn;
                    p.Right.localRotation = p.RightSet = p.LeftOwn;
                    p.Set = true;
                }
                _swapped = true;
            }

            /// <summary>Unmirrored: each hand's own pose back where the animation doesn't set it.</summary>
            public static void Restore()
            {
                if (!_swapped) return;
                _swapped = false;
                foreach (var p in Pairs)
                {
                    if (!p.Set || !p.Left || !p.Right) continue;
                    if (p.Left.localRotation == p.LeftSet) p.Left.localRotation = p.LeftOwn;
                    if (p.Right.localRotation == p.RightSet) p.Right.localRotation = p.RightOwn;
                    p.Set = false;
                }
            }

            /// <summary>The finger bones under each hand (char_LeftHand.../char_RightHand..., not the weapon bones), paired by name.</summary>
            private static bool Find(VRPlayerControl vr)
            {
                var left = _leftIk.GetValue(vr) as InverseKinematics;
                var right = _rightIk.GetValue(vr) as InverseKinematics;
                if (!left || !right || !left.hand || !right.hand) return false;
                if (_leftHand == left.hand && Pairs.Count > 0) return true;
                _leftHand = left.hand;
                Pairs.Clear();
                var rights = new Dictionary<string, Transform>();
                foreach (var t in right.hand.GetComponentsInChildren<Transform>(true))
                    if (IsFinger(t, "char_RightHand")) rights[t.name] = t;
                foreach (var t in left.hand.GetComponentsInChildren<Transform>(true))
                    if (IsFinger(t, "char_LeftHand") && rights.TryGetValue("char_RightHand" + t.name.Substring("char_LeftHand".Length), out var r))
                        Pairs.Add(new Pair { Left = t, Right = r });
                return Pairs.Count > 0;
            }

            private static bool IsFinger(Transform t, string prefix) =>
                t.name.StartsWith(prefix, StringComparison.Ordinal) && t.name.Length > prefix.Length
                && t.name.IndexOf("Weapon", StringComparison.Ordinal) < 0;
        }

        private static void SetScale(Transform t, Vector3 scale)
        {
            if (t && t.localScale != scale) t.localScale = scale;
        }

        // The mirrored visuals' fields (VR build): forearm bones, the watch's unmirrored pose.
        private static FieldInfo _leftForearm, _rightForearm, _watchRotation, _watchScale;
        private static object _mirrorVisuals;

        private static void FindMirrorVisuals()
        {
            var vr = typeof(VRPlayerControl);
            _leftForearm = AccessTools.Field(vr, "LeftForearmTransform");
            _rightForearm = AccessTools.Field(vr, "RightForearmTransform");
            _watchRotation = AccessTools.Field(vr, "watchParentLocalRot");
            _watchScale = AccessTools.Field(vr, "watchParentLocalScale");
            if (_leftForearm != null && _rightForearm != null && _watchRotation != null && _watchScale != null)
                _mirrorVisuals = new object();
            else Plugin.Log.LogWarning("Handedness: the arms' mirroring wasn't found, mirrored arms stay as in vanilla");
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
