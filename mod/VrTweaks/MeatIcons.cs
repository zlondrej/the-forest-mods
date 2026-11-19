using TheForest.Items;
using TheForest.UI;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// The HUD's item widgets (fire, drying rack, stew pot, door weights) show small meat with the very same picture as
    /// meat (only the text label differs). Here the small meat entries draw it smaller, so the two are told apart at a
    /// glance.
    /// </summary>
    internal static class MeatIcons
    {
        public const float SmallScale = 0.7f;

        private static HudGui _done;

        /// <summary>A few times per second: once per HUD (it is recreated on load), shrink the small meat icons.</summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled) return;
            var hud = Scene.HudGui;
            if (!hud || hud == _done || ItemDatabase.Items == null) return;
            _done = hud;
            int shrunk = 0;
            foreach (var entry in hud.GetComponentsInChildren<ItemListWidgetEntry>(true))
            {
                if (!entry || !entry._icon || entry._itemIds == null || entry._itemIds.Length != 1 || !IsSmallMeat(entry._itemIds[0])) continue;
                var icon = entry._icon;
                icon.width = Mathf.RoundToInt(icon.width * SmallScale);
                icon.height = Mathf.RoundToInt(icon.height * SmallScale);
                shrunk++;
            }
            Plugin.Log.LogInfo($"SmallMeatIcon: {shrunk} small meat icons drawn smaller");
        }

        // By name ("SmallGenericMeat" and any cooked/dried variants): the ids are the game's business.
        private static bool IsSmallMeat(int itemId)
        {
            var item = ItemDatabase.IsItemidValid(itemId) ? ItemDatabase.ItemById(itemId) : null;
            if (item == null || string.IsNullOrEmpty(item._name)) return false;
            var name = item._name.ToLowerInvariant();
            return name.Contains("small") && name.Contains("meat");
        }
    }
}
