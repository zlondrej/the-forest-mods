using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
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
            BindSettings();
            ApplySettings();

            // Essential fixes, always on.
            Apply(harmony, "AutoRunStaminaRegen", typeof(AutoRunStaminaPatch));
            Apply(harmony, "RopeSlide", typeof(RopeSlidePatch));
            Apply(harmony, "ThrowOnRelease", typeof(ThrowOnReleasePatch));
            Apply(harmony, "InventoryButtonOpensMenu", typeof(MenuButtonPatch));
            Apply(harmony, "SprintThreshold", typeof(SprintThresholdPatch));
            ApplyVrOnly("VrKeyboard", () => VrKeyboardPatch.Apply(harmony));
            ApplyVrOnly("Handedness", () => HandednessPatch.Apply(harmony));
            ApplyVrOnly("BuildingRotation", () => BuildingRotationPatch.Apply(harmony));

            Log.LogInfo(_airHud ? "UnderwaterAirBar: applied" : "UnderwaterAirBar: disabled in config");

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

            Log.LogInfo("Settings:\n" + string.Join("\n", Settings().ToArray()));
            _configTime = ConfigTime();
        }

        private bool _immersiveCamera;
        private bool _airHud;

        // The settings, bound once; ApplySettings copies them to where they're used (at start and when the file changes).
        private ConfigEntry<bool> _bodyVisible, _airHudEnabled, _airHudBlurred, _onlyWhileRotateHeld, _followView;
        private ConfigEntry<float> _bodyBack, _sprintThreshold, _twistRatio;
        private ConfigEntry<float> _airDistance, _airHeight, _airSize;
        private readonly Dictionary<ImmersiveCamera.Case, ConfigEntry<bool>> _immersive = new Dictionary<ImmersiveCamera.Case, ConfigEntry<bool>>();
        private ConfigEntry<float> _rotationSmoothing, _positionSmoothing, _rotationSpeedup, _positionSpeedup, _maxPositionLag, _maxRotationLag;

        private void BindSettings()
        {
            _bodyVisible = Config.Bind("PlayerBody", "Visible", PlayerBody.Visible,
                "Draw your body in VR (torso and legs, clothes and armour; not the head, nor the bare arms: the VR hands are drawn instead). Vanilla VR only draws its shadow.");
            _bodyBack = Config.Bind("PlayerBody", "BackOffset", PlayerBody.BackOffset,
                "Metres your body sits further back behind your eyes than in vanilla, so the chest isn't in front of them when you look down (negative = forward, 0 = vanilla).");

            _sprintThreshold = Config.Bind("Movement", "SprintThreshold", SprintThresholdPatch.Vanilla,
                "VR auto-run: you sprint while the movement stick is pushed past this part of its range (0.85 = vanilla). Lower: sprint without pushing all the way (sticks that don't reach the edge, especially diagonally). Higher: walk with the stick further out.");

            _twistRatio = Config.Bind("BuildingRotation", "HandTwistRatio", BuildingRotationPatch.Ratio,
                "Placing a building in VR: twisting the hand that aims (around where it points, like turning a knob) turns the building by this many degrees per degree of twist (2: a quarter turn of the hand turns it half around). Negative = the other way, 0 = off.");
            _onlyWhileRotateHeld = Config.Bind("BuildingRotation", "OnlyWhileRotateHeld", BuildingRotationPatch.OnlyWhileRotateHeld,
                "true = the hand turns the building only while you hold the rotate button (a TILT sign shows at the hand); false = always, on top of the rotate input.");
            _followView = Config.Bind("BuildingRotation", "FacesYou", BuildingRotationPatch.FacesYou,
                "true = the building keeps facing you the same way as you turn (or aim around you), as it does in flat-screen when you turn the view; false = it keeps its compass direction (vanilla VR).");

            _airHudEnabled = Config.Bind("UnderwaterAirBar", "Enabled", true,
                "Show the underwater air bar in VR, in front of the view (vanilla VR never shows it). Takes effect when the game starts.");
            _airDistance = Config.Bind("UnderwaterAirBar", "Distance", AirHud.Distance, "Distance from the eyes, in metres.");
            _airHeight = Config.Bind("UnderwaterAirBar", "Height", AirHud.Height, "Height relative to the centre of the view, in metres (negative = below).");
            _airSize = Config.Bind("UnderwaterAirBar", "Size", AirHud.Size, "Size of one HUD pixel, in metres.");
            _airHudBlurred = Config.Bind("UnderwaterAirBar", "Blurred", AirHud.Blurred, "Underwater blur also covers the air bar (true), or the bar stays sharp (false).");

            BindImmersiveCamera();
        }

        /// <summary>
        /// [ImmersiveCamera]: per scene, true = first person (the view pinned to the character's head, the headset only
        /// rotates it), false = theatre mode (vanilla); and the smoothing of the pinned view.
        /// </summary>
        private void BindImmersiveCamera()
        {
            void Bind(ImmersiveCamera.Case c, string when) =>
                _immersive[c] = Config.Bind("ImmersiveCamera", c.ToString(), true,
                    $"{when}: true = first person, seen through the character's eyes; false = theatre mode (the scene on a screen, as in vanilla VR).");

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

            _rotationSmoothing = Config.Bind("ImmersiveCamera", "RotationSmoothing", HeadPin.RotationSmoothing,
                "Seconds the pinned view takes to follow the character's head rotation (smooths its small animated movements); 0 = no smoothing.");
            _positionSmoothing = Config.Bind("ImmersiveCamera", "PositionSmoothing", HeadPin.PositionSmoothing,
                "Seconds the pinned view takes to follow the character's head position (relative to the body); 0 = no smoothing.");
            _rotationSpeedup = Config.Bind("ImmersiveCamera", "RotationSmoothingSpeedup", HeadPin.RotationSmoothingSpeedup,
                "How much less the rotation is smoothed when the character's head turns fast (added follow rate in Hz per degree/s); 0 = the same smoothing at any speed.");
            _positionSpeedup = Config.Bind("ImmersiveCamera", "PositionSmoothingSpeedup", HeadPin.PositionSmoothingSpeedup,
                "How much less the position is smoothed when the character's head moves fast (added follow rate in Hz per m/s); 0 = the same smoothing at any speed.");
            _maxPositionLag = Config.Bind("ImmersiveCamera", "MaxPositionLag", HeadPin.MaxPositionLag,
                "The smoothed view never trails the character's head by more than this many metres, so large movements don't drag behind (small sways are smaller and still smoothed); 0 = no limit.");
            _maxRotationLag = Config.Bind("ImmersiveCamera", "MaxRotationLag", HeadPin.MaxRotationLag,
                "The smoothed view never trails the character's head rotation by more than this many degrees; 0 = no limit.");
        }

        /// <summary>The settings into the fixes. All take effect right away, except where a description says otherwise.</summary>
        private void ApplySettings()
        {
            PlayerBody.Visible = _bodyVisible.Value;
            PlayerBody.BackOffset = _bodyBack.Value;
            SprintThresholdPatch.Threshold = Mathf.Clamp(_sprintThreshold.Value, 0.1f, 1f);
            BuildingRotationPatch.Ratio = _twistRatio.Value;
            BuildingRotationPatch.OnlyWhileRotateHeld = _onlyWhileRotateHeld.Value;
            BuildingRotationPatch.FacesYou = _followView.Value;
            if (_configTime == default(DateTime)) _airHud = _airHudEnabled.Value; // at start only
            AirHud.Distance = _airDistance.Value;
            AirHud.Height = _airHeight.Value;
            AirHud.Size = _airSize.Value;
            AirHud.Blurred = _airHudBlurred.Value;
            foreach (var pair in _immersive) ImmersiveCamera.Immersive[pair.Key] = pair.Value.Value;
            HeadPin.RotationSmoothing = Mathf.Max(0f, _rotationSmoothing.Value);
            HeadPin.PositionSmoothing = Mathf.Max(0f, _positionSmoothing.Value);
            HeadPin.RotationSmoothingSpeedup = Mathf.Max(0f, _rotationSpeedup.Value);
            HeadPin.PositionSmoothingSpeedup = Mathf.Max(0f, _positionSpeedup.Value);
            HeadPin.MaxPositionLag = Mathf.Max(0f, _maxPositionLag.Value);
            HeadPin.MaxRotationLag = Mathf.Max(0f, _maxRotationLag.Value);
        }

        /// <summary>Every setting and its value, for bug reports.</summary>
        private IEnumerable<string> Settings() =>
            Config.OrderBy(p => p.Key.Section).ThenBy(p => p.Key.Key)
                .Select(p => $"  [{p.Key.Section}] {p.Key.Key} = {p.Value.BoxedValue}");

        // Hot reload: the config file is checked every second; when it was saved, it is read again.
        private DateTime _configTime;
        private float _nextConfigCheck;

        private DateTime ConfigTime()
        {
            try { return File.GetLastWriteTimeUtc(Config.ConfigFilePath); }
            catch { return _configTime; }
        }

        private void CheckConfigFile()
        {
            if (Time.unscaledTime < _nextConfigCheck) return;
            _nextConfigCheck = Time.unscaledTime + 1f;
            var time = ConfigTime();
            if (time == _configTime) return;
            _configTime = time;
            try
            {
                var before = Settings().ToList();
                Config.Reload();
                ApplySettings();
                var changed = Settings().Where(s => !before.Contains(s)).ToArray();
                Log.LogInfo(changed.Length == 0
                    ? "Settings file saved, nothing changed"
                    : "Settings changed:\n" + string.Join("\n", changed));
            }
            catch (Exception e) { Log.LogError($"Settings: could not reload the config file. {e.Message}"); }
        }

        private float _nextTick;

        private void Update()
        {
            CheckConfigFile();
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
            if (_playerBody)
            {
                try { PlayerBody.UpdateOffset(); }
                catch (Exception e) { Log.LogError($"PlayerBody: {e}"); _playerBody = false; }
            }
            try { BuildingRotationPatch.UpdateSign(); }
            catch (Exception e) { Log.LogError($"BuildingRotation: {e}"); }
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
                catch (Exception e) { Log.LogError($"MeatIcons: {e}"); _meatIcons = false; }
            }
            if (_playerBody)
            {
                try { PlayerBody.Tick(); }
                catch (Exception e) { Log.LogError($"PlayerBody: {e}"); _playerBody = false; }
            }
        }

        private bool _meatIcons = true, _playerBody = true;

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

        /// <summary>A fix patched by hand, for methods that exist only in the VR build.</summary>
        private static void ApplyVrOnly(string key, Func<bool> apply)
        {
            try { Log.LogInfo(apply() ? $"{key}: applied" : $"{key}: not applied (VR build only)"); }
            catch (Exception e) { Log.LogError($"{key}: failed to apply. {e}"); }
        }
    }
}
