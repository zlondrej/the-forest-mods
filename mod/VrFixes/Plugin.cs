using System;
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

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(Guid);

            Apply(harmony, "AutoRunStaminaRegen",
                "VR auto-run: once out of stamina, stamina regenerates while you keep walking with the stick fully forward (vanilla only regenerates after you ease off the stick).",
                typeof(AutoRunStaminaPatch));

            Apply(harmony, "RopeSlide",
                "VR: on a rope, sprint + stick down starts the fast slide, as in flat-screen (vanilla VR ignored the stick for the climbing animation, so the slide almost never started).",
                typeof(RopeSlidePatch));

            Apply(harmony, "ThrowOnRelease",
                "VR throwing (spear, molotov, bombs, rocks, balls) only throws when you release the throw trigger; vanilla also threw when you pressed it while your hand was moving, often backwards.",
                typeof(ThrowOnReleasePatch));

            Apply(harmony, "InventoryButtonOpensMenu",
                "VR: a short press of the inventory button opens the pause menu where the inventory can't open (during animations), and underwater and while hanging in the cave (instead of the inventory), as Esc does in flat-screen; vanilla VR only reaches the menu through the inventory.",
                typeof(MenuButtonPatch));

            _airHud = Config.Bind("Fixes", "UnderwaterAirBar", true,
                "Show the underwater air bar in VR, in front of the view (vanilla VR never shows it).").Value;
            AirHud.Distance = Config.Bind("UnderwaterAirBar", "Distance", AirHud.Distance, "Distance from the eyes, in metres.").Value;
            AirHud.Height = Config.Bind("UnderwaterAirBar", "Height", AirHud.Height, "Height relative to the centre of the view, in metres (negative = below).").Value;
            AirHud.Size = Config.Bind("UnderwaterAirBar", "Size", AirHud.Size, "Size of one HUD pixel, in metres.").Value;
            AirHud.Blurred = Config.Bind("UnderwaterAirBar", "Blurred", AirHud.Blurred, "Underwater blur also covers the air bar (true), or the bar stays sharp (false).").Value;
            Log.LogInfo(_airHud ? "UnderwaterAirBar: applied" : "UnderwaterAirBar: disabled in config");

            BindImmersiveCamera();
            if (ImmersiveCamera.Immersive.Values.Any(v => v))
            {
                try
                {
                    ImmersiveCamera.Apply(harmony);
                    _immersiveCamera = true;
                    Log.LogInfo("ImmersiveCamera: applied");
                }
                catch (Exception e) { Log.LogError($"ImmersiveCamera: failed to apply, theatre mode stays vanilla. {e}"); }
            }
            else Log.LogInfo("ImmersiveCamera: not applied (every case set to theatre)");

            // Every setting and its value, for bug reports.
            var settings = Config.OrderBy(p => p.Key.Section).ThenBy(p => p.Key.Key)
                .Select(p => $"  [{p.Key.Section}] {p.Key.Key} = {p.Value.BoxedValue}");
            Log.LogInfo("Settings:\n" + string.Join("\n", settings.ToArray()));
        }

        private bool _immersiveCamera;

        /// <summary>
        /// [ImmersiveCamera]: per case, true = first person, false = theatre mode (vanilla).
        /// [ImmersiveCameraRotationOnly]: per case, for first person: true = the view is pinned to the character's head and
        /// the headset only rotates it, false = normal headset tracking.
        /// </summary>
        private void BindImmersiveCamera()
        {
            void Bind(ImmersiveCamera.Case c, bool immersive, bool rotationOnly, string when)
            {
                ImmersiveCamera.Immersive[c] = Config.Bind("ImmersiveCamera", c.ToString(), immersive,
                    $"{when}: true = first person (immersive), false = theatre mode (the scene on a screen, as in vanilla VR).").Value;
                ImmersiveCamera.RotationOnly[c] = Config.Bind("ImmersiveCameraRotationOnly", c.ToString(), rotationOnly,
                    $"{when}, with [ImmersiveCamera] {c} = true: true = the view is pinned to the character's head and the headset " +
                    "only rotates it (no positional tracking), false = normal headset tracking.").Value;
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

            BodyView.Enabled = Config.Bind("ImmersiveCamera", "ShowBody", true,
                "In first-person scenes: show your body (not the head) and its animated arms instead of the VR hands, as the theatre does.").Value;
            ImmersiveCamera.AlignViewAtStart = Config.Bind("ImmersiveCamera", "AlignViewAtStart", ImmersiveCamera.AlignViewAtStart,
                "First-person scenes with normal headset tracking: when the scene starts, turn and move your view to where the character looks and stands (your height stays).").Value;

            float NonNegative(string key, float value, string description) =>
                Mathf.Max(0f, Config.Bind("ImmersiveCameraRotationOnly", key, value, description).Value);
            HeadPin.RotationSmoothing = NonNegative("RotationSmoothing", HeadPin.RotationSmoothing,
                "Seconds the pinned view takes to follow the character's head rotation (smooths its small animated movements); 0 = no smoothing.");
            HeadPin.PositionSmoothing = NonNegative("PositionSmoothing", HeadPin.PositionSmoothing,
                "Seconds the pinned view takes to follow the character's head position (relative to the body); 0 = no smoothing.");
            HeadPin.RotationSmoothingSpeedup = NonNegative("RotationSmoothingSpeedup", HeadPin.RotationSmoothingSpeedup,
                "How much less the rotation is smoothed when the character's head turns fast (added follow rate in Hz per degree/s); 0 = the same smoothing at any speed.");
            HeadPin.PositionSmoothingSpeedup = NonNegative("PositionSmoothingSpeedup", HeadPin.PositionSmoothingSpeedup,
                "How much less the position is smoothed when the character's head moves fast (added follow rate in Hz per m/s); 0 = the same smoothing at any speed.");
            HeadPin.MaxPositionLag = NonNegative("MaxPositionLag", HeadPin.MaxPositionLag,
                "The smoothed view never trails the character's head by more than this many metres, so large movements don't drag behind (small sways are smaller and still smoothed); 0 = no limit.");
            HeadPin.MaxRotationLag = NonNegative("MaxRotationLag", HeadPin.MaxRotationLag,
                "The smoothed view never trails the character's head rotation by more than this many degrees; 0 = no limit.");
            ImmersiveCamera.ClimbingFreeOnRope = Config.Bind("ImmersiveCameraRotationOnly", "ClimbingFreeOnRope", ImmersiveCamera.ClimbingFreeOnRope,
                "With Climbing = true here: the view is pinned only while getting onto and off the rope or wall; while climbing, the headset moves the view horizontally and turns it, its height stays at the character's head. false = pinned the whole climb.").Value;
        }

        private bool _airHud;
        private float _nextTick;

        private void Update()
        {
            if (_immersiveCamera)
            {
                try { ImmersiveCamera.CheckPlayerGone(); }
                catch (Exception e) { Log.LogError($"ImmersiveCamera: {e}"); }
            }
            if (!_airHud) return;
            try { AirHud.UpdateCamera(); }
            catch (Exception e) { Log.LogError($"UnderwaterAirBar: {e}"); _airHud = false; }
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            try { AirHud.Tick(); }
            catch (Exception e) { Log.LogError($"UnderwaterAirBar: {e}"); _airHud = false; }
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
