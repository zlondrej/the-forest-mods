using System.Collections.Generic;
using ArrowFixes;
using TheForest.Items;
using TheForest.UI;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// The HUD's item widgets (fire, drying rack, stew pot, door weights) show meat and small meat with the very same
    /// picture (only the text label differs). Here both get a size badge: the meat a little smaller, bottom left, and
    /// a rounded square with "L" (meat) or "S" (small meat) cut out, top right. The badges ship with the mod
    /// (Icons/Badge*.png, rendered by scripts/chalk-icons.html); the meat is the game's own icon, combined when the HUD
    /// loads.
    /// </summary>
    internal static class MeatIcons
    {
        // Where things go in the icon: centre (0..1, from the bottom left) and size, of the icon's size.
        private static readonly Vector2 MeatCentre = new Vector2(0.489f, 0.380f), BadgeCentre = new Vector2(0.805f, 0.782f);
        private const float MeatSize = 1.047f, BadgeSize = 0.24f; // BadgeSize: as in scripts/chalk-icons.html
        private static readonly Color32 Chalk = new Color32(251, 251, 251, 255); // the game's icon white

        private static HudGui _done;
        private static readonly Dictionary<string, PngMask> Badges = new Dictionary<string, PngMask>();
        private static readonly Dictionary<string, Texture2D> Made = new Dictionary<string, Texture2D>();

        /// <summary>A few times per second: once per HUD (it is recreated on load), the meat icons.</summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled) return;
            var hud = Scene.HudGui;
            if (!hud || hud == _done || ItemDatabase.Items == null) return;
            _done = hud;
            int large = 0, small = 0;
            foreach (var entry in hud.GetComponentsInChildren<ItemListWidgetEntry>(true))
            {
                if (!entry || entry._itemIds == null || entry._itemIds.Length != 1) continue;
                var texture = entry._icon as UITexture;
                string letter = SizeLetter(entry._itemIds[0]);
                if (!texture || letter == null) continue;
                var icon = Badged(texture.mainTexture, letter);
                if (!icon) continue;
                texture.mainTexture = icon;
                if (letter == "S") small++;
                else large++;
            }
            Plugin.Log.LogInfo($"MeatIcons: size badges on {large} meat and {small} small meat icons");
        }

        /// <summary>"L" for meat, "S" for small meat, by item name ("GenericMeat", "SmallGenericMeat"); else null.</summary>
        private static string SizeLetter(int itemId)
        {
            var item = ItemDatabase.IsItemidValid(itemId) ? ItemDatabase.ItemById(itemId) : null;
            if (item == null || string.IsNullOrEmpty(item._name)) return null;
            var name = item._name.ToLowerInvariant();
            if (!name.Contains("meat") || name.Contains("turtle")) return null;
            return name.Contains("small") ? "S" : "L";
        }

        /// <summary>The game's meat icon with the size badge (made once per letter, reused).</summary>
        private static Texture2D Badged(Texture meat, string letter)
        {
            if (!meat) return null;
            var key = meat.GetInstanceID() + letter;
            if (Made.TryGetValue(key, out var made) && made) return made;
            var badge = Badge(letter);
            if (badge == null) return null;
            int w = meat.width, h = meat.height;
            var pixels = new Color32[w * h];
            Draw(pixels, w, h, Read(meat, w, h), w, h, MeatCentre, MeatSize);
            DrawBadge(pixels, w, h, badge);
            var icon = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "VrTweaks_Meat" + letter, wrapMode = TextureWrapMode.Clamp };
            icon.SetPixels32(pixels);
            icon.Apply(false, true);
            Object.DontDestroyOnLoad(icon);
            Made[key] = icon;
            return icon;
        }

        private static PngMask Badge(string letter)
        {
            if (Badges.TryGetValue(letter, out var badge)) return badge;
            var name = $"VrTweaks.Icons.Badge{letter}.png";
            using (var stream = typeof(MeatIcons).Assembly.GetManifestResourceStream(name))
                badge = stream == null ? null : PngMask.Read(stream);
            if (badge == null) Plugin.Log.LogWarning($"MeatIcons: {name} missing from the plugin");
            Badges[letter] = badge;
            return badge;
        }

        /// <summary>The badge in its corner, scaled to BadgeSize of the icon.</summary>
        private static void DrawBadge(Color32[] dst, int w, int h, PngMask badge)
        {
            int size = Mathf.RoundToInt(w * BadgeSize);
            int left = Mathf.RoundToInt(BadgeCentre.x * w - size / 2f), bottom = Mathf.RoundToInt(BadgeCentre.y * h - size / 2f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int px = left + x, py = bottom + y;
                if (px < 0 || px >= w || py < 0 || py >= h) continue;
                // The PNG is larger than the badge in the icon: 4×4 samples per icon pixel, averaged (a smooth
                // downscale). The mask's rows are top first; the texture's bottom first.
                float a = 0f;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    a += badge.Sample((x + (sx + 0.5f) / 4f) / size, 1f - (y + (sy + 0.5f) / 4f) / size);
                a /= 16f;
                if (a <= 0f) continue;
                Blend(dst, py * w + px, new Color(Chalk.r / 255f, Chalk.g / 255f, Chalk.b / 255f, a));
            }
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
                if (c.a > 0f) Blend(dst, y * dw + x, c);
            }
        }

        private static void Blend(Color32[] dst, int i, Color c)
        {
            Color under = dst[i];
            float a = c.a + under.a * (1f - c.a);
            var rgb = a > 0f ? (c * c.a + under * under.a * (1f - c.a)) / a : Color.clear;
            dst[i] = new Color(rgb.r, rgb.g, rgb.b, a);
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
