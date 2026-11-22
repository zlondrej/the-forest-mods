using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// Where the drawn body stands under your head. In game mode the skeleton hangs off the VR offset rig
    /// (VRPlayerAdapter.JointsOffsetTransform under VRPlayerControl.VROffsetTransform), placed and turned with the
    /// player: it followed you only once your head was 40 cm away (VRPlayerControl.MovePlayerThreshold) and turned
    /// only past 105° (RoomScaleCameraAlign). And the game's head point sits about 23 cm ahead of the spine, so the
    /// spine leaned the body forward to reach it. Here, every frame before the arms and spine are posed, the skeleton
    /// itself is placed (only it: the view, the player and its collider stay as the game has them):
    /// - turned to where you face once your head is turned more than <see cref="TurnSlack"/> from it, smoothly
    ///   (<see cref="TurnSlackLookingDown"/> while you look down at it, so it stays put while you look it over);
    /// - upright under your neck, a little behind your eyes, following once you are <see cref="FollowSlack"/> away
    ///   (within it, the spine leans towards you instead: SpinePatch), stepping as it goes: the game's small steps
    ///   for slow room-scale movement play for this movement too (StepsPostfix);
    /// - moved back by <see cref="PlayerBody.BackOffset"/> as you look down (<see cref="PlayerBody.BackOffsetAhead"/>
    ///   looking ahead or up), so the chest
    ///   isn't in front of your eyes.
    /// Knocked down (with the view left with the headset), it stays where it stood: the animation throws it from there.
    /// Not in scenes (the theatre or the immersive camera take the skeleton), nor with the body not drawn.
    /// </summary>
    internal static class BodyPlacement
    {
        /// <summary>Degrees your head may turn from the body before the body turns along.</summary>
        public static float TurnSlack = 8f;

        /// <summary>The same while you look down (blended in as for <see cref="PlayerBody.BackOffset"/>).</summary>
        public static float TurnSlackLookingDown = 15f;

        /// <summary>Degrees below the horizon where looking down starts to count, and over how many more it is all in.</summary>
        public static float LookDownFrom = 20f, LookDownOver = 15f;

        /// <summary>Metres your head may move from above the body before the body follows (leaning within it).</summary>
        public static float FollowSlack = 0.08f;

        /// <summary>Where your eyes are on an upright body: this far ahead of the spine (metres).</summary>
        public const float EyesAheadOfSpine = 0.06f;

        /// <summary>From the eyes to the neck's pivot, in the head's frame (metres): below and behind the eyes.</summary>
        private static readonly Vector3 EyesToNeck = new Vector3(0f, -0.11f, -0.09f);

        private const float TurnTime = 0.12f, FollowTime = 0.08f, LookDownTime = 0.15f, StepSmoothing = 0.1f; // seconds

        private static System.Reflection.FieldInfo _forwardMode;
        private static VRPlayerControl _vr;
        private static bool _placed;
        private static float _yaw, _lastBaseYaw, _lookDown;
        private static Vector3 _spineFromRoot, _followVelocity;
        private static AccessTools.FieldRef<playerAnimatorControl, Vector3> _modLocalVel;

        /// <summary>VR build only: before VRPlayerControl.LateUpdate (which bends the spine and solves the arms).</summary>
        public static bool Apply(Harmony harmony)
        {
            var lateUpdate = AccessTools.Method(typeof(VRPlayerControl), "LateUpdate");
            _forwardMode = AccessTools.Field(typeof(PlayerPreferences), "VRForwardMovement");
            if (lateUpdate == null || _forwardMode == null) return false;
            harmony.Patch(lateUpdate, prefix: new HarmonyMethod(typeof(BodyPlacement), nameof(Prefix)));
            var fixedUpdate = AccessTools.Method(typeof(playerAnimatorControl), "FixedUpdate");
            if (fixedUpdate != null && AccessTools.Field(typeof(playerAnimatorControl), "modLocalVel") != null)
            {
                _modLocalVel = AccessTools.FieldRefAccess<playerAnimatorControl, Vector3>("modLocalVel");
                harmony.Patch(fixedUpdate, postfix: new HarmonyMethod(typeof(BodyPlacement), nameof(StepsPostfix)));
            }
            else Plugin.Log.LogWarning("BodyPlacement: the room-scale steps weren't found, the body slides without stepping");
            return true;
        }

        /// <summary>
        /// The game animates small steps for room-scale movement from how far the player moved since the last physics
        /// step (playerAnimatorControl.modLocalVel, used while you aren't walking with the stick). The body following
        /// you moves too: its movement is added, in the body's own frame (it may be turned from the player's).
        /// </summary>
        private static void StepsPostfix(playerAnimatorControl __instance)
        {
            if (!ForestVR.Enabled || !_placed) return;
            var vr = LocalPlayer.vrPlayerControl;
            var joints = LocalPlayer.vrAdapter ? LocalPlayer.vrAdapter.JointsOffsetTransform : null;
            if (!vr || vr._startedMoving || !joints || __instance != LocalPlayer.AnimControl) return;
            ref var velocity = ref _modLocalVel(__instance);
            var world = __instance.transform.TransformDirection(velocity) + _followVelocity;
            velocity = joints.InverseTransformDirection(world);
        }

        /// <summary>The body's facing (flat), as placed: for the spine's lean.</summary>
        public static Vector3 BodyForward(VRPlayerControl vr)
        {
            var joints = LocalPlayer.vrAdapter ? LocalPlayer.vrAdapter.JointsOffsetTransform : null;
            var forward = joints ? joints.forward : vr.transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        }

        /// <summary>How far back the body is moved for looking down now (metres).</summary>
        public static float BackNow =>
            _placed ? Mathf.Lerp(PlayerBody.BackOffsetAhead, PlayerBody.BackOffset, Smooth01(_lookDown)) : 0f;

        /// <summary>Where the eyes would be with the head held level (turned the same way): the neck's movement only.</summary>
        public static Vector3 LevelEyes(Transform head)
        {
            var forward = head.forward;
            forward.y = 0f;
            var level = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward) : head.rotation;
            return head.position + head.rotation * EyesToNeck - level * EyesToNeck;
        }

        private static void Prefix(VRPlayerControl __instance)
        {
            if (!ForestVR.Enabled) return;
            try { CrouchBlend.Measure(); }
            catch (System.Exception e) { Plugin.Log.LogError($"CrouchBlend: {e}"); }
            try { Place(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"BodyPlacement: {e}");
                PlayerBody.Visible = false;
            }
        }

        private static void Place(VRPlayerControl vr)
        {
            var adapter = LocalPlayer.vrAdapter;
            var joints = adapter ? adapter.JointsOffsetTransform : null;
            var root = LocalPlayer.Transform;
            var head = vr.VRCamera;
            var spine = vr.spineBones != null && vr.spineBones.Length > 0 ? vr.spineBones[0] : null;
            if (vr != _vr)
            {
                _vr = vr;
                _placed = false;
            }
            bool applies = PlayerBody.Visible && joints && root && head && spine && vr.VROffsetTransform
                           && joints.parent == vr.VROffsetTransform && !InScene(vr);
            if (!applies)
            {
                if (_placed && joints && joints.parent == vr.VROffsetTransform) joints.localPosition = Vector3.zero;
                _placed = false;
                _followVelocity = Vector3.zero;
                return;
            }

            // Where the game puts the skeleton: at the rig's origin, turned as its constraint turns it.
            var baseRotation = vr.VROffsetTransform.rotation * GameLocalRotation(joints);
            joints.localPosition = Vector3.zero;
            joints.rotation = baseRotation;
            float baseYaw = baseRotation.eulerAngles.y;
            var spineAtBase = spine.position;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            bool hold = ImmersiveCamera.AnimatedKnockdown; // stays where it stood
            float headYaw = Yaw(head);
            var eyes = LevelEyes(head);
            if (!_placed)
            {
                _placed = true;
                _yaw = Mathf.DeltaAngle(baseYaw, headYaw);
                _lastBaseYaw = baseYaw;
                _lookDown = 0f;
                _spineFromRoot = Flat(spineAtBase - root.position);
                _followVelocity = Vector3.zero;
            }

            // Looking down: the body moves back from BackOffsetAhead to BackOffset, and turns later.
            float pitch = -Mathf.Asin(Mathf.Clamp(head.forward.y, -1f, 1f)) * Mathf.Rad2Deg; // positive = down
            float lookDownGoal = LookDownOver > 0.01f ? Mathf.Clamp01((pitch - LookDownFrom) / LookDownOver) : (pitch >= LookDownFrom ? 1f : 0f);
            if (!hold) _lookDown = Mathf.Lerp(_lookDown, lookDownGoal, 1f - Mathf.Exp(-dt / LookDownTime));

            // Turn: the game turns the player (and the skeleton with it) in steps; keep the body where it was.
            _yaw -= Mathf.DeltaAngle(_lastBaseYaw, baseYaw);
            _lastBaseYaw = baseYaw;
            float slack = Mathf.Lerp(TurnSlack, Mathf.Max(TurnSlack, TurnSlackLookingDown), Smooth01(_lookDown));
            float off = Mathf.DeltaAngle(_yaw, Mathf.DeltaAngle(baseYaw, headYaw));
            if (!hold && Mathf.Abs(off) > slack)
            {
                float goal = _yaw + off - Mathf.Sign(off) * slack;
                _yaw = Mathf.LerpAngle(_yaw, goal, 1f - Mathf.Exp(-dt / TurnTime));
            }
            _yaw = Mathf.DeltaAngle(0f, _yaw);
            var rotation = Quaternion.AngleAxis(_yaw, Vector3.up);
            var forward = rotation * Flat(baseRotation * Vector3.forward).normalized;

            // Follow: the spine under your neck (a little behind your eyes), once you're FollowSlack away. Kept
            // relative to the player's position, so walking moves it along; the game's own catching up (the player
            // moved under you) only slides it back into place. Walking with the stick, it settles right under you
            // (no slack: a lean left over from standing would look odd on the move).
            var goalFromRoot = Flat(eyes - forward * (EyesAheadOfSpine + BackNow) - root.position);
            var gap = hold ? Vector3.zero : goalFromRoot - _spineFromRoot;
            var before = _spineFromRoot;
            float slackNow = vr._startedMoving ? 0f : FollowSlack;
            if (gap.magnitude > 1f) _spineFromRoot = goalFromRoot; // teleported, respawned: no slide
            else if (gap.magnitude > slackNow)
            {
                var goal = _spineFromRoot + gap * (1f - slackNow / gap.magnitude);
                _spineFromRoot = Vector3.Lerp(_spineFromRoot, goal, 1f - Mathf.Exp(-dt / FollowTime));
            }
            var moved = gap.magnitude > 1f ? Vector3.zero : (_spineFromRoot - before) / dt;
            _followVelocity = Vector3.Lerp(_followVelocity, moved, 1f - Mathf.Exp(-dt / StepSmoothing));

            // The skeleton: turned around the spine, then moved so the spine is where it should be.
            var pivot = spineAtBase;
            var position = pivot + rotation * (joints.position - pivot);
            position += Flat(root.position + _spineFromRoot - pivot);
            joints.SetPositionAndRotation(position, rotation * baseRotation);
        }

        /// <summary>The skeleton's local rotation as invertRotateConstrain sets it (in FixedUpdate, so not every frame).</summary>
        private static Quaternion GameLocalRotation(Transform joints)
        {
            var constraint = joints.GetComponent<invertRotateConstrain>();
            bool player = _forwardMode.GetValue(null)?.ToString() == "PLAYER";
            if (!constraint || !constraint.enabled || !constraint.Target || player) return Quaternion.identity;
            var euler = constraint.Target.localEulerAngles;
            euler.y *= -1f;
            return Quaternion.Euler(euler);
        }

        private static float Yaw(Transform head)
        {
            // Looking (almost) straight down, the top of the head points where you face.
            var forward = head.forward;
            if (Mathf.Abs(forward.y) > 0.9f) forward = forward.y < 0f ? head.up : -head.up;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float Smooth01(float t) => t * t * (3f - 2f * t);

        /// <summary>Scenes (theatre mode or immersive camera) and the plane crash: the skeleton is theirs.</summary>
        private static bool InScene(VRPlayerControl vr)
        {
            var head = LocalPlayer.CamFollowHead;
            var inventory = LocalPlayer.Inventory;
            return vr.useGhostMode || (head && head.followAnim)
                   || (inventory && inventory.CurrentView == TheForest.Items.Inventory.PlayerInventory.PlayerViews.PlaneCrash);
        }
    }
}
