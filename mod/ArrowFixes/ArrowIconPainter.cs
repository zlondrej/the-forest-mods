using System;
using System.Collections.Generic;

namespace ArrowFixes
{
    internal enum IconBase { Wooden, Bone, Modern }

    internal enum IconCoating { None, Fire, Poison }

    /// <summary>
    /// Draws HUD icons for arrow types in the style of the game's own arrow icon: a white arrow pointing up-right with a
    /// chalky look (all in the transparency: grainy, speckled, rough edges; no outline, no colour), in the same size and
    /// proportions. Wooden copies the game icon (leaf head, fanned feathers), bone has a wide barbed head, modern a
    /// shaft that tapers into a point, two see-through stripes across it and short vanes. A badge in the empty top-left corner
    /// shows the upgrade (Icons/flame.png, Icons/poison.png, used as shapes). Plain RGBA pixels, no Unity types, so
    /// scripts/ClothPreview produces the identical images.
    /// </summary>
    internal static class ArrowIconPainter
    {
        private const int Supersample = 4;
        private const float BadgeX = 0.09f, BadgeY = 0.59f, BadgeSize = 0.30f; // badge box in icon space (y up)
        private static readonly Dictionary<IconCoating, PngMask> Badges = new Dictionary<IconCoating, PngMask>();

        /// <summary>RGBA pixels, bottom row first (Unity's Texture2D order). White; the shape is in the alpha.</summary>
        public static byte[] Paint(IconBase arrow, IconCoating coating, int size)
        {
            var badge = BadgeFor(coating);
            int hi = size * Supersample;
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float covered = 0f;
                for (int sy = 0; sy < Supersample; sy++)
                for (int sx = 0; sx < Supersample; sx++)
                {
                    float px = (x * Supersample + sx + 0.5f) / hi, py = (y * Supersample + sy + 0.5f) / hi;
                    // Rough, chalk-like edges: wobble the sample position a little.
                    float wx = px + 0.004f * (Noise(px * 90f, py * 90f) - 0.5f) + 0.002f * (Noise(px * 300f, py * 300f) - 0.5f);
                    float wy = py + 0.004f * (Noise(px * 90f + 31f, py * 90f + 17f) - 0.5f) + 0.002f * (Noise(px * 300f + 7f, py * 300f + 3f) - 0.5f);
                    covered += Coverage(arrow, badge, wx, wy);
                }
                float alpha = covered / (Supersample * Supersample) * Grain(x, y, size);
                int o = (y * size + x) * 4;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = 255;
                pixels[o + 3] = (byte)Math.Max(0, Math.Min(255, (int)(alpha * 255f + 0.5f)));
            }
            return pixels;
        }

        /// <summary>Chalk: mostly opaque with grainy, partly see-through blotches and specks (like the game icon).</summary>
        private static float Grain(int x, int y, int size)
        {
            float s = 152f / size; // keep the grain size similar at any icon size
            float g = 0.55f + 0.35f * Noise(x * 0.35f * s, y * 0.35f * s) + 0.25f * Noise(x * 0.9f * s + 50f, y * 0.9f * s + 50f);
            if (Hash(x * 7 + 3, y * 13 + 1) < 0.06f) g *= 0.5f; // specks
            return Math.Max(0f, Math.Min(1f, g));
        }

