using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace Tweaks
{
    /// <summary>
    /// Sliding down a rope costs stamina twice in vanilla:
    ///  - sprint (or the stick fully down with VR auto-run) doubles the rope speed and drains stamina at the sprint
    ///    rate, up or down; the faster rope speed doesn't depend on stamina;
    ///  - the climbing animations drain stamina through animation events (animEventsManager.staminaDrain).
    /// Here both cost only <see cref="CostRatio"/> while moving down a rope; climbing up is unchanged.
    /// </summary>
    [HarmonyPatch] // class-level attribute required, or the class processor skips the method patches
    internal static class RopeSlideStaminaPatch
    {
        public static float CostRatio;

        // Vertical speed on the rope, from the player position (the animator speed "verticalSpeed" stays 0 in VR).
        private static readonly AccessTools.FieldRef<playerAnimatorControl, float> YVel =
            AccessTools.FieldRefAccess<playerAnimatorControl, float>("yVel");

        private static float _nextLog;

        /// <summary>On a rope (not a cliff), moving down; logs the rope state once a second with VerboseLogging.</summary>
        private static bool SlidingDown(string what)
        {
            var anim = LocalPlayer.AnimControl;
            if (!LocalPlayer.Stats || !anim || !anim.onRope || anim.cliffClimb) return false;
            float v = YVel(anim);
            if (Plugin.Verbose && Time.time >= _nextLog)
            {
                _nextLog = Time.time + 1f;
                Plugin.Debug($"rope: {what}; vertical speed {v:0.0} ({(v < -1f ? "down" : v > 1f ? "up" : "still")}), " +
                             $"stamina {LocalPlayer.Stats.Stamina:0.0}, run={LocalPlayer.FpCharacter?.run}");
            }
            return v < -1f;
        }

        // ------------------------------------------------------------------ sprint drain

        [HarmonyPatch(typeof(FirstPersonCharacter), "HandleRunningStaminaAndSpeed")]
        [HarmonyPrefix]
        private static void RunPrefix(FirstPersonCharacter __instance, out float __state)
        {
            __state = float.NaN;
            if (__instance == LocalPlayer.FpCharacter && SlidingDown("sprint drain")) __state = LocalPlayer.Stats.Stamina;
        }

        [HarmonyPatch(typeof(FirstPersonCharacter), "HandleRunningStaminaAndSpeed")]
        [HarmonyPostfix]
        private static void RunPostfix(float __state)
        {
            if (float.IsNaN(__state)) return;
            float spent = __state - LocalPlayer.Stats.Stamina;
            if (spent > 0f) LocalPlayer.Stats.Stamina = __state - spent * CostRatio;
        }

        // ------------------------------------------------------------------ diagnostics ([Debug] VerboseLogging)

        private static readonly AccessTools.FieldRef<playerAnimatorControl, float> AnimVerticalSpeed =
            AccessTools.FieldRefAccess<playerAnimatorControl, float>("verticalSpeed");

        private static float _nextStateLog;

        /// <summary>
        /// While on a rope, log what drives the climbing animation (twice a second): the fast slide is chosen by the
        /// animator, from these values. In flat-screen it starts with sprint + down; in VR it rarely does.
        /// </summary>
        [HarmonyPatch(typeof(playerAnimatorControl), "Update")]
        [HarmonyPostfix]
        private static void AnimUpdatePostfix(playerAnimatorControl __instance)
        {
            if (!Plugin.Verbose || __instance != LocalPlayer.AnimControl || !__instance.onRope || Time.time < _nextStateLog) return;
            _nextStateLog = Time.time + 0.5f;
            var animator = LocalPlayer.Animator;
            var fpc = LocalPlayer.FpCharacter;
            object startedMoving = null;
            try
            {
                var vr = Traverse.Create(typeof(LocalPlayer)).Field("vrPlayerControl");
                if (!vr.FieldExists()) vr = Traverse.Create(typeof(LocalPlayer)).Property("vrPlayerControl");
                var control = vr.GetValue();
                if (control != null) startedMoving = Traverse.Create(control).Field("_startedMoving").GetValue();
            }
            catch { }
            Plugin.Debug($"rope state: animator vSpeed={(animator ? animator.GetFloat("vSpeed") : float.NaN):0.00}" +
                         $" climbSpeed={(animator ? animator.GetFloat("climbSpeed") : float.NaN):0.00}" +
                         $" verticalSpeed={AnimVerticalSpeed(__instance):0.00} input={(fpc ? fpc.inputVelocity : Vector3.zero)}" +
                         $" run={(fpc ? fpc.run : false)} startedMoving={startedMoving ?? "n/a"} yVel={YVel(__instance):0.0}" +
                         $" stamina={(LocalPlayer.Stats ? LocalPlayer.Stats.Stamina : -1f):0.0}");
        }

        // ------------------------------------------------------------------ animation-event drain

        [HarmonyPatch(typeof(animEventsManager), "staminaDrain")]
        [HarmonyPrefix]
        private static void AnimDrainPrefix(animEventsManager __instance, ref float amount)
        {
            if (__instance.Remote) return;
            var anim = LocalPlayer.AnimControl;
            if (!anim || !anim.onRope) return;
            bool down = SlidingDown("animation drain");
            Plugin.Debug($"rope: animation drains {amount:0.0} stamina ({(down ? $"moving down, x{CostRatio}" : "not moving down, unchanged")})");
            if (down) amount *= CostRatio;
        }
    }
}
