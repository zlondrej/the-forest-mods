using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// Physical crouching (the game's VR option): the game crouches the character once your eyes are below 1.26 m above
    /// the floor (VRPlayerControl.UpdateCrouchHeight), ramping the animator's "crouch" parameter from 0 (standing) to 10
    /// (crouched) in 0.18 s (FirstPersonCharacter.EnableCrouch). Until then the body stands at full height under you;
    /// past it, it is fully crouched while you may be barely lower, and you looked down at its neck. The animator blends
    /// the standing pose into the crouched one by that parameter (its idle blend tree), so here the parameter follows how
    /// far your eyes are below their standing height: 0 within <see cref="From"/>, 10 at <see cref="Depth"/> further
    /// down. Your body (and its shadow, and how others see you) crouches as far as you do.
    ///
    /// The game's crouching itself (stealth, the collider, crouch walking) still switches at its own height. The
    /// animator switches walking to crouch walking at 5, so the parameter stays below 5 while the game has you standing
    /// and at 5 or more while it has you crouched: the two agree, at the cost of a small jump where they differ.
    ///
    /// Your standing eye height is learned as you play: the highest your eyes have been, settling down slowly (over
    /// tens of seconds) while you stand near it, so a posture change is followed but crouching for a while is not.
    /// </summary>
    internal static class CrouchBlend
    {
        /// <summary>Gradual crouching on (with the game's physical crouching).</summary>
        public static bool Enabled = true;

        /// <summary>Metres your eyes go below their standing height before the body starts to crouch.</summary>
        public static float From = 0.05f;

        /// <summary>Metres further down where the body is fully crouched (about how far the character's head drops).</summary>
        public static float Depth = 0.5f;

        private const string Param = "crouch";
        private const float Crouched = 10f, Switch = 5f, BelowSwitch = 4.9f;
        private const float Smoothing = 0.08f, StandSettle = 20f, StandBand = 0.1f; // seconds, seconds, metres

        private static VRPlayerControl _vr;
        private static bool _active, _standKnown;
        private static float _stand, _value, _written = float.NaN;

        /// <summary>VR build only: the game's own crouch ramps (coroutines) would overwrite the parameter every frame.</summary>
        public static bool Apply(Harmony harmony)
        {
            var postfix = new HarmonyMethod(typeof(CrouchBlend), nameof(RampPostfix));
            foreach (var name in new[] { "EnableCrouch", "DisableCrouch" })
            {
                var moveNext = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(FirstPersonCharacter), name));
                if (moveNext == null) return false;
                harmony.Patch(moveNext, postfix: postfix);
            }
            return true;
        }

        /// <summary>After each step of the game's ramp: the parameter as it is here, on the side of 5 the game is on now.</summary>
        private static void RampPostfix(FirstPersonCharacter __instance)
        {
            if (!_active || __instance != LocalPlayer.FpCharacter || !LocalPlayer.Animator) return;
            Write(LocalPlayer.Animator, OnGameSide(_value, __instance.crouching));
        }

        /// <summary>Every frame, before the animator runs.</summary>
        public static void Update()
        {
            if (!ForestVR.Enabled) return;
            var vr = LocalPlayer.vrPlayerControl;
            var animator = LocalPlayer.Animator;
            var fps = LocalPlayer.FpCharacter;
            if (vr != _vr)
            {
                _vr = vr;
                _active = _standKnown = false;
                _written = float.NaN;
            }
            bool applies = Enabled && PlayerPreferences.VRUsePhysicalCrouching && vr && animator && fps && fps.enabled
                           && vr.VRCamera && vr.VRCameraRig && !vr.useGhostMode && !HeadPin.On && Free();
            if (!applies)
            {
                // Back to the game's own value, unless something else set the parameter since (a scene setting it to 0).
                if (_active && animator && Mathf.Approximately(animator.GetFloat(Param), _written))
                    Write(animator, fps && fps.crouching ? Crouched : 0f);
                _active = false;
                return;
            }

            // Your eyes' height above the floor, in the tracking space (as the game measures it for crouching).
            float eyes = vr.VRCameraRig.InverseTransformPoint(vr.VRCamera.position).y;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (!_standKnown || eyes > _stand)
            {
                _stand = eyes;
                _standKnown = true;
            }
            else if (eyes > _stand - StandBand) _stand = Mathf.Lerp(_stand, eyes, 1f - Mathf.Exp(-dt / StandSettle));
            float drop = (_stand - eyes) * vr.VRCameraRig.lossyScale.y; // metres in the game world
            float blend = Depth > 0.01f ? Mathf.Clamp01((drop - From) / Depth) : (drop > From ? 1f : 0f);
            float goal = OnGameSide(blend * Crouched, fps.crouching);

            if (!_active)
            {
                _active = true;
                _value = animator.GetFloat(Param);
            }
            _value = Mathf.Lerp(_value, goal, 1f - Mathf.Exp(-dt / Smoothing));
            Write(animator, _value);
        }

        /// <summary>Below 5 while the game has you standing, 5 or more while crouched.</summary>
        private static float OnGameSide(float value, bool crouching) =>
            crouching ? Mathf.Max(value, Switch) : Mathf.Min(value, BelowSwitch);

        private static void Write(Animator animator, float value)
        {
            animator.SetFloat(Param, value);
            _written = value;
        }

        /// <summary>Plain standing or walking in the world: not swimming, on a rope, riding, sitting, in a scene...</summary>
        private static bool Free()
        {
            var anim = LocalPlayer.AnimControl;
            var inventory = LocalPlayer.Inventory;
            return anim && inventory && inventory.CurrentView == PlayerInventory.PlayerViews.World
                   && !anim.swimming && !anim.onRope && !anim.doShellRideMode && !anim.doSledPushMode && !anim.enteringACave
                   && !anim.endGameCutScene && !anim.useRootMotion && !anim.sitting && !anim.onRockThrower && !anim.onRaft
                   && !anim.introCutScene && !anim.knockedDown;
        }

        // The character's head height over its skeleton's root while plainly standing and fully crouched, for the log:
        // their difference is what Depth should be.
        private static float _standingHead = float.NaN, _crouchedHead = float.NaN, _loggedDrop = float.NaN;

        /// <summary>Every frame after the animator, before the spine and arms are posed (BodyPlacement).</summary>
        public static void Measure()
        {
            var animator = LocalPlayer.Animator;
            var setup = LocalPlayer.ScriptSetup;
            var joints = LocalPlayer.vrAdapter ? LocalPlayer.vrAdapter.JointsOffsetTransform : null;
            if (!animator || !setup || !setup.headJnt || !joints || animator.IsInTransition(0) || !Free()) return;
            var rb = LocalPlayer.Rigidbody;
            if (rb && new Vector2(rb.velocity.x, rb.velocity.z).sqrMagnitude > 0.01f) return; // standing still only
            float c = animator.GetFloat(Param);
            float height = setup.headJnt.position.y - joints.position.y;
            if (c <= 0.05f) _standingHead = Smooth(_standingHead, height);
            else if (c >= Crouched - 0.05f) _crouchedHead = Smooth(_crouchedHead, height);
            else return;
            float drop = _standingHead - _crouchedHead;
            if (float.IsNaN(drop) || (!float.IsNaN(_loggedDrop) && Mathf.Abs(drop - _loggedDrop) < 0.02f)) return;
            _loggedDrop = drop;
            Plugin.Log.LogInfo($"CrouchBlend: the character's head drops {drop:0.00} m from standing to crouched "
                               + $"(CrouchDepth = {Depth:0.00}); your standing eye height: {_stand:0.00} m");
        }

        private static float Smooth(float old, float now) => float.IsNaN(old) ? now : Mathf.Lerp(old, now, 0.1f);
    }
}
