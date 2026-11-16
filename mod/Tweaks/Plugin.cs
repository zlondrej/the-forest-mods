using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Tweaks
{
    /// <summary>Gameplay tweaks for The Forest, flat-screen and VR.</summary>
    [BepInPlugin(Guid, "Tweaks", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "forestmods.tweaks";

        internal static ManualLogSource Log;
        internal static bool Verbose;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(Guid);
            Verbose = Config.Bind("Debug", "VerboseLogging", false,
                "Write detailed [debug] lines to BepInEx/LogOutput.log.").Value;

            RopeSlideStaminaPatch.CostRatio = Config.Bind("RopeSlide", "StaminaCost", 0f,
                "Share of the normal sprint stamina cost that sliding down a rope fast costs (0 = free, 1 = vanilla).").Value;
            Apply(harmony, "RopeSlideStamina",
                "Sliding down a rope fast (sprint, or the stick fully down with VR auto-run) costs no stamina, or only the share set in [RopeSlide] StaminaCost. Climbing up is unchanged.",
                typeof(RopeSlideStaminaPatch));
        }

        internal static void Debug(string message)
        {
            if (Verbose) Log.LogInfo("[debug] " + message);
        }

        private bool Apply(Harmony harmony, string key, string description, params Type[] patchClasses)
        {
            if (!Config.Bind("Tweaks", key, true, description).Value)
            {
                Log.LogInfo($"{key}: disabled in config");
                return false;
            }
            try
            {
                foreach (var patchClass in patchClasses)
                {
                    // A class that patches nothing is an error (HarmonyX silently skips classes without a class-level
                    // [HarmonyPatch] attribute).
                    var patched = harmony.CreateClassProcessor(patchClass).Patch();
                    if (patched == null || patched.Count == 0)
                        throw new InvalidOperationException($"{patchClass.Name} patched no methods");
                }
                Log.LogInfo($"{key}: applied");
                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"{key}: failed to apply, tweak is inactive. {e}");
                return false;
            }
        }
    }
}
