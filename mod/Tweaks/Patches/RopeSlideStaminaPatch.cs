using HarmonyLib;
using TheForest.Utils;

namespace Tweaks
{
    /// <summary>
    /// On a rope, sprint doubles the climbing speed, and the game drains stamina at the sprint rate while it is held,
    /// whether you go up or down (with VR auto-run, pushing the stick fully down is sprint). The faster rope speed
    /// doesn't depend on stamina, so here sliding down only costs <see cref="CostRatio"/> of it; climbing up is
    /// unchanged.
    /// </summary>
    [HarmonyPatch(typeof(FirstPersonCharacter), "HandleRunningStaminaAndSpeed")]
    internal static class RopeSlideStaminaPatch
    {
        public static float CostRatio;

        private static readonly AccessTools.FieldRef<playerAnimatorControl, float> VerticalSpeed =
            AccessTools.FieldRefAccess<playerAnimatorControl, float>("verticalSpeed");

        private static bool _logged;

        private static void Prefix(FirstPersonCharacter __instance, out float __state)
        {
            __state = float.NaN;
            var anim = LocalPlayer.AnimControl;
            if (__instance != LocalPlayer.FpCharacter || !LocalPlayer.Stats || !anim || !anim.onRope || anim.cliffClimb
                || VerticalSpeed(anim) >= -0.01f)
            {
                _logged = false;
                return;
            }
            __state = LocalPlayer.Stats.Stamina;
        }

        private static void Postfix(float __state)
        {
            if (float.IsNaN(__state)) return;
            float spent = __state - LocalPlayer.Stats.Stamina;
            if (spent <= 0f) return;
            LocalPlayer.Stats.Stamina = __state - spent * CostRatio;
            if (!_logged)
            {
                _logged = true;
                Plugin.Debug($"rope: sliding down fast, stamina cost x{CostRatio} ({__state:0.0} stamina)");
            }
        }
    }
}
