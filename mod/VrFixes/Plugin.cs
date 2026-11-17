using System;
using System.Collections.Generic;
using System.Linq;
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
                "Write detailed [debug] lines to BepInEx/LogOutput.log (also logs VR drop and throw attempts, and the rope climbing state).").Value;
            if (Verbose)
            {
                try { DebugPatches.Apply(harmony); Log.LogInfo("VerboseLogging: drop/throw logging applied"); }
                catch (Exception e) { Log.LogError($"VerboseLogging: drop/throw logging failed. {e}"); }
            }

            Apply(harmony, "AutoRunStaminaRegen",
                "VR auto-run: once out of stamina, stamina regenerates while you keep walking with the stick fully forward (vanilla only regenerates after you ease off the stick).",
                typeof(AutoRunStaminaPatch));

            Apply(harmony, "RopeSlide",
                "VR: on a rope, sprint + stick down starts the fast slide, as in flat-screen (vanilla VR ignored the stick for the climbing animation, so the slide almost never started).",
                typeof(RopeSlidePatch));

            Apply(harmony, "ThrowOnRelease",
                "VR throwing (spear, molotov, bombs, rocks, balls) only throws when you release the throw trigger; vanilla also threw when you pressed it while your hand was moving, often backwards.",
                typeof(ThrowOnReleasePatch));

            _airHud = Config.Bind("Fixes", "UnderwaterAirBar", true,
                "Show the underwater air bar in VR, in front of the view (vanilla VR never shows it).").Value;
            AirHud.Distance = Config.Bind("UnderwaterAirBar", "Distance", AirHud.Distance, "Distance from the eyes, in metres.").Value;
            AirHud.Height = Config.Bind("UnderwaterAirBar", "Height", AirHud.Height, "Height relative to the centre of the view, in metres (negative = below).").Value;
            AirHud.Size = Config.Bind("UnderwaterAirBar", "Size", AirHud.Size, "Size of one HUD pixel, in metres.").Value;
            AirHud.Blurred = Config.Bind("UnderwaterAirBar", "Blurred", AirHud.Blurred, "Underwater blur also covers the air bar (true), or the bar stays sharp (false).").Value;
            Log.LogInfo(_airHud ? "UnderwaterAirBar: applied" : "UnderwaterAirBar: disabled in config");

            _harmony = harmony;
            BindImmersiveCamera();
            ApplyImmersiveCameraSettings();
            if (!_immersiveCameraPatched) Log.LogInfo("ImmersiveCamera: not applied (every case set to theatre)");
            _configTime = ConfigTime();
        }

        private Harmony _harmony;
        private bool _immersiveCameraPatched;
        private BepInEx.Configuration.ConfigEntry<bool> _showBody;
        private readonly Dictionary<ImmersiveCamera.Case, BepInEx.Configuration.ConfigEntry<bool>> _immersiveEntries =
            new Dictionary<ImmersiveCamera.Case, BepInEx.Configuration.ConfigEntry<bool>>();
        private readonly Dictionary<ImmersiveCamera.Case, BepInEx.Configuration.ConfigEntry<bool>> _rotationOnlyEntries =
            new Dictionary<ImmersiveCamera.Case, BepInEx.Configuration.ConfigEntry<bool>>();

        /// <summary>Copy the [ImmersiveCamera*] settings in use; patch the first time any case is first person.</summary>
        private void ApplyImmersiveCameraSettings()
        {
            foreach (var pair in _immersiveEntries) ImmersiveCamera.Immersive[pair.Key] = pair.Value.Value;
            foreach (var pair in _rotationOnlyEntries) ImmersiveCamera.RotationOnly[pair.Key] = pair.Value.Value;
            BodyView.Enabled = _showBody == null || _showBody.Value;
            if (_alignView != null) ImmersiveCamera.AlignViewAtStart = _alignView.Value;
            if (_climbingFreeOnRope != null) ImmersiveCamera.ClimbingFreeOnRope = _climbingFreeOnRope.Value;
            if (_rotationSmoothing != null) HeadPin.RotationSmoothing = Mathf.Max(0f, _rotationSmoothing.Value);
            if (_positionSmoothing != null) HeadPin.PositionSmoothing = Mathf.Max(0f, _positionSmoothing.Value);
            if (_immersiveCameraPatched || !ImmersiveCamera.Immersive.Values.Any(v => v)) return;
            _immersiveCameraPatched = true; // also on failure: don't retry every reload
            try { ImmersiveCamera.Apply(_harmony); Log.LogInfo("ImmersiveCamera: applied"); }
            catch (Exception e) { Log.LogError($"ImmersiveCamera: failed to apply, theatre mode stays vanilla. {e}"); }
        }

        // Live reload of the [ImmersiveCamera*] settings: the config file is checked every 2 s while the game runs.
        private DateTime _configTime;
        private float _nextConfigCheck;

        private DateTime ConfigTime()
        {
            try { return System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath); }
            catch { return _configTime; }
        }

        private void CheckConfigFile()
        {
            if (Time.unscaledTime < _nextConfigCheck) return;
            _nextConfigCheck = Time.unscaledTime + 2f;
            var time = ConfigTime();
            if (time == _configTime) return;
            _configTime = time;
            try
            {
                Config.Reload();
                ApplyImmersiveCameraSettings();
                Log.LogInfo($"Config reloaded: ShowBody={BodyView.Enabled}, immersive camera " + string.Join(", ", ImmersiveCamera.Immersive
                    .Select(p => $"{p.Key}={(p.Value ? (ImmersiveCamera.RotationOnly.TryGetValue(p.Key, out var r) && r ? "first person, rotation only" : "first person") : "theatre")}")
                    .ToArray()));
            }
            catch (Exception e) { Log.LogError($"Config reload failed: {e}"); }
        }

        /// <summary>
        /// [ImmersiveCamera]: per case, true = first person, false = theatre mode (vanilla).
        /// [ImmersiveCameraRotationOnly]: per case, for first person: true = the view is pinned to the character's head and
        /// the headset only rotates it, false = normal headset tracking.
        /// </summary>
        private void BindImmersiveCamera()
        {
            void Bind(ImmersiveCamera.Case c, bool immersive, bool rotationOnly, string when)
            {
                _immersiveEntries[c] = Config.Bind("ImmersiveCamera", c.ToString(), immersive,
                    $"{when}: true = first person (immersive), false = theatre mode (the scene on a screen, as in vanilla VR). " +
                    "Changes apply while the game runs.");
                _rotationOnlyEntries[c] = Config.Bind("ImmersiveCameraRotationOnly", c.ToString(), rotationOnly,
                    $"{when}, with [ImmersiveCamera] {c} = true: true = the view is pinned to the character's head and the headset " +
                    "only rotates it (no positional tracking), false = normal headset tracking. Changes apply while the game runs.");
            }

            Bind(ImmersiveCamera.Case.Caves, true, true, "Crawling into or out of a cave entrance");
            Bind(ImmersiveCamera.Case.Climbing, true, true, "Getting onto a rope or climbing wall, from the bottom or the top");
            Bind(ImmersiveCamera.Case.Drinking, true, true, "Drinking from a lake, river or other water");
            Bind(ImmersiveCamera.Case.Raft, true, true, "Getting on and riding a raft");
            Bind(ImmersiveCamera.Case.Skinning, true, true, "Skinning an animal");
            Bind(ImmersiveCamera.Case.KeypadDoors, true, true, "Opening a keycard/keypad door");
            Bind(ImmersiveCamera.Case.Artifact, true, true, "Placing the artifact");
            Bind(ImmersiveCamera.Case.StoryScenes, true, true,
                "Story scenes (opening plane crash, being dragged away, waking up, Timmy, the girl, the endings); first person may break some");
            Bind(ImmersiveCamera.Case.HangingInCave, true, true, "Waking up hanging upside down in the cave");
            Bind(ImmersiveCamera.Case.Death, true, true, "Dying (falling, drowning, killed)");

            _showBody = Config.Bind("ImmersiveCamera", "ShowBody", true,
                "In first-person scenes: show your body (not the head) and its animated arms instead of the VR hands, as the theatre does. Changes apply while the game runs.");
            _alignView = Config.Bind("ImmersiveCamera", "AlignViewAtStart", true,
                "First-person scenes with normal headset tracking: when the scene starts, turn and move your view to where the character looks and stands (your height stays). Changes apply while the game runs.");
            _rotationSmoothing = Config.Bind("ImmersiveCameraRotationOnly", "RotationSmoothing", HeadPin.RotationSmoothing,
                "Seconds the pinned view takes to follow the character's head rotation (smooths its small animated movements); 0 = no smoothing. Changes apply while the game runs.");
            _positionSmoothing = Config.Bind("ImmersiveCameraRotationOnly", "PositionSmoothing", HeadPin.PositionSmoothing,
                "Seconds the pinned view takes to follow the character's head position; 0 = no smoothing (larger values lag behind, the view may dip into the body). Changes apply while the game runs.");
            _climbingFreeOnRope = Config.Bind("ImmersiveCameraRotationOnly", "ClimbingFreeOnRope", ImmersiveCamera.ClimbingFreeOnRope,
                "With Climbing = true here: the view is pinned only while getting onto and off the rope or wall; while climbing, normal headset tracking. false = pinned the whole climb. Changes apply while the game runs (from the next climb).");
        }

        private BepInEx.Configuration.ConfigEntry<bool> _alignView, _climbingFreeOnRope;
        private BepInEx.Configuration.ConfigEntry<float> _rotationSmoothing, _positionSmoothing;

        private bool _airHud;
        private float _nextTick;

        private void Update()
        {
            CheckConfigFile();
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
