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

            // One setting: at 1 (vanilla) nothing is patched, unless VerboseLogging wants the rope log lines.
            RopeSlideStaminaPatch.CostRatio = Config.Bind("RopeSlide", "StaminaCost", 0f,
                "Stamina cost of moving down a rope, as a share of vanilla: 0 = free, 0.5 = half, 1 = vanilla (the game is then left unpatched). Climbing up is unchanged.").Value;
            Apply(harmony, "RopeSlideStamina", RopeSlideStaminaPatch.CostRatio != 1f || Verbose,
                "StaminaCost is 1 (vanilla)", typeof(RopeSlideStaminaPatch));
        }

        internal static void Debug(string message)
        {
            if (Verbose) Log.LogInfo("[debug] " + message);
        }

        private bool Apply(Harmony harmony, string key, bool enabled, string whyNot, params Type[] patchClasses)
        {
            if (!enabled)
            {
                Log.LogInfo($"{key}: not applied ({whyNot})");
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
