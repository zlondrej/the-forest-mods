using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace DevCheats
{
    /// <summary>
    /// For testing the mods, not shipped (scripts/package.sh leaves it out): the debug console's god mode, build hack and
    /// item hack switched on in every game, and the four schematic pages (church, glider, rollercoaster, tower) given,
    /// each by its own setting, all by <c>Enabled</c>. A cheat switched off in the settings (while the game runs too) is
    /// switched off in the game again. Flat-screen and VR.
    /// </summary>
    [BepInPlugin(Guid, "Dev Cheats", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "forestmods.devcheats";

        // PageChurch, PageGlider, PageRollercoaster, PageTower (the game's item database).
        private static readonly int[] Schematics = { 308, 309, 310, 311 };

        internal static ManualLogSource Log;

        private ConfigEntry<bool> _enabled, _godMode, _buildHack, _itemHack, _schematics;
        private PlayerInventory _given;
        private bool _failed, _godModeOn, _buildHackOn;
        private IInventoryItemFilter _filter; // the item hack's, while it's on
        private float _nextTick;

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true,
                "All cheats on (each by its own setting below) or all off. Read while the game runs: save the file to apply.");
            _godMode = Config.Bind("Cheats", "GodMode", true,
                "No damage, no hunger, thirst or tiredness, unlimited energy (the console's godmode).");
            _buildHack = Config.Bind("Cheats", "BuildHack", true,
                "Buildings need no materials and go up while you hold the build button (the console's buildhack, as in Creative games).");
            _itemHack = Config.Bind("Cheats", "ItemHack", true,
                "Unlimited items: everything counts as owned, 100000 of each (the console's itemhack).");
            _schematics = Config.Bind("Cheats", "Schematics", true,
                "Give the four schematic pages (church, glider, rollercoaster, tower) once per game, if not owned.");
            Log.LogInfo("Dev Cheats: applied (test build, not for release)");
        }

        private void Update()
        {
            if (_failed || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            try
            {
                Config.Reload();
                Tick();
            }
            catch (Exception e)
            {
                Log.LogError($"Dev Cheats: {e}");
                _failed = true;
            }
        }

        private void Tick()
        {
            bool on = _enabled.Value;
            // Every second: games and modes switch these back (a Creative game's end resets them, loading a save...).
            bool godMode = on && _godMode.Value;
            if (godMode || _godModeOn)
            {
                Cheats.GodMode = godMode;
                Cheats.NoSurvival = godMode;
                Cheats.InfiniteEnergy = godMode;
                _godModeOn = godMode;
            }
            bool buildHack = on && _buildHack.Value;
            if (buildHack || _buildHackOn)
            {
                Cheats.Creative = buildHack;
                _buildHackOn = buildHack;
            }

            var inventory = LocalPlayer.Inventory;
            if (!inventory || !LocalPlayer.Transform) return;
            bool itemHack = on && _itemHack.Value;
            if (itemHack && !(inventory.ItemFilter is InventoryItemFilter_Unlimited))
                inventory.ItemFilter = _filter = new InventoryItemFilter_Unlimited();
            else if (!itemHack && _filter != null)
            {
                if (inventory.ItemFilter == _filter) inventory.ItemFilter = null;
                _filter = null;
            }
            if (!on || inventory == _given) return;
            _given = inventory; // a new game or load: a new inventory
            if (_schematics.Value)
            {
                // Owned for real (the item hack's filter says everything is), added if not.
                var filter = inventory.ItemFilter;
                inventory.ItemFilter = null;
                try
                {
                    foreach (int id in Schematics)
                        if (!inventory.Owns(id)) inventory.AddItem(id);
                }
                finally { inventory.ItemFilter = filter; }
            }
            Log.LogInfo($"Dev Cheats: on for this game (god mode {_godMode.Value}, build hack {_buildHack.Value}, "
                        + $"item hack {_itemHack.Value}, schematics {_schematics.Value})");
        }
    }
}
