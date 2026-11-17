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
    /// player faced when it started is "forward". Right before the eye camera renders, the VR rig is placed so the eye
    /// sits there with that rotation; the headset's rotation is added on top.
    /// </summary>
    internal static class HeadPin
    {
        private static bool _on;
        private static Transform _rig;
        private static Vector3 _rigLocalPosition;
        private static Quaternion _rigLocalRotation;
        private static Quaternion _yawCorrection;

        // InputTracking.disablePositionalTracking is in the VR build's Unity only (UnityEngine.XR in UnityEngine.VRModule).
        // Looked up in that assembly directly: searching all assemblies trips over ones that fail to load.
        private static readonly PropertyInfo DisablePositionalTracking =
            (System.Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.VRModule", false)
             ?? System.Type.GetType("UnityEngine.XR.InputTracking, UnityEngine", false))
            ?.GetProperty("disablePositionalTracking", BindingFlags.Public | BindingFlags.Static);

        private static System.Func<bool> _keepWhile;

        public static bool On => _on;

        /// <summary>Pinned while the scene's ghost mode lasts, or while <see cref="Hold"/> says so.</summary>
        public static bool Holding => _on && _keepWhile != null && SafeKeep();

        private static bool SafeKeep()
        {
            try { return _keepWhile(); }
            catch { return false; }
        }

        /// <param name="keepWhile">
        /// Keep the pin after ghost mode ends, while this is true (e.g. still hanging: the game ends ghost mode before the
        /// scene is over).
        /// </param>
        public static void Start(System.Func<bool> keepWhile = null)
        {
            if (_on) return;
            _keepWhile = keepWhile;
            var vr = LocalPlayer.vrPlayerControl;
            var eye = LocalPlayer.MainCamTr;
            if (!vr || !vr.VRCameraRig || !eye || !LocalPlayer.CamFollowHead) return;
            _rig = vr.VRCameraRig;
            _rigLocalPosition = _rig.localPosition;
            _rigLocalRotation = _rig.localRotation;
            // Where the player looks relative to the rig, flattened to a heading (yaw only, no pitch or roll): that heading
            // becomes "forward". From the look direction projected on the floor (not Euler angles, which can flip when
            // the head is tilted); looking almost straight up or down, the head's up direction gives the heading.
            _yawCorrection = Quaternion.Euler(0f, -Heading(Quaternion.Inverse(_rig.rotation) * eye.rotation), 0f);
            _smoothing = false;
            _smoothedFrame = -1;
            SetPositionalTracking(false);
            AnchorSkeletonToBody();
            Camera.onPreCull += Pin;
            _on = true;
            Plugin.Debug($"immersive camera: view pinned to the head (rotation only){(DisablePositionalTracking == null ? ", but positional tracking could not be turned off" : "")}");
        }

        public static void Stop()
        {
            if (!_on) return;
            Camera.onPreCull -= Pin;
            SetPositionalTracking(true);
            RestoreSkeleton();
            if (_rig)
            {
                _rig.localPosition = _rigLocalPosition;
                _rig.localRotation = _rigLocalRotation;
            }
            _rig = null;
            _keepWhile = null;
            _on = false;
            Plugin.Debug("immersive camera: view unpinned, positional tracking back on");
        }

        /// <summary>Smoothing time constants (seconds) of the pinned view; 0 = none.</summary>
        public static float RotationSmoothing = 0f, PositionSmoothing = 0f;

        private static int _smoothedFrame = -1;
        private static bool _smoothing;
        private static Vector3 _smoothPosition;
        private static Quaternion _smoothRotation;

        /// <summary>
        /// Where flat-screen's camera is (without mouse look): always at the head joint (plus the flat camera's offset);
        /// rotated with the head only while the scene asks the camera to follow the animation (camFollowHead.followAnim,
        /// e.g. while hanging until the lighter), otherwise as the camera anchor (the body's direction, or frozen where
        /// the following stopped). VR's own anchor stops following the head's position then, so the head joint is used.
        /// </summary>
        public static bool FlatView(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            var follow = LocalPlayer.CamFollowHead;
            if (!follow) return false;
            var anchor = follow.transform;
            var head = follow.headJnt;
            bool followsHead = follow.followAnim || (LocalPlayer.AnimControl && LocalPlayer.AnimControl.endGameCutScene);
            rotation = head && followsHead ? head.rotation : anchor.rotation;
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
                if (!FlatView(out var position, out var rotation)) return;
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
