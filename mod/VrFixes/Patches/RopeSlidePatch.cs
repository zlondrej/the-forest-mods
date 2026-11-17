using HarmonyLib;
using TheForest.Utils;

namespace VrFixes
{
    /// <summary>
    /// On a rope, the climbing animation (and its fast slide, which starts once the animator's vSpeed passes about -16,
    /// i.e. sprint + down) is driven by the stick input. In VR, while the player isn't "moving" by VR standards
    /// (VRPlayerControl._startedMoving, which stays false on a rope), playerAnimatorControl.Update uses the player's
    /// measured forward velocity instead of the stick, which is 0 on a rope: vSpeed stayed 0, so the slide almost never
    /// started. Here the stick drives it on a rope, as in flat-screen. Only the flag read by that one Update is changed,
    /// and restored right after.
    /// </summary>
    [HarmonyPatch(typeof(playerAnimatorControl), "Update")]
    internal static class RopeSlidePatch
    {
        private static void Prefix(playerAnimatorControl __instance, out bool __state)
        {
            __state = false;
            var vr = LocalPlayer.vrPlayerControl;
            if (!ForestVR.Enabled || !vr || __instance != LocalPlayer.AnimControl || !__instance.onRope || vr._startedMoving) return;
            vr._startedMoving = true;
            __state = true;
        }

        private static void Postfix(bool __state)
        {
            if (__state && LocalPlayer.vrPlayerControl) LocalPlayer.vrPlayerControl._startedMoving = false;
        }
    }
}
