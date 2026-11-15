using System;
using System.Collections.Generic;

namespace ArrowFixes
{
    /// <summary>
    /// Shape of a cloth wrap around an arrow shaft, in the arrow model's own mesh space (the arrow points along +Y).
    /// The shaft is described by stations along Y (centre and half-widths of its cross-section), linearly interpolated.
    /// </summary>
    internal sealed class ClothWrapSpec
    {
        public string Name;
        public float Y0, Y1;               // back and front end of the wrap
        public float[] StationY;           // ascending
        public float[] CenterX, CenterZ;   // shaft centre at each station
        public float[] HalfX, HalfZ;       // shaft half-widths at each station
        public float Thickness;            // extra radius of the outermost layer (mesh units)
        public float Wobble = 0.07f;       // how much the band edges spiral, as a fraction of the wrap length
        public int Layers = 3;

        /// <summary>
        /// Texture patches around the wrap. With more than one, each patch of each layer gets the cloth texture at its
        /// own angle (up to <see cref="UvTwist"/> degrees), so the weave and the frayed strip edges change direction
        /// where the cloth goes over an edge, as if folded there. Patch borders are texture seams (duplicated vertices),
        /// starting at the +X side.
        /// </summary>
        public int UvPatches = 1;
        public float UvTwist;
        public int Seed;
    }

    /// <summary>Plain mesh arrays (Unity conventions: triangle (a,b,c) faces the side cross(b-a, c-a) points to).</summary>
    internal sealed class ClothMeshData
    {
        public float[] Positions; // xyz
        public float[] Normals;   // xyz
        public float[] Uvs;       // uv
        public int[] Triangles;
    }

    /// <summary>
    /// Builds a cloth wrap for arrows that have no cloth mesh of their own (bone, modern). The game's single cloth mesh is
    /// shaped for the wooden arrow and can't be reshaped at runtime (it isn't readable), so the wrap is generated:
    /// a few overlapping, slightly spiralling and lumpy bands of cloth around the shaft. UVs follow the game's cloth
    /// texture layout (u across the strip, with the frayed edges at u = 0 and 1; v around the arrow), so the game's
    /// cloth material can be used as-is. No Unity types here, so scripts/ClothPreview can build the identical mesh.
    /// </summary>
    internal static class ClothWrapBuilder
    {
        // Kept close to the game's own wrap (240 vertices, 318 triangles): 3 layers x 13 x 5 = 195 vertices, 288 triangles
        // (each extra texture patch adds a column of seam vertices per layer).
        private const int Around = 12; // segments around the shaft
        private const int Along = 4;   // segments along the wrap

        public static ClothMeshData Build(ClothWrapSpec s)
        {
            var pos = new List<float>();
            var nrm = new List<float>();
            var uv = new List<float>();
            var tri = new List<int>();
            var rnd = new Random(s.Seed);
            var uvRnd = new Random(s.Seed + 1000); // separate, so texture settings don't change the shape
            float len = s.Y1 - s.Y0;
            int patches = Math.Max(1, s.UvPatches);
            // Patch borders start at angle 0 (+X); with 2 patches they sit on the two edges of a blade that is wide in X,
            // so each flat face is one patch and the texture only turns where the cloth goes over an edge.
            const int shift = 0;

            for (int layer = 0; layer < s.Layers; layer++)
            {
                float f = s.Layers > 1 ? layer / (float)(s.Layers - 1) : 1f;
                // Outer layers are shorter and sit a little further forward, which gives the wrap a lumpy profile.
                float b0 = s.Y0 + len * 0.10f * layer;
                float b1 = s.Y1 - len * 0.06f * layer;
                float thick = s.Thickness * (0.45f + 0.55f * f);
                float phase = (float)(rnd.NextDouble() * Math.PI * 2);
                float p1 = (float)(rnd.NextDouble() * Math.PI * 2), p2 = (float)(rnd.NextDouble() * Math.PI * 2);
                float vOffset = (float)rnd.NextDouble();

                Shaft(s, (b0 + b1) / 2, out _, out _, out float mhx, out float mhz);
                float perimeter = (float)(Math.PI * (mhx + mhz + 2 * thick)); // around the wrap, roughly
                for (int patch = 0; patch < patches; patch++)
                {
                int j0 = shift + patch * Around / patches, j1 = shift + (patch + 1) * Around / patches;
                float angle = 0f, uOffset = 0f, vPatch = 0f;
                if (patches > 1)
                {
                    float twist = (float)((0.35 + 0.65 * uvRnd.NextDouble()) * s.UvTwist * Math.PI / 180);
                    angle = uvRnd.NextDouble() < 0.5 ? twist : -twist;
                    uOffset = (float)(uvRnd.NextDouble() * 0.3 - 0.15);
                    vPatch = (float)uvRnd.NextDouble();
                }
                float cosA = (float)Math.Cos(angle), sinA = (float)Math.Sin(angle);
                float aMid = (j0 + j1) / 2f / Around;

                int first = pos.Count / 3;
                for (int k = 0; k <= Along; k++)
                {
                    float t = k / (float)Along;
                    float yBase = b0 + (b1 - b0) * t;
                    float taper = 0.35f + 0.65f * (float)Math.Sin(Math.PI * t); // thinner at the band edges
                    for (int j = j0; j <= j1; j++)
                    {
                        float a = (float)(2 * Math.PI * j / Around);
                        float y = yBase + s.Wobble * len * (float)Math.Sin(a + phase);
                        float noise = 1f + 0.18f * (0.6f * (float)Math.Sin(3 * a + p1 + 6 * t) + 0.4f * (float)Math.Sin(7 * a + p2 - 4 * t));
                        float e = thick * taper * noise;
                        Shaft(s, y, out float cx, out float cz, out float hx, out float hz);
                        float rx = hx + e, rz = hz + e;
                        float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                        pos.Add(cx + rx * ca); pos.Add(y); pos.Add(cz + rz * sa);
                        // Ellipse normal.
                        float nx = ca * rz, nz = sa * rx, nl = (float)Math.Sqrt(nx * nx + nz * nz);
                        nrm.Add(nx / nl); nrm.Add(0f); nrm.Add(nz / nl);
                        if (patches == 1)
                        {
                            uv.Add(t); uv.Add(j / (float)Around + vOffset);
                        }
                        else
                        {
                            // The strip's own frame (u across, v along the strip), turned by this patch's angle.
                            float du = (t - 0.5f) * (b1 - b0), dv = (j / (float)Around - aMid) * perimeter;
                            uv.Add(0.5f + uOffset + (du * cosA - dv * sinA) / (b1 - b0));
                            uv.Add(vOffset + vPatch + (du * sinA + dv * cosA) / perimeter);
                        }
                    }
                }
                AddQuads(s, pos, tri, first, j1 - j0, outward: true);
                }

            }

            return new ClothMeshData { Positions = pos.ToArray(), Normals = nrm.ToArray(), Uvs = uv.ToArray(), Triangles = tri.ToArray() };
        }

