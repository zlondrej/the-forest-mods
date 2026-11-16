using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VrFixes
{
    /// <summary>Fixes for The Forest VR (TheForestVR.exe). Every patch checks ForestVR.Enabled, so flat-screen is unaffected.</summary>
    [BepInPlugin(Guid, "VR Fixes", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "forestmods.vrfixes";

        internal static ManualLogSource Log;
        internal static bool Verbose;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(Guid);
            Verbose = Config.Bind("Debug", "VerboseLogging", false,
                "Write detailed [debug] lines to BepInEx/LogOutput.log.").Value;

            Apply(harmony, "AutoRunStaminaRegen",
                "VR auto-run: once out of stamina, stamina regenerates while you keep walking with the stick fully forward (vanilla only regenerates after you ease off the stick).",
                typeof(AutoRunStaminaPatch));

            _airHud = Config.Bind("Fixes", "UnderwaterAirBar", true,
                "Show the underwater air bar in VR, in front of the view (vanilla VR never shows it).").Value;
            AirHud.Distance = Config.Bind("UnderwaterAirBar", "Distance", AirHud.Distance, "Distance from the eyes, in metres.").Value;
            AirHud.Height = Config.Bind("UnderwaterAirBar", "Height", AirHud.Height, "Height relative to the centre of the view, in metres (negative = below).").Value;
            AirHud.Size = Config.Bind("UnderwaterAirBar", "Size", AirHud.Size, "Size of one HUD pixel, in metres.").Value;
            Log.LogInfo(_airHud ? "UnderwaterAirBar: applied" : "UnderwaterAirBar: disabled in config");
        }

        private bool _airHud;
        private float _nextTick;

        private void Update()
        {
            if (!_airHud || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            try { AirHud.Tick(); }
            catch (Exception e) { Log.LogError($"UnderwaterAirBar: {e}"); _airHud = false; }
        }

        internal static void Debug(string message)
        {
            if (Verbose) Log.LogInfo("[debug] " + message);
        }

        private bool Apply(Harmony harmony, string key, string description, params Type[] patchClasses)
        {
            if (!Config.Bind("Fixes", key, true, description).Value)
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
                Log.LogError($"{key}: failed to apply, fix is inactive. {e}");
                return false;
            }
        }
    }
}
