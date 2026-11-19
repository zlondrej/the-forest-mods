using TheForest.Items;
using TheForest.UI;
using TheForest.Utils;
using UnityEngine;

namespace ArrowFixes
{
    /// <summary>
    /// The arrow-type icons' material badges use the game's own HUD icons: the stick for wooden arrows, the bone for
    /// bone arrows (the item widgets have entries for both). Read once from the HUD when it exists, then the icons are
    /// painted in the background (so showing one the first time doesn't freeze a frame). The game's textures can't be
    /// read directly, so each is drawn into a temporary render texture and read back from there.
    /// </summary>
    internal static class GameBadges
    {
        private static bool _done;

        /// <summary>A few times per second until done.</summary>
        public static void Tick()
        {
            if (_done) return;
            var hud = Scene.HudGui;
            if (!hud || ItemDatabase.Items == null) return;
            _done = true;
            PngMask stick = null, bone = null;
            try
            {
                stick = Shape(hud, "stick");
                bone = Shape(hud, "bone");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Arrow icons: the game's stick/bone icons couldn't be read, drawn badges instead. {e.Message}"); }
            Plugin.Log.LogInfo($"Arrow icons: the game's stick icon {(stick != null ? "found" : "not found (no badge on wooden)")}, bone icon {(bone != null ? "found" : "not found (drawn bone)")}");
            ArrowIconTextures.UseMaterialBadges(stick, bone);
            ArrowIconTextures.PrepaintInBackground();
        }

        /// <summary>The icon shape of the HUD item entry for the item of this name (case ignored).</summary>
        private static PngMask Shape(HudGui hud, string itemName)
        {
            foreach (var entry in hud.GetComponentsInChildren<ItemListWidgetEntry>(true))
            {
                if (!entry || entry._itemIds == null || entry._itemIds.Length != 1) continue;
                var item = ItemDatabase.IsItemidValid(entry._itemIds[0]) ? ItemDatabase.ItemById(entry._itemIds[0]) : null;
                if (item == null || !string.Equals(item._name, itemName, System.StringComparison.OrdinalIgnoreCase)) continue;
                var texture = entry._icon is UITexture ui ? ui.mainTexture : null;
                if (texture) return Alpha(texture).AsSolidShape();
            }
            return null;
        }

        private static PngMask Alpha(Texture texture)
        {
            int w = texture.width, h = texture.height;
            var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply(false);
                var pixels = copy.GetPixels32();
                var alpha = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
                return PngMask.FromAlpha(w, h, alpha);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(copy);
            }
        }
    }
}
