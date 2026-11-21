using System;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace MeatBadges
{
    /// <summary>
    /// Meat and small meat told apart in the HUD's item prompts (fire, drying rack, stew pot, door weights): an "L" or
    /// "S" badge on the meat icon. Flat-screen and VR.
    /// </summary>
    [BepInPlugin(Guid, "Meat Badges", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "forestmods.meatbadges";

        internal static ManualLogSource Log;

        private bool _enabled = true;
        private float _nextTick;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("MeatIcons: applied");
        }

        private void Update()
        {
            if (!_enabled || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            try { MeatIcons.Tick(); }
            catch (Exception e)
            {
                Log.LogError($"MeatIcons: {e}");
                _enabled = false;
            }
        }
    }
}