        private static PngMask BadgeFor(IconCoating coating)
        {
            if (coating == IconCoating.None) return null;
            if (Badges.TryGetValue(coating, out var mask)) return mask;
            var name = coating == IconCoating.Fire ? "flame.png" : "poison.png";
            using (var stream = typeof(ArrowIconPainter).Assembly.GetManifestResourceStream("ArrowFixes.Icons." + name))
                mask = stream == null ? null : PngMask.Read(stream);
            Badges[coating] = mask;
            return mask;
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>Coverage of the icon at (px, py) in [0,1]² (y up).</summary>
        private static float Coverage(IconBase arrow, PngMask badge, float px, float py)
        {
            if (badge != null)
            {
                float b = badge.Sample((px - BadgeX) / BadgeSize, 1f - (py - BadgeY) / BadgeSize);
                if (b > 0f) return b;
            }
            // Arrow space in pixels of the game's 152 px arrow icon, whose proportions these copy: the arrow lies on the
            // diagonal through the centre, s along it (towards the tip, up-right), t across.
            const float scale = 152f / 1.41421356f;
            float s = ((px - 0.5f) + (py - 0.5f)) * scale;
            float t = ((py - 0.5f) - (px - 0.5f)) * scale;
            if (!Head(arrow, s, t) && !Shaft(arrow, s, t) && !Fletching(arrow, s, t)) return 0f;
            return Shading(arrow, s, t);
        }

        /// <summary>
        /// Soft, darker (more transparent) lines like the game icon's: one along the lower-right side of the shaft through
        /// the feathers, and one across the base of the head (not on modern, whose point is barely a separate head).
        /// </summary>
        private static float Shading(IconBase arrow, float s, float t)
        {
            if (arrow == IconBase.Modern) return 1f;
            float shade = 1f;
            if (s < -35f) shade = Math.Min(shade, 1f - 0.75f * Tent(t + 1.0f, 1.1f)); // feathers
            if (arrow == IconBase.Wooden) shade = Math.Min(shade, 1f - Tent(s - 46.2f, 1.7f)); // head base: a thin gap, like the game icon
            else if (Math.Abs(t) < 1.8f) shade = Math.Min(shade, 1f - 0.95f * Tent(s - 47.8f, 1.3f)); // bone: where it meets the shaft
            return shade;
        }

        private static float Tent(float d, float halfWidth) => Math.Max(0f, 1f - Math.Abs(d) / halfWidth);

        private static bool Shaft(IconBase arrow, float s, float t)
        {
            if (s < -50f || s > 50f) return false;
            if (arrow != IconBase.Modern) return Math.Abs(t) <= 1.7f;
            // modern: a thin shaft with two stripes in front of the vanes, left as see-through gaps
            if ((s >= -31.8f && s <= -30.0f) || (s >= -27.8f && s <= -26.0f)) return false;
            return Math.Abs(t) <= 1.3f;
        }

        private static bool Head(IconBase arrow, float s, float t)
        {
            switch (arrow)
            {
                case IconBase.Wooden: // the game icon's head: a small leaf, widest near its back
                    return InPolygon(s, t, new[]
                    {
                        46.6f, 2.7f, 48.5f, 4.2f, 51f, 4.0f, 53f, 3.3f, 57f, 2.4f, 60f, 1.4f, 62.5f, 0f,
                        60f, -1.4f, 57f, -2.4f, 53f, -3.3f, 51f, -4.0f, 48.5f, -4.2f, 46.6f, -2.7f,
                    });
                case IconBase.Bone: // a wide, flat triangle with barbs reaching back
                    return InPolygon(s, t, new[] { 62f, 0f, 48.5f, 7.4f, 43.5f, 7.0f, 48f, 1.5f, 48f, -1.5f, 43.5f, -7.0f, 48.5f, -7.4f });
                default: // modern: no separate head, the shaft just tapers into a point
                    return InPolygon(s, t, new[] { 46f, 1.3f, 62f, 0f, 46f, -1.3f });
            }
        }

        private static bool Fletching(IconBase arrow, float s, float t)
        {
            float a = Math.Abs(t);
            if (arrow == IconBase.Modern) // short, stiff, rounded vanes
                return InPolygon(s, a, new[] { -52f, 1.0f, -37f, 1.0f, -43f, 6.0f, -48f, 6.6f, -52f, 5.2f });
            // the game icon's feathers: two vanes fanning out backwards past the end of the shaft
            return InPolygon(s, a, new[] { -36f, 0.8f, -41f, 5.0f, -47f, 7.6f, -52f, 8.6f, -54.5f, 8.2f, -52f, 1.0f });
        }

        // ------------------------------------------------------------------ helpers

        private static bool InPolygon(float x, float y, float[] xy)
        {
            bool inside = false;
            int n = xy.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = xy[i * 2], yi = xy[i * 2 + 1], xj = xy[j * 2], yj = xy[j * 2 + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        /// <summary>Smooth value noise in [0,1].</summary>
        private static float Noise(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float v00 = Hash(xi, yi), v10 = Hash(xi + 1, yi), v01 = Hash(xi, yi + 1), v11 = Hash(xi + 1, yi + 1);
            return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
