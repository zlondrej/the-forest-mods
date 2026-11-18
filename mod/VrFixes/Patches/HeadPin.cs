using System.Reflection;
using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// First person in scenes where the character's head moves far from where the player's real head is, e.g. hanging
    /// upside down in the cave: the headset only rotates the view (no positional tracking), and the view is pinned to
    /// the character's head as flat-screen's camera is (see <see cref="FlatView"/>; upside down included). The skeleton
    /// is anchored to the body as in flat-screen (and the theatre), so the animation plays as there. The direction the
    /// player faced when the pin engaged is "forward". Right before the eye camera renders, the VR rig is placed so the
    /// eye sits there with that rotation; the headset's rotation is added on top.
    ///
    /// A scene (<see cref="On"/>) may pin the view only part of the time (<c>pinWhile</c>, e.g. climbing: only getting
    /// on and off the rope); in between the headset tracks horizontally, the height stays the head's. When the view is
    /// released, it continues from where the pinned view was (same heading, same spot), so it doesn't jump.
    /// </summary>
    internal static class HeadPin
    {
        private static bool _on, _engaged;
        private static Transform _rig;
        private static Vector3 _rigLocalPosition;
        private static Quaternion _rigLocalRotation;
        private static Quaternion _yawCorrection;
        private static System.Func<bool> _keepWhile, _pinWhile;

        // InputTracking.disablePositionalTracking is in the VR build's Unity only (UnityEngine.XR in UnityEngine.VRModule).
        // Looked up in that assembly directly: searching all assemblies trips over ones that fail to load.
        private static readonly PropertyInfo DisablePositionalTracking =
            (System.Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.VRModule", false)
             ?? System.Type.GetType("UnityEngine.XR.InputTracking, UnityEngine", false))
            ?.GetProperty("disablePositionalTracking", BindingFlags.Public | BindingFlags.Static);

        /// <summary>A scene with a pin is running (skeleton on the body); the view may be pinned or free right now.</summary>
        public static bool On => _on;

        /// <summary>The view is pinned to the head right now.</summary>
        public static bool Engaged => _engaged;

        /// <summary>Kept after ghost mode ends, while the scene's <c>keepWhile</c> says so.</summary>
        public static bool Holding => _on && _keepWhile != null && Safe(_keepWhile, false);

        /// <summary>
        /// The view turns with the head joint whatever camFollowHead does: flat-screen's camera follows the animation
        /// there, VR's code skips it (cut down from hanging: the fall and getting up). Cleared when the pin stops.
        /// </summary>
        public static bool FollowHead;

        private static bool Safe(System.Func<bool> f, bool fallback)
        {
            try { return f(); }
            catch { return fallback; }
        }

        /// <param name="keepWhile">
        /// Keep the pin after ghost mode ends, while this is true (e.g. still hanging: the game ends ghost mode before the
        /// scene is over).
        /// </param>
        /// <param name="pinWhile">Pin the view only while this is true (null = all the time); checked every frame.</param>
        public static void Start(System.Func<bool> keepWhile = null, System.Func<bool> pinWhile = null)
        {
            if (_on) return;
            var vr = LocalPlayer.vrPlayerControl;
            if (!vr || !vr.VRCameraRig || !LocalPlayer.MainCamTr || !LocalPlayer.CamFollowHead) return;
            _keepWhile = keepWhile;
            _pinWhile = pinWhile;
            _rig = vr.VRCameraRig;
            FollowHead = false;
            AnchorSkeletonToBody();
            _on = true;
            Plugin.Debug("immersive camera: scene with the view pinned to the head started" + (pinWhile != null ? " (pinned part of the time)" : ""));
            if (Safe(_pinWhile ?? (() => true), true)) Engage();
        }

        public static void Stop()
        {
            if (!_on) return;
            Disengage(sceneEnds: true);
            RestoreSkeleton();
            _keepWhile = _pinWhile = null;
            FollowHead = false;
            _on = false;
            Plugin.Debug("immersive camera: scene with the view pinned ended");
        }

        /// <summary>
        /// The player is gone (game over, back to the menu) while a scene was pinned: positional tracking is a global
        /// setting, it must come back on or the menu's view stays frozen in place (out of step with the controllers).
        /// Everything else belonged to the old player and is dropped.
        /// </summary>
        public static void Abandon()
        {
            if (!_on && !_engaged && _continueFrom == null) return;
            Camera.onPreCull -= Pin;
            Camera.onPreCull -= HoldHeight;
            SetPositionalTracking(true);
            _on = _engaged = false;
            _continueFrom = null;
            _rig = null;
            _keepWhile = _pinWhile = null;
            FollowHead = false;
            _joints = _jointsParent = null;
            _jointsConstraint = null;
            Plugin.Debug("immersive camera: player gone during a pinned scene, positional tracking back on");
        }

        /// <summary>Every frame: pin or release the view as the scene's <c>pinWhile</c> says; finish a pending release.</summary>
        public static void Tick()
        {
            if (_continueFrom != null && Time.frameCount > _continueFrame) ContinueView();
            if (!_on || _pinWhile == null) return;
            bool pin = Safe(_pinWhile, true);
            if (pin && !_engaged) Engage();
            else if (!pin && _engaged) Disengage(sceneEnds: false);
        }

        private static void Engage()
        {
            if (_engaged || !_rig || !LocalPlayer.MainCamTr) return;
            Camera.onPreCull -= HoldHeight;
            _continueFrom = null;
            _rigLocalPosition = _rig.localPosition;
            _rigLocalRotation = _rig.localRotation;
            // Where the player looks relative to the rig, flattened to a heading (yaw only, no pitch or roll): that heading
            // becomes "forward".
            _yawCorrection = Quaternion.Euler(0f, -Heading(Quaternion.Inverse(_rig.rotation) * LocalPlayer.MainCamTr.rotation), 0f);
            _smoothing = false;
            _smoothedFrame = -1;
            SetPositionalTracking(false);
            Camera.onPreCull += Pin;
            _engaged = true;
            Plugin.Debug($"immersive camera: view pinned to the head (rotation only){(DisablePositionalTracking == null ? ", but positional tracking could not be turned off" : "")}");
        }

        /// <summary>
        /// Back to headset tracking: the rig's own pose back, then (next frame, once the headset's position is tracked
        /// again) turned and moved so the view continues from where the pinned view was. Within a scene (released only
        /// for a while, e.g. on the rope) the height stays the head's (<see cref="HoldHeight"/>); when the scene ends,
        /// normal tracking.
        /// </summary>
        private static void Disengage(bool sceneEnds)
        {
            Camera.onPreCull -= HoldHeight;
            if (!_engaged) return;
            Camera.onPreCull -= Pin;
            if (!sceneEnds)
            {
                _heightFrame = -1;
                Camera.onPreCull += HoldHeight;
            }
            var eye = LocalPlayer.MainCamTr;
            if (eye && LocalPlayer.Transform)
            {
                // Relative to the player: the scene may teleport the player right after (dying: into the cave).
                _continueFrom = LocalPlayer.Transform.InverseTransformPoint(eye.position);
                _continueHeading = Mathf.DeltaAngle(LocalPlayer.Transform.eulerAngles.y, Heading(eye.rotation));
                _continueFrame = Time.frameCount;
            }
            SetPositionalTracking(true);
            if (_rig)
            {
                _rig.localPosition = _rigLocalPosition;
                _rig.localRotation = _rigLocalRotation;
            }
            _engaged = false;
            Plugin.Debug("immersive camera: view released, positional tracking back on");
        }

        private static int _heightFrame = -1;
        private static float _height;

        /// <summary>
        /// Released within a scene: the headset moves the view horizontally, but its height stays at the character's
        /// head (sliding down a rope, the body drops faster than the view would follow, and the view ended up inside
        /// it). Right before the eye camera renders, the rig is moved up or down so the eye is at the head's height.
        /// </summary>
        private static void HoldHeight(Camera cam)
        {
            if (cam != LocalPlayer.MainCam || !_rig) return;
            if (_heightFrame != Time.frameCount)
            {
                if (!FlatView(out var position, out _)) return;
                _height = _heightFrame < 0 ? position.y : Mathf.Lerp(_height, position.y, Follow(PositionSmoothing));
                _heightFrame = Time.frameCount;
            }
            _rig.position += Vector3.up * (_height - cam.transform.position.y);
        }

        private static Vector3? _continueFrom;
        private static float _continueHeading;
        private static int _continueFrame;

        private static void ContinueView()
        {
            var from = _continueFrom.Value;
            _continueFrom = null;
            var vr = LocalPlayer.vrPlayerControl;
            var player = LocalPlayer.Transform;
            if (!vr || !vr.VRCameraRig || !LocalPlayer.MainCamTr || !player) return;
            var position = player.TransformPoint(from);
            // Safety net: never carry the view far away (it should be within the player's reach).
            if ((position - LocalPlayer.MainCamTr.position).sqrMagnitude > 9f) position = LocalPlayer.MainCamTr.position;
            AlignView(vr.VRCameraRig, LocalPlayer.MainCamTr, position, player.eulerAngles.y + _continueHeading);
        }

        /// <summary>
        /// Turn the rig around the eye so the view has this heading, and move it so the eye is (horizontally) at this
        /// position; height stays the player's own.
        /// </summary>
        public static void AlignView(Transform rig, Transform eye, Vector3 position, float heading)
        {
            float turn = Mathf.DeltaAngle(Heading(eye.rotation), heading);
            rig.RotateAround(eye.position, Vector3.up, turn);
            var offset = position - eye.position;
            offset.y = 0f;
            rig.position += offset;
            Plugin.Debug($"immersive camera: view turned {turn:0}° and moved {offset.magnitude:0.00} m");
        }

        /// <summary>Smoothing time constants (seconds) of the pinned view; 0 = none.</summary>
        public static float RotationSmoothing = 0.35f, PositionSmoothing = 0.1f;

        // Switching between following the head's rotation and the anchor's is blended over this time (flat-screen
        // snaps, but in VR a snap of the whole view is uncomfortable).
        private const float SwitchBlendTime = 0.3f;

        private static int _smoothedFrame = -1;
        private static bool _smoothing, _followedHead;
        private static Vector3 _smoothPosition;
        private static Quaternion _smoothRotation, _blendFrom;
        private static float _blendStart = -10f;

        /// <summary>
        /// Where flat-screen's camera is (without mouse look): always at the head joint (plus the flat camera's offset);
        /// rotated with the head only while the scene asks the camera to follow the animation (camFollowHead.followAnim,
        /// e.g. while hanging until the lighter, or <see cref="FollowHead"/>), otherwise as the camera anchor (the body's
        /// direction, or frozen where the following stopped). VR's own anchor stops following the head's position then,
        /// so the head joint is used.
        /// </summary>
        public static bool FlatView(out Vector3 position, out Quaternion rotation) => FlatView(out position, out rotation, out _);

        private static bool FlatView(out Vector3 position, out Quaternion rotation, out bool followsHead)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            followsHead = false;
            var follow = LocalPlayer.CamFollowHead;
            if (!follow) return false;
            var anchor = follow.transform;
            var head = follow.headJnt;
            followsHead = head && (FollowHead || follow.followAnim || (LocalPlayer.AnimControl && LocalPlayer.AnimControl.endGameCutScene));
            rotation = followsHead ? head.rotation : anchor.rotation;
            var flatCam = LocalPlayer.ScriptSetup ? LocalPlayer.ScriptSetup.playerCam : null;
            position = (head ? head.position : anchor.position) + rotation * (flatCam ? flatCam.localPosition : Vector3.zero);
            return true;
        }

        /// <summary>
        /// Right before the eye camera renders: the rig goes where the eye sits at <see cref="FlatView"/>, smoothed (once
        /// per frame, the eyes may render separately) so the head's small animated movements don't shake the view.
        /// </summary>
        private static void Pin(Camera cam)
        {
            if (cam != LocalPlayer.MainCam || !_rig) return;
            if (_smoothedFrame != Time.frameCount)
            {
                _smoothedFrame = Time.frameCount;
                if (!FlatView(out var position, out var rotation, out bool followsHead)) return;
                if (_smoothing && followsHead != _followedHead)
                {
                    _blendFrom = _smoothRotation;
                    _blendStart = Time.time;
                }
                _followedHead = followsHead;
                float blend = (Time.time - _blendStart) / SwitchBlendTime;
                if (blend < 1f) rotation = Quaternion.Slerp(_blendFrom, rotation, Mathf.SmoothStep(0f, 1f, blend));
                if (!_smoothing || (position - _smoothPosition).sqrMagnitude > 4f) // first frame, or teleported
                {
                    _smoothPosition = position;
                    _smoothRotation = rotation;
                    _smoothing = true;
                }
                else
                {
                    _smoothRotation = Quaternion.Slerp(_smoothRotation, rotation, Follow(RotationSmoothing));
                    _smoothPosition = Vector3.Lerp(_smoothPosition, position, Follow(PositionSmoothing));
                }
            }
            if (!_smoothing) return;
            _rig.rotation = _smoothRotation * _yawCorrection;
            _rig.position += _smoothPosition - cam.transform.position;
        }

        // How far to move toward the target this frame, for an exponential follow with time constant tau.
        private static float Follow(float tau) => tau <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / tau);

        /// <summary>
        /// Heading (yaw, degrees) of a rotation: its forward direction projected on the floor; when it points almost
        /// straight up or down, its up direction (looking down, the top of the head points forward).
        /// </summary>
        public static float Heading(Quaternion rotation)
        {
            var heading = rotation * Vector3.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.01f)
            {
                heading = rotation * Vector3.up;
                heading.y = 0f;
            }
            return heading.sqrMagnitude > 1e-6f ? Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg : 0f;
        }

        // ------------------------------------------------------------------ skeleton

        private static Transform _joints, _jointsParent;
        private static Vector3 _jointsLocalPosition;
        private static Quaternion _jointsLocalRotation;
        private static Behaviour _jointsConstraint;
        private static bool _jointsConstraintEnabled;

        /// <summary>
        /// In VR the character's skeleton hangs off the headset rig (VRPlayerAdapter.JointsOffsetTransform under the VR
        /// offset), so the body follows the player's real head; scripted animations then play offset (hanging beside the
        /// rope, falling the wrong way). The theatre anchors the skeleton to the player base for its screen, as in
        /// flat-screen; do the same here, so the animation plays as in flat-screen with the view at its head.
        /// </summary>
        private static void AnchorSkeletonToBody()
        {
            var adapter = LocalPlayer.vrAdapter;
            if (!adapter || !adapter.JointsOffsetTransform || !LocalPlayer.PlayerBase) return;
            _joints = adapter.JointsOffsetTransform;
            _jointsParent = _joints.parent;
            _jointsLocalPosition = _joints.localPosition;
            _jointsLocalRotation = _joints.localRotation;
            _jointsConstraint = _joints.GetComponent<invertRotateConstrain>();
            _jointsConstraintEnabled = _jointsConstraint && _jointsConstraint.enabled;
            _joints.parent = LocalPlayer.PlayerBase.transform;
            if (_jointsConstraint) _jointsConstraint.enabled = false;
            _joints.localPosition = Vector3.zero;
            _joints.localRotation = Quaternion.identity;
            Plugin.Debug($"immersive camera: skeleton anchored to the body (was under '{(_jointsParent ? _jointsParent.name : "nothing")}')");
        }

        private static void RestoreSkeleton()
        {
            if (!_joints) return;
            if (_jointsParent) _joints.parent = _jointsParent;
            _joints.localPosition = _jointsLocalPosition;
            _joints.localRotation = _jointsLocalRotation;
            if (_jointsConstraint) _jointsConstraint.enabled = _jointsConstraintEnabled;
            _joints = _jointsParent = null;
            _jointsConstraint = null;
        }

        private static void SetPositionalTracking(bool on)
        {
            try { DisablePositionalTracking?.SetValue(null, !on, null); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"ImmersiveCamera: could not switch positional tracking: {e.Message}"); }
        }
    }
}
