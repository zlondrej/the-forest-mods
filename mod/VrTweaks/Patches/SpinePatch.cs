using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// VR bends the body's spine towards your head (VRPlayerControl.updateSpineRotation), so leaning in your room leans
    /// the body. It followed your eyes, though, and tilting the head to look down moves the eyes forward (the head
    /// turns around the neck): the body leaned forward into the view when you only looked down. Here the spine
    /// follows where your eyes would be with the head held level, i.e. the neck's movement: looking down keeps the
    /// body upright, leaning still leans it. And upright means upright: vanilla bent towards its head point
    /// (VRCameraOrigin), which sits about 25 cm ahead of the spine, so the body always leaned forward some 13°; here
    /// the spine bends by how far your neck is from that point, starting from straight up (PlayerBody brings the
    /// body forward under your head instead). Otherwise as vanilla (clamps, bend amount, smoothing, the twist when you
    /// turn your head).
    /// </summary>
    internal static class SpinePatch
    {
        /// <summary>From the eyes to the neck's pivot, in the head's frame (metres): below and behind the eyes.</summary>
        private static readonly Vector3 EyesToNeck = new Vector3(0f, -0.11f, -0.09f);

        private static AccessTools.FieldRef<VRPlayerControl, Vector3> _smoothPosition, _velRef;
        private static AccessTools.FieldRef<VRPlayerControl, float> _smoothTargetAngle;

        /// <summary>VR build only.</summary>
        public static bool Apply(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(VRPlayerControl), "updateSpineRotation");
            if (method == null || AccessTools.Field(typeof(VRPlayerControl), "smoothPosition") == null
                || AccessTools.Field(typeof(VRPlayerControl), "velRef") == null
                || AccessTools.Field(typeof(VRPlayerControl), "smoothTargetAngle") == null)
                return false;
            _smoothPosition = AccessTools.FieldRefAccess<VRPlayerControl, Vector3>("smoothPosition");
            _velRef = AccessTools.FieldRefAccess<VRPlayerControl, Vector3>("velRef");
            _smoothTargetAngle = AccessTools.FieldRefAccess<VRPlayerControl, float>("smoothTargetAngle");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(SpinePatch), nameof(Prefix)));
            return true;
        }

        /// <summary>Replaces VRPlayerControl.updateSpineRotation.</summary>
        private static bool Prefix(VRPlayerControl __instance)
        {
            if (!ForestVR.Enabled) return true;
            var vr = __instance;
            var origin = vr.VRCameraOrigin;
            var follow = vr.followTarget;
            var spine = vr.spineBones != null && vr.spineBones.Length > 0 ? vr.spineBones[0] : null;
            if (vr.useGhostMode || !origin || !follow || !spine || !vr.headTransform) return false;

            // Where the eyes would be with the head level (turned the same way): the neck's movement only.
            var forward = follow.forward;
            forward.y = 0f;
            var level = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward) : follow.rotation;
            var eyes = follow.position + follow.rotation * EyesToNeck - level * EyesToNeck;

            var local = origin.InverseTransformPoint(eyes);
            local.x = Mathf.Clamp(local.x, -vr.xClampValue, vr.xClampValue) * vr.spineBendAmount;
            local.z = Mathf.Clamp(local.z, -vr.zClampValue, vr.zClampValue) * vr.spineBendAmount;
            // Upright over the spine with your head at the head point; leaning (your neck away from it) leans it.
            var target = origin.TransformPoint(local);
            target.x += spine.position.x - origin.position.x;
            target.z += spine.position.z - origin.position.z;
            target.y = origin.position.y;
            ref var smooth = ref _smoothPosition(vr);
            smooth = Vector3.SmoothDamp(smooth, target, ref _velRef(vr), 0.025f);
            var toTarget = smooth - spine.position;
            bool locked = DisableSpineInput();
            if (!locked) spine.rotation = Quaternion.LookRotation(toTarget, vr.transform.forward) * Quaternion.Euler(-90f, 90f, 90f);

            // The twist as you turn your head, as vanilla.
            var look = vr.headTransform.InverseTransformPoint(follow.position + follow.forward * 10f);
            float twist = Mathf.Clamp(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg / (110f * vr.spineTwistAmount), -1f, 1f);
            ref var angle = ref _smoothTargetAngle(vr);
            angle = Mathf.Lerp(angle, twist, Time.deltaTime * 13f);
#pragma warning disable 618 // as vanilla: Transform.RotateAround(axis, angle)
            if (!locked) spine.RotateAround(toTarget, angle);
#pragma warning restore 618
            return false;
        }

        /// <summary>VRPlayerControl.disableSpineInput: animations that move the body themselves.</summary>
        private static bool DisableSpineInput()
        {
            var anim = LocalPlayer.AnimControl;
            var head = LocalPlayer.CamFollowHead;
            return (anim && (anim.useRootMotion || anim.doingGroundChop)) || (head && head.followAnim);
        }
    }
}
