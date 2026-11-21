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
    /// (VRCameraOrigin), which sits about 23 cm ahead of the spine, so the body always leaned forward some 13°; here
    /// the spine bends by how far your neck is from where it would be on the upright body (BodyPlacement stands the
    /// body under it, so this is the lean within its follow slack). Otherwise as vanilla (clamps, bend amount,
    /// smoothing, the twist when you turn your head).
    /// </summary>
    internal static class SpinePatch
    {
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

            // Your lean: where your eyes are (head held level) against where they'd be on the upright body, in the
            // body's frame; the spine bends that way, clamped and scaled as vanilla. Height: the game's head point.
            var forward = BodyPlacement.BodyForward(vr);
            var right = Vector3.Cross(Vector3.up, forward);
            var lean = BodyPlacement.LevelEyes(follow) - (spine.position + forward * (BodyPlacement.EyesAheadOfSpine + BodyPlacement.BackNow));
            float x = Mathf.Clamp(Vector3.Dot(lean, right), -vr.xClampValue, vr.xClampValue) * vr.spineBendAmount;
            float z = Mathf.Clamp(Vector3.Dot(lean, forward), -vr.zClampValue, vr.zClampValue) * vr.spineBendAmount;
            var target = spine.position + right * x + forward * z;
            target.y = origin.position.y;
            ref var smooth = ref _smoothPosition(vr);
            smooth = Vector3.SmoothDamp(smooth, target, ref _velRef(vr), 0.025f);
            var toTarget = smooth - spine.position;
            bool locked = DisableSpineInput();
            if (!locked) spine.rotation = Quaternion.LookRotation(toTarget, forward) * Quaternion.Euler(-90f, 90f, 90f);

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
