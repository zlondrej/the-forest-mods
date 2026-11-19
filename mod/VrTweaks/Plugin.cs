using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>Fixes for The Forest VR (TheForestVR.exe). Every patch checks ForestVR.Enabled, so flat-screen is unaffected.</summary>
    [BepInPlugin(Guid, "VR Tweaks", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "forestmods.vrtweaks";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(Guid);

            // Essential fixes, always on.
            Apply(harmony, "AutoRunStaminaRegen", typeof(AutoRunStaminaPatch));
            Apply(harmony, "RopeSlide", typeof(RopeSlidePatch));
            Apply(harmony, "ThrowOnRelease", typeof(ThrowOnReleasePatch));
            Apply(harmony, "InventoryButtonOpensMenu", typeof(MenuButtonPatch));

            SprintThresholdPatch.Threshold = Mathf.Clamp(Config.Bind("Movement", "SprintThreshold", SprintThresholdPatch.Vanilla,
                "VR auto-run: you sprint while the movement stick is pushed past this part of its range (0.85 = vanilla). Lower: sprint without pushing all the way (sticks that don't reach the edge, especially diagonally). Higher: walk with the stick further out.").Value, 0.1f, 1f);
            Apply(harmony, "SprintThreshold", typeof(SprintThresholdPatch));

            BuildingRotationPatch.Ratio = Config.Bind("BuildingRotation", "HandTwistRatio", BuildingRotationPatch.Ratio,
                "Placing a building in VR: twisting the hand that aims (around where it points, like turning a knob) turns the building by this many degrees per degree of twist (2: a quarter turn of the hand turns it half around). Negative = the other way, 0 = off.").Value;
            BuildingRotationPatch.OnlyWhileRotateHeld = Config.Bind("BuildingRotation", "OnlyWhileRotateHeld", BuildingRotationPatch.OnlyWhileRotateHeld,
                "true = the hand turns the building only while you hold the rotate button; false = always, on top of the rotate input.").Value;
            try
            {
                Log.LogInfo(BuildingRotationPatch.Apply(harmony)
                    ? "BuildingRotation: applied"
                    : "BuildingRotation: not applied (VR build only)");
            }
            catch (Exception e) { Log.LogError($"BuildingRotation: failed to apply. {e}"); }

            _airHud = Config.Bind("UnderwaterAirBar", "Enabled", true,
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
        /// [ImmersiveCamera]: per scene, true = first person (the view pinned to the character's head, the headset only
        /// rotates it), false = theatre mode (vanilla); and the smoothing of the pinned view.
        /// </summary>
        private void BindImmersiveCamera()
        {
            void Bind(ImmersiveCamera.Case c, string when) =>
                ImmersiveCamera.Immersive[c] = Config.Bind("ImmersiveCamera", c.ToString(), true,
                    $"{when}: true = first person, seen through the character's eyes; false = theatre mode (the scene on a screen, as in vanilla VR).").Value;

            Bind(ImmersiveCamera.Case.Caves, "Crawling into or out of a cave entrance");
            Bind(ImmersiveCamera.Case.Climbing, "Getting onto and off a rope or climbing wall (on it, you look around and lean freely, at the character's height)");
            Bind(ImmersiveCamera.Case.Drinking, "Drinking from a lake, river or other water");
            Bind(ImmersiveCamera.Case.Raft, "Getting on and riding a raft");
            Bind(ImmersiveCamera.Case.Skinning, "Skinning an animal");
            Bind(ImmersiveCamera.Case.KeypadDoors, "Opening a keycard/keypad door");
            Bind(ImmersiveCamera.Case.Artifact, "Placing the artifact");
            Bind(ImmersiveCamera.Case.StoryScenes,
                "Story scenes (opening plane crash, being dragged away, waking up, Timmy, the girl, the endings)");
            Bind(ImmersiveCamera.Case.HangingInCave, "Waking up hanging upside down in the cave");
            Bind(ImmersiveCamera.Case.Death, "Dying (falling, drowning, killed)");

            float NonNegative(string key, float value, string description) =>
                Mathf.Max(0f, Config.Bind("ImmersiveCamera", key, value, description).Value);
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
            if (_airHud)
            {
                try { AirHud.UpdateCamera(); }
                catch (Exception e) { Log.LogError($"UnderwaterAirBar: {e}"); _airHud = false; }
            }
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            if (_airHud)
            {
                try { AirHud.Tick(); }
                catch (Exception e) { Log.LogError($"UnderwaterAirBar: {e}"); _airHud = false; }
            }
            if (_meatIcons)
            {
                try { MeatIcons.Tick(); }
                catch (Exception e) { Log.LogError($"SmallMeatIcon: {e}"); _meatIcons = false; }
            }
        }

        private bool _meatIcons = true;

        private static bool Apply(Harmony harmony, string key, params Type[] patchClasses)
        {
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
