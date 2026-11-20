using TheForest.Items;
using TheForest.UI;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// The HUD's item widgets (fire, drying rack, stew pot, door weights) show small meat with the very same picture as
    /// meat (only the text label differs). Here small meat gets its own icon: the meat, smaller, with the game's rabbit
    /// icon (small game) above it, made from the game's own HUD icons when the HUD loads. If the rabbit icon isn't
    /// found, the meat is only drawn smaller.
    /// </summary>
    internal static class MeatIcons
    {
        public const float SmallScale = 0.7f;

        // Where the two pictures go in the new icon: centre (0..1, from the bottom left) and size, of the icon's size.
        private static readonly Vector2 MeatCentre = new Vector2(0.60f, 0.38f), RabbitCentre = new Vector2(0.32f, 0.70f);
        private const float MeatSize = 0.72f, RabbitSize = 0.62f;

        private static HudGui _done;
        private static Texture2D _icon;
        private static Texture _iconMeat;

        /// <summary>A few times per second: once per HUD (it is recreated on load), the small meat icons.</summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled) return;
            var hud = Scene.HudGui;
            if (!hud || hud == _done || ItemDatabase.Items == null) return;
            _done = hud;
            int replaced = 0, shrunk = 0;
            foreach (var entry in hud.GetComponentsInChildren<ItemListWidgetEntry>(true))
            {
                if (!entry || !entry._icon || entry._itemIds == null || entry._itemIds.Length != 1 || !IsSmallMeat(entry._itemIds[0])) continue;
                var texture = entry._icon as UITexture;
                var icon = texture ? SmallMeatIcon(texture.mainTexture) : null;
                if (icon)
                {
                    texture.mainTexture = icon;
                    replaced++;
                }
                else
                {
                    entry._icon.width = Mathf.RoundToInt(entry._icon.width * SmallScale);
                    entry._icon.height = Mathf.RoundToInt(entry._icon.height * SmallScale);
                    shrunk++;
                }
            }
            Plugin.Log.LogInfo($"SmallMeatIcon: {replaced} small meat icons with the rabbit, {shrunk} drawn smaller");
        }

        // By name ("SmallGenericMeat" and any cooked/dried variants): the ids are the game's business.
        private static bool IsSmallMeat(int itemId)
        {
            var item = ItemDatabase.IsItemidValid(itemId) ? ItemDatabase.ItemById(itemId) : null;
            if (item == null || string.IsNullOrEmpty(item._name)) return false;
            var name = item._name.ToLowerInvariant();
            return name.Contains("small") && name.Contains("meat");
        }

        /// <summary>The small meat icon made from <paramref name="meat"/> (made once, reused); null without a rabbit icon.</summary>
        private static Texture2D SmallMeatIcon(Texture meat)
        {
            if (!meat) return null;
            if (_icon && _iconMeat == meat) return _icon;
            var rabbit = FindTexture("Rabbit");
            if (!rabbit) return null;
            int w = meat.width, h = meat.height;
            var meatPixels = Read(meat, w, h);
            var rabbitPixels = Read(rabbit, rabbit.width, rabbit.height);
            var pixels = new Color32[w * h];
            Draw(pixels, w, h, meatPixels, w, h, MeatCentre, MeatSize);
            Draw(pixels, w, h, rabbitPixels, rabbit.width, rabbit.height, RabbitCentre, RabbitSize);
            if (_icon) Object.Destroy(_icon);
            _icon = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "VrTweaks_SmallMeat", wrapMode = TextureWrapMode.Clamp };
            _icon.SetPixels32(pixels);
            _icon.Apply(false, true);
            Object.DontDestroyOnLoad(_icon);
            _iconMeat = meat;
            return _icon;
        }

        private static Texture2D FindTexture(string name)
        {
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (texture && texture.name == name) return texture;
            return null;
        }

        /// <summary>A texture's pixels (HUD textures aren't readable: drawn into a render texture and read back).</summary>
        private static Color32[] Read(Texture texture, int w, int h)
        {
            var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply(false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(copy);
            }
        }

        /// <summary>Draws <paramref name="src"/> over <paramref name="dst"/>, scaled (bilinear) to <paramref name="size"/> of it around <paramref name="centre"/>.</summary>
        private static void Draw(Color32[] dst, int dw, int dh, Color32[] src, int sw, int sh, Vector2 centre, float size)
        {
            float width = dw * size, height = dh * size;
            float left = centre.x * dw - width / 2f, bottom = centre.y * dh - height / 2f;
            for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                float u = (x + 0.5f - left) / width, v = (y + 0.5f - bottom) / height;
                if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                var c = Sample(src, sw, sh, u, v);
                if (c.a <= 0f) continue;
                int i = y * dw + x;
                Color under = dst[i];
                float a = c.a + under.a * (1f - c.a);
                var rgb = a > 0f ? ((Color)c * c.a + under * under.a * (1f - c.a)) / a : Color.clear;
                dst[i] = new Color(rgb.r, rgb.g, rgb.b, a);
            }
        }

        private static Color Sample(Color32[] src, int w, int h, float u, float v)
        {
            float fx = Mathf.Clamp(u * w - 0.5f, 0f, w - 1f), fy = Mathf.Clamp(v * h - 0.5f, 0f, h - 1f);
            int x0 = (int)fx, y0 = (int)fy, x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float tx = fx - x0, ty = fy - y0;
            // Premultiplied, so transparent pixels' colours don't bleed in.
            Color P(int x, int y) { Color c = src[y * w + x]; return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a); }
            var c0 = Color.Lerp(P(x0, y0), P(x1, y0), tx);
            var c1 = Color.Lerp(P(x0, y1), P(x1, y1), tx);
            var p = Color.Lerp(c0, c1, ty);
            return p.a > 0f ? new Color(p.r / p.a, p.g / p.a, p.b / p.a, p.a) : Color.clear;
        }
    }
}
