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
                prefix: new HarmonyMethod(typeof(HandednessPatch), nameof(FoldElbowPrefix)),
                postfix: new HarmonyMethod(typeof(HandednessPatch), nameof(StretchAtElbowPostfix)));
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
            if (__instance.useGhostMode)
            {
                // Scenes: the game doesn't pose the arms (the animation does), so what was done to them last stays
                // (a stretched forearm reached for the sky while being dragged): undone, once.
                Unpose(left);
                Unpose(right);
            }
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
        /// wherever the hand is: elbows fold down and away from the body; rolled a little with the wrist's twist.
        /// </summary>
        private static void FoldElbowPrefix(InverseKinematics __instance)
        {
            if (!ForestVR.Enabled) return;
            var arm = __instance;
            if (!arm.upperArm || !arm.target) return;
            RestoreStretch(arm);
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
            var offset = Vector3.down * ElbowDown + outward.normalized * ElbowOut + back.normalized * ElbowBack;
            // The elbow rolls a little with the wrist's twist (the upper arm turning with the forearm).
            if (Rests.TryGetValue(arm, out var rest))
            {
                float roll = Mathf.Clamp(rest.Twist * ElbowTwistShare, -ElbowTwistLimit, ElbowTwistLimit);
                var along = arm.target.position - arm.upperArm.position;
                if (along.sqrMagnitude > 1e-6f) offset = Quaternion.AngleAxis(roll, along.normalized) * offset;
            }
            hint.position = middle + offset;
            arm.elbow = hint;
        }

        /// <summary>The player an arm belongs to (its facing): another player's network body, else you.</summary>
        private static Transform Body(InverseKinematics arm)
        {
            if (Bodies.TryGetValue(arm, out var body) && body) return body;
            var sync = arm.GetComponentInParent<VRCoopPlayerSync>();
            if (sync && !(sync.entity && sync.entity.isOwner)) body = sync.transform;
            else
            {
                // You: the skeleton itself, which BodyPlacement turns to where you face.
                var adapter = LocalPlayer.vrAdapter;
                if (adapter && adapter.JointsOffsetTransform) return adapter.JointsOffsetTransform;
                body = LocalPlayer.Transform ? LocalPlayer.Transform : arm.transform.root;
            }
            Bodies[arm] = body;
            return body;
        }

        // Each arm as the rig has it before posing: the forearm's and hand's local positions, the hand's local
        // rotation, the forearm twist bones' local positions and rotations (and how far along the forearm each sits),
        // the bone lengths; the watch's place on the left hand. Everything posed here is put back to this each frame.
        private sealed class Rest
        {
            public Vector3 Forearm, Hand;
            public Quaternion HandRotation;
            public float UpperLength, ForearmLength;
            public Transform[] Twists;
            public Vector3[] TwistPositions;
            public Quaternion[] TwistRotations;
            public float[] TwistAlong;
            public float Twist; // the wrist's twist last frame, smoothed (degrees, around the forearm)
            public bool Posed; // stretched / twisted / the watch placed since the last restore
            public Transform Watch;
            public Vector3 WatchPosition, WatchFromTwistPosition;
            public Quaternion WatchRotation, WatchFromTwistRotation;
        }

        private static readonly Dictionary<InverseKinematics, Rest> Rests = new Dictionary<InverseKinematics, Rest>();

        /// <summary>How much of the wrist's twist the elbow takes, rolling up or down around the arm (and its limit).</summary>
        private const float ElbowTwistShare = 0.3f, ElbowTwistLimit = 30f;

        /// <summary>The watch: carried by the forearm, its angle halfway between the forearm's and the hand's.</summary>
        private const float WatchFollowsHand = 0.5f;

        private static void Unpose(InverseKinematics arm)
        {
            if (arm && Rests.TryGetValue(arm, out var rest) && rest.Posed)
            {
                RestoreStretch(arm);
                rest.Posed = false;
            }
        }

        /// <summary>Before posing: the arm's bones back as the rig has them (the stretch and twist are redone each frame).</summary>
        private static void RestoreStretch(InverseKinematics arm)
        {
            if (!arm.forearm || !arm.hand) return;
            if (!Rests.TryGetValue(arm, out var rest))
            {
                Rests[arm] = Measure(arm);
                return;
            }
            arm.forearm.localPosition = rest.Forearm;
            arm.hand.localPosition = rest.Hand;
            for (int i = 0; i < rest.Twists.Length; i++)
            {
                if (!rest.Twists[i]) continue;
                rest.Twists[i].localPosition = rest.TwistPositions[i];
                rest.Twists[i].localRotation = rest.TwistRotations[i];
            }
            if (rest.Watch && rest.Watch.parent == arm.hand)
            {
                rest.Watch.localPosition = rest.WatchPosition;
                // Its rotation as the game sets it unmirrored (VRPlayerControl.watchParentLocalRot; UnmirrorArms too).
                var vr = LocalPlayer.vrPlayerControl;
                rest.Watch.localRotation = _watchRotation != null && vr ? (Quaternion)_watchRotation.GetValue(vr) : rest.WatchRotation;
            }
        }

        private static Rest Measure(InverseKinematics arm)
        {
            var twists = new List<Transform>();
            foreach (var t in arm.forearm.GetComponentsInChildren<Transform>(true))
                if (t != arm.forearm && t.name.IndexOf("ForeArmTwist", StringComparison.OrdinalIgnoreCase) >= 0) twists.Add(t);
            twists.Sort((a, b) => Depth(a).CompareTo(Depth(b))); // the chain from the elbow to the wrist
            float length = Vector3.Distance(arm.forearm.position, arm.hand.position);
            var axis = (arm.hand.position - arm.forearm.position).normalized;
            var rest = new Rest
            {
                Forearm = arm.forearm.localPosition,
                Hand = arm.hand.localPosition,
                HandRotation = arm.hand.localRotation,
                UpperLength = Vector3.Distance(arm.upperArm.position, arm.forearm.position),
                ForearmLength = length,
                Twists = twists.ToArray(),
                TwistPositions = twists.ConvertAll(t => t.localPosition).ToArray(),
                TwistRotations = twists.ConvertAll(t => t.localRotation).ToArray(),
                TwistAlong = twists.ConvertAll(t => length > 1e-4f ? Mathf.Clamp01(Vector3.Dot(t.position - arm.forearm.position, axis) / length) : 0f).ToArray(),
            };
            var adapter = LocalPlayer.vrAdapter;
            var watch = adapter && adapter.VRWatchParentGo ? adapter.VRWatchParentGo.transform : null;
            if (watch && watch.parent == arm.hand)
            {
                rest.Watch = watch;
                rest.WatchPosition = watch.localPosition;
                rest.WatchRotation = watch.localRotation;
                var reference = twists.Count > 0 ? twists[twists.Count - 1] : arm.forearm;
                rest.WatchFromTwistPosition = reference.InverseTransformPoint(watch.position);
                rest.WatchFromTwistRotation = Quaternion.Inverse(reference.rotation) * watch.rotation;
            }
            return rest;
        }

        private static int Depth(Transform t)
        {
            int depth = 0;
            for (; t; t = t.parent) depth++;
            return depth;
        }

        /// <summary>
        /// After posing:
        /// - Stretch: the game puts the hand right on the controller even when it is out of the arm's reach, so the hand
        ///   came off the forearm and the skin stretched at the wrist. The stretch is shared out along the whole arm:
        ///   the elbow moves out along it (the upper arm takes its share), and the forearm's twist bones spread evenly
        ///   between elbow and wrist (the forearm takes the rest), the hand staying on the controller.
        /// - Twist: the hand turns with the controller, the forearm doesn't, so the skin wrung at the wrist. The wrist's
        ///   twist is shared out along the forearm's twist bones (a quarter, half, three quarters...), and the elbow
        ///   rolls a little with it (next frame, by the elbow hint).
        /// - Elbow: never inside the body (KeepElbowOut).
        /// - Watch: it hangs from the hand; it's put where the forearm carries it and turned halfway back towards the
        ///   forearm, as on a real wrist.
        /// </summary>
        private static void StretchAtElbowPostfix(InverseKinematics __instance)
        {
            if (!ForestVR.Enabled) return;
            var arm = __instance;
            if (!arm.upperArm || !arm.forearm || !arm.hand || !arm.target || !Rests.TryGetValue(arm, out var rest)) return;

            rest.Posed = true;
            var toTarget = arm.target.position - arm.upperArm.position;
            float excess = toTarget.magnitude - (rest.UpperLength + rest.ForearmLength);
            if (excess > 0.001f)
            {
                var target = arm.hand.position;
                var handRotation = arm.hand.rotation;
                float upperShare = rest.UpperLength / Mathf.Max(1e-4f, rest.UpperLength + rest.ForearmLength);
                arm.forearm.position += toTarget.normalized * (excess * upperShare);
                arm.hand.SetPositionAndRotation(target, handRotation);
                var along = arm.hand.position - arm.forearm.position;
                for (int i = 0; i < rest.Twists.Length; i++)
                {
                    var t = rest.Twists[i];
                    if (!t) continue;
                    // Where it sits along the stretched forearm, the same share of the way as unstretched.
                    var onAxis = Vector3.Dot(t.position - arm.forearm.position, along.normalized);
                    t.position += along.normalized * (rest.TwistAlong[i] * along.magnitude - onAxis);
                }
            }

            KeepElbowOut(arm);

            // The wrist's twist around the forearm: the hand as turned, against the hand as the forearm carries it.
            var axis = (arm.hand.position - arm.forearm.position).normalized;
            var carried = arm.forearm.rotation * rest.HandRotation;
            float twist = TwistAround(arm.hand.rotation * Quaternion.Inverse(carried), axis);
            int n = rest.Twists.Length;
            float given = 0f;
            for (int i = 0; i < n; i++)
            {
                var t = rest.Twists[i];
                if (!t) continue;
                float share = twist * (i + 1) / (n + 1); // each twist bone turns this much in all (its parents included)
                t.rotation = Quaternion.AngleAxis(share - given, axis) * t.rotation;
                given = share;
            }
            rest.Twist = Mathf.Lerp(rest.Twist, twist, 0.2f);

            // Only with the body's own hands drawn (PlayerBody.UpdateArms): on the VR hands mesh, the watch stays as
            // the game puts it.
            if (rest.Watch && rest.Watch.parent == arm.hand && PlayerBody.BodyHandsShown)
            {
                var reference = n > 0 && rest.Twists[n - 1] ? rest.Twists[n - 1] : arm.forearm;
                var fromForearm = reference.rotation * rest.WatchFromTwistRotation;
                var fromForearmPosition = reference.TransformPoint(rest.WatchFromTwistPosition);
                // Moved sideways across the wrist by the setting (along the watch's own side, across the forearm).
                var across = fromForearm * Vector3.right;
                across -= axis * Vector3.Dot(across, axis);
                if (across.sqrMagnitude > 1e-6f) fromForearmPosition += across.normalized * PlayerBody.WatchSide;
                // Where the forearm carries it (the hand would swing it about the wrist, into the forearm or off to
                // a side as the wrist bends), moved along the forearm by the setting; turned partly with the hand.
                rest.Watch.SetPositionAndRotation(
                    fromForearmPosition + axis * PlayerBody.WatchOffset,
                    Quaternion.Slerp(fromForearm, rest.Watch.rotation, WatchFollowsHand));
            }
        }

        /// <summary>
        /// The elbow never inside the body: if it ends up nearer the body's middle than the shoulder is (and not
        /// in front of the chest, where crossed arms put it), the arm is turned around the line from shoulder to hand,
        /// the way that brings the elbow out, in 5° steps up to 90°. The hand stays on the controller.
        /// </summary>
        private static void KeepElbowOut(InverseKinematics arm)
        {
            var shoulder = arm.upperArm.parent;
            var torso = shoulder ? shoulder.parent : null; // char_Spine2: the chest's middle
            if (!torso) return;
            var body = Body(arm);
            var right = body.right;
            right.y = 0f;
            var forward = body.forward;
            forward.y = 0f;
            if (right.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f) return;
            right.Normalize();
            forward.Normalize();
            float side = arm.upperArm.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0 ? -1f : 1f;
            float minOut = Mathf.Abs(Vector3.Dot(arm.upperArm.position - torso.position, right)) * 0.85f;
            float Out() => side * Vector3.Dot(arm.forearm.position - torso.position, right) - minOut;
            bool Inside()
            {
                var elbow = arm.forearm.position;
                return Out() < 0f && Vector3.Dot(elbow - torso.position, forward) < ElbowInFront
                       && elbow.y < arm.upperArm.position.y + 0.05f && elbow.y > torso.position.y - 0.6f;
            }
            if (!Inside()) return;
            var axis = arm.hand.position - arm.upperArm.position;
            if (axis.sqrMagnitude < 1e-4f) return;
            axis.Normalize();
            var handPosition = arm.hand.position;
            var handRotation = arm.hand.rotation;
            // Which way out: try a step each way.
            arm.upperArm.RotateAround(arm.upperArm.position, axis, 5f);
            float plus = Out();
            arm.upperArm.RotateAround(arm.upperArm.position, axis, -10f);
            float minus = Out();
            arm.upperArm.RotateAround(arm.upperArm.position, axis, 5f);
            float step = plus >= minus ? 5f : -5f;
            for (int i = 0; i < 18 && Inside(); i++) arm.upperArm.RotateAround(arm.upperArm.position, axis, step);
            arm.hand.SetPositionAndRotation(handPosition, handRotation);
        }

        /// <summary>An elbow this far (metres) in front of the chest's middle is in front of the body, not inside it.</summary>
        private const float ElbowInFront = 0.12f;

        /// <summary>The signed angle (degrees) a rotation turns around an axis: its twist part (swing-twist).</summary>
        private static float TwistAround(Quaternion q, Vector3 axis)
        {
            var v = new Vector3(q.x, q.y, q.z);
            float along = Vector3.Dot(v, axis);
            float angle = 2f * Mathf.Atan2(along, q.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, angle);
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
        /// The fingers, swapped between the hands while mirrored. Each finger is posed by a VrFingerCurl component
        /// (after the animation, from its controller's grip: "VRLeftIndexCurl" bends the left index finger). Mirrored,
        /// the game feeds each hand's curls from the other controller, as vanilla showed the right hand at the left
        /// controller. With each arm at its own controller, each curl now bends the other hand's finger (its target
        /// bones swapped with its counterpart's), so the fingers follow the controller they're at. The rig is
        /// mirrored left to right with equal local rotations, so the curls' poses fit either hand.
        /// </summary>
        private static class Fingers
        {
            private static readonly Type CurlType = typeof(VRPlayerControl).Assembly.GetType("TheForest.Utils.VrFingerCurl");
            private static readonly FieldInfo Id = CurlType == null ? null : AccessTools.Field(CurlType, "_id");
            private static readonly FieldInfo Targets = CurlType == null ? null : AccessTools.Field(CurlType, "_targets");
            private static readonly List<KeyValuePair<Component, Component>> Pairs = new List<KeyValuePair<Component, Component>>();
            private static Component _player;
            private static bool _swapped;

            public static void Swap(VRPlayerControl vr)
            {
                if (_swapped || !Find()) return;
                Exchange();
                _swapped = true;
            }

            /// <summary>Unmirrored: each curl back on its own hand.</summary>
            public static void Restore()
            {
                if (!_swapped) return;
                _swapped = false;
                if (_player && LocalPlayer.Transform == _player.transform) Exchange();
            }

            private static void Exchange()
            {
                foreach (var pair in Pairs)
                {
                    if (!pair.Key || !pair.Value) continue;
                    var left = Targets.GetValue(pair.Key);
                    Targets.SetValue(pair.Key, Targets.GetValue(pair.Value));
                    Targets.SetValue(pair.Value, left);
                }
            }

            /// <summary>The player's finger curls, left with right by id ("VRLeftIndexCurl" with "VRRightIndexCurl").</summary>
            private static bool Find()
            {
                if (Id == null || Targets == null || !LocalPlayer.Transform) return false;
                if (_player == LocalPlayer.Transform) return Pairs.Count > 0;
                _player = LocalPlayer.Transform;
                Pairs.Clear();
                var rights = new Dictionary<string, Component>();
                var curls = LocalPlayer.Transform.GetComponentsInChildren(CurlType, true);
                foreach (var curl in curls)
                    if (Id.GetValue(curl) is string id && id.StartsWith("VRRight", StringComparison.Ordinal)) rights[id.Substring(7)] = curl;
                foreach (var curl in curls)
                    if (Id.GetValue(curl) is string id && id.StartsWith("VRLeft", StringComparison.Ordinal) && rights.TryGetValue(id.Substring(6), out var right))
                        Pairs.Add(new KeyValuePair<Component, Component>(curl, right));
                Plugin.Log.LogInfo($"Handedness: {Pairs.Count} finger curls paired for mirrored hands");
                return Pairs.Count > 0;
            }
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
