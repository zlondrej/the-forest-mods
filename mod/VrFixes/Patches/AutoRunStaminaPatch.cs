using HarmonyLib;
using TheForest.Utils;

namespace VrFixes
{
    /// <summary>
    /// With VR auto-run, holding the stick past 85% is the sprint button. When stamina runs out, the game drops you to
    /// walking speed (CantRun) but keeps you "running" while the stick stays forward, and running restarts the
    /// run-recovery delay every frame, so stamina never regenerates until you ease off the stick. Here an exhausted
    /// player walking with the stick forward no longer counts as running. Sprinting again still needs the stick eased
    /// off and pushed again, as in vanilla.
    /// </summary>
    [HarmonyPatch(typeof(FirstPersonCharacter), "Update")]
    internal static class AutoRunStaminaPatch
    {
        private static bool _logged;

        private static void Prefix(FirstPersonCharacter __instance, out float __state)
        {
            __state = __instance.recoveringFromRun;
        }

        private static void Postfix(FirstPersonCharacter __instance, float __state)
        {
            if (!ForestVR.Enabled || !PlayerPreferences.VRAutoRun || __instance != LocalPlayer.FpCharacter) return;
            if (!__instance.CantRun || !__instance.running)
            {
                _logged = false;
                return;
            }
            __instance.running = false;
            __instance.recoveringFromRun = __state; // undo the restart of the recovery delay
            if (!_logged)
            {
                _logged = true;
                Plugin.Debug($"auto-run: out of stamina ({LocalPlayer.Stats?.Stamina:0.0}), walking on with the stick forward; stamina can regenerate");
            }
        }
    }
}