        /// <summary>Two triangles per grid cell, wound so they face away from (or towards) the shaft axis.</summary>
        private static void AddQuads(ClothWrapSpec s, List<float> pos, List<int> tri, int first, int columns, bool outward)
        {
            int w = columns + 1;
            for (int k = 0; k < Along; k++)
            for (int j = 0; j < columns; j++)
            {
                int a = first + k * w + j, b = a + 1, c = a + w, d = c + 1;
                AddTriangle(s, pos, tri, a, b, c, outward);
                AddTriangle(s, pos, tri, b, d, c, outward);
            }
        }

        private static void AddTriangle(ClothWrapSpec s, List<float> p, List<int> tri, int a, int b, int c, bool outward)
        {
            // Face normal (Unity: cross(b-a, c-a)) vs. the direction away from the shaft axis at the triangle.
            float ux = p[b * 3] - p[a * 3], uy = p[b * 3 + 1] - p[a * 3 + 1], uz = p[b * 3 + 2] - p[a * 3 + 2];
            float vx = p[c * 3] - p[a * 3], vy = p[c * 3 + 1] - p[a * 3 + 1], vz = p[c * 3 + 2] - p[a * 3 + 2];
            float nx = uy * vz - uz * vy, nz = ux * vy - uy * vx;
            float mx = (p[a * 3] + p[b * 3] + p[c * 3]) / 3f;
            float my = (p[a * 3 + 1] + p[b * 3 + 1] + p[c * 3 + 1]) / 3f;
            float mz = (p[a * 3 + 2] + p[b * 3 + 2] + p[c * 3 + 2]) / 3f;
            Shaft(s, my, out float cx, out float cz, out _, out _);
            bool facesOut = nx * (mx - cx) + nz * (mz - cz) > 0f;
            if (facesOut == outward) { tri.Add(a); tri.Add(b); tri.Add(c); }
            else { tri.Add(a); tri.Add(c); tri.Add(b); }
        }

        private static void Shaft(ClothWrapSpec s, float y, out float cx, out float cz, out float hx, out float hz)
        {
            var ys = s.StationY;
            int i = 0;
            while (i < ys.Length - 2 && y > ys[i + 1]) i++;
            float t = ys.Length == 1 ? 0f : Clamp01((y - ys[i]) / (ys[i + 1] - ys[i]));
            int n = Math.Min(i + 1, ys.Length - 1);
            cx = Lerp(s.CenterX[i], s.CenterX[n], t);
            cz = Lerp(s.CenterZ[i], s.CenterZ[n], t);
            hx = Lerp(s.HalfX[i], s.HalfX[n], t);
            hz = Lerp(s.HalfZ[i], s.HalfZ[n], t);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
