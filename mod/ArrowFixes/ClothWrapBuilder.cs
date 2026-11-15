using System;
using System.Collections.Generic;

namespace ArrowFixes
{
    /// <summary>
    /// Shape of a cloth wrap around an arrow shaft or head, in the arrow model's own mesh space (the arrow points along
    /// +Y). What is wrapped is described by stations along Y (centre and half-widths of its cross-section), linearly
    /// interpolated.
    /// </summary>
    internal sealed class ClothWrapSpec
    {
        public string Name;
        public float Y0, Y1;               // back and front end of the wrap
        public float[] StationY;           // ascending
        public float[] CenterX, CenterZ;   // centre of what is wrapped, at each station
        public float[] HalfX, HalfZ;       // its half-widths at each station
        public float Thickness;            // extra radius of the outermost layer (mesh units)
        public float Wobble = 0.07f;       // how much the band edges spiral, as a fraction of the wrap length
        public float EdgeThickness = 0.35f; // layer thickness at its edges vs. its middle; higher = edges stand out as flaps
        public int Layers = 3;
        public int Around = 12;            // segments around
        public int Along = 4;              // segments along

        /// <summary>
        /// Cross-section roundness: 2 = ellipse (round shaft); lower values pull the cloth flat against the faces of a
        /// flat blade and into an edge at its sides (1 = diamond).
        /// </summary>
        public float Roundness = 2f;

        public int Folds;                  // creases per layer, in random directions
        public float FoldDepth;            // how far a crease pushes the cloth out (+) or in (-) (mesh units)
        public float FoldWidth;            // width of a crease (mesh units)
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
    /// a few overlapping, slightly spiralling bands of cloth, optionally pulled flat around a blade and creased.
    /// UVs follow the game's cloth texture layout (u across the strip, with the frayed edges at u = 0 and 1; v around
    /// the arrow), so the game's cloth material can be used as-is. No Unity types here, so scripts/ClothPreview can
    /// build the identical mesh.
    /// </summary>
    internal static class ClothWrapBuilder
    {
        private struct Fold
        {
            public float S, Y;      // a point on the crease: position around the wrap (0..1) and along the arrow
            public float Dx, Dy;    // crease direction (unit vector in around/along space)
            public float Depth;     // + ridge, - pressed-in crease
        }

        public static ClothMeshData Build(ClothWrapSpec s)
        {
            var pos = new List<float>();
            var nrm = new List<float>();
            var uv = new List<float>();
            var tri = new List<int>();
            var rnd = new Random(s.Seed);
            float len = s.Y1 - s.Y0;
            // A plain round wrap gets exact ellipse normals; a flattened or creased one gets normals from its surface.
            bool round = s.Roundness == 2f && s.Folds == 0;
            float exponent = 2f / Math.Max(0.5f, s.Roundness);

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
                var folds = MakeFolds(s, rnd, b0, b1);

                int first = pos.Count / 3;
                for (int k = 0; k <= s.Along; k++)
                {
                    float t = k / (float)s.Along;
                    float yBase = b0 + (b1 - b0) * t;
                    float taper = s.EdgeThickness + (1f - s.EdgeThickness) * (float)Math.Sin(Math.PI * t); // thinner at the band edges
                    for (int j = 0; j <= s.Around; j++)
                    {
                        float a = (float)(2 * Math.PI * j / s.Around);
                        float y = yBase + s.Wobble * len * (float)Math.Sin(a + phase);
                        float noise = 1f + 0.18f * (0.6f * (float)Math.Sin(3 * a + p1 + 6 * t) + 0.4f * (float)Math.Sin(7 * a + p2 - 4 * t));
                        float e = thick * taper * noise;
                        Shape(s, y, out float cx, out float cz, out float hx, out float hz);
                        if (folds.Count > 0) e += Crease(folds, a, y, hx, hz, s.FoldWidth) * taper;
                        float rx = Math.Max(0.0005f, hx + e), rz = Math.Max(0.0005f, hz + e);
                        float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                        if (round)
                        {
                            pos.Add(cx + rx * ca); pos.Add(y); pos.Add(cz + rz * sa);
                            float nx = ca * rz, nz = sa * rx, nl = (float)Math.Sqrt(nx * nx + nz * nz);
                            nrm.Add(nx / nl); nrm.Add(0f); nrm.Add(nz / nl);
                        }
                        else
                        {
                            pos.Add(cx + rx * Signed(ca, exponent)); pos.Add(y); pos.Add(cz + rz * Signed(sa, exponent));
                        }
                        uv.Add(t); uv.Add(j / (float)s.Around + vOffset);
                    }
                }
                if (!round) AddSurfaceNormals(s, pos, nrm, first);
                AddQuads(s, pos, tri, first);
            }

            return new ClothMeshData { Positions = pos.ToArray(), Normals = nrm.ToArray(), Uvs = uv.ToArray(), Triangles = tri.ToArray() };
        }

        private static List<Fold> MakeFolds(ClothWrapSpec s, Random rnd, float b0, float b1)
        {
            var folds = new List<Fold>();
            for (int i = 0; i < s.Folds; i++)
            {
                // Mostly diagonal to the arrow, like cloth pulled around a point, leaning either way.
                float dir = (float)((rnd.NextDouble() * 0.9 + 0.3) * (rnd.NextDouble() < 0.5 ? 1 : -1));
                folds.Add(new Fold
                {
                    S = (float)rnd.NextDouble(),
                    Y = b0 + (b1 - b0) * (float)(0.15 + 0.7 * rnd.NextDouble()),
                    Dx = (float)Math.Cos(dir), Dy = (float)Math.Sin(dir),
                    Depth = s.FoldDepth * (float)(0.6 + 0.4 * rnd.NextDouble()) * (rnd.NextDouble() < 0.6 ? 1 : -1),
                });
            }
            return folds;
        }

        /// <summary>Sum of sharp (tent-shaped) creases at angle <paramref name="a"/> and position <paramref name="y"/>.</summary>
        private static float Crease(List<Fold> folds, float a, float y, float hx, float hz, float width)
        {
            if (width <= 0f) return 0f;
            // Distance around the cross-section (Ramanujan's ellipse perimeter is close enough).
            float h = (hx - hz) * (hx - hz) / ((hx + hz) * (hx + hz) + 1e-12f);
            float perimeter = (float)(Math.PI * (hx + hz) * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h))));
            float around = a / (float)(2 * Math.PI);
            float sum = 0f;
            foreach (var f in folds)
            {
                float ds = around - f.S;
                ds -= (float)Math.Round(ds); // the shortest way around
                float du = ds * perimeter, dy = y - f.Y;
                float dist = Math.Abs(du * f.Dy - dy * f.Dx); // distance to the crease line
                float along = du * f.Dx + dy * f.Dy;           // creases fade out along their length
                float fade = Math.Max(0f, 1f - Math.Abs(along) / (width * 4f));
                sum += f.Depth * Math.Max(0f, 1f - dist / width) * fade;
            }
            return sum;
        }

        private static float Signed(float v, float exponent) => Math.Sign(v) * (float)Math.Pow(Math.Abs(v), exponent);

        /// <summary>Normals from the actual surface (so creases and edges catch the light), pointing away from the axis.</summary>
        private static void AddSurfaceNormals(ClothWrapSpec s, List<float> p, List<float> nrm, int first)
        {
            int w = s.Around + 1;
            for (int k = 0; k <= s.Along; k++)
            for (int j = 0; j <= s.Around; j++)
            {
                // Neighbours around (the seam column j = Around is the same ring point as j = 0) and along.
                int jm = (j + s.Around - 1) % s.Around, jp = (j + 1) % s.Around;
                int km = Math.Max(0, k - 1), kp = Math.Min(s.Along, k + 1);
                int i = first + k * w + j;
                Sub(p, first + k * w + jp, first + k * w + jm, out float ax, out float ay, out float az);
                Sub(p, first + kp * w + j, first + km * w + j, out float bx, out float by, out float bz);
                float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                Shape(s, p[i * 3 + 1], out float cx, out float cz, out _, out _);
                if (nx * (p[i * 3] - cx) + nz * (p[i * 3 + 2] - cz) < 0f) { nx = -nx; ny = -ny; nz = -nz; }
                float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-12f;
                nrm.Add(nx / l); nrm.Add(ny / l); nrm.Add(nz / l);
            }
        }

        private static void Sub(List<float> p, int a, int b, out float x, out float y, out float z)
        {
            x = p[a * 3] - p[b * 3]; y = p[a * 3 + 1] - p[b * 3 + 1]; z = p[a * 3 + 2] - p[b * 3 + 2];
        }

        /// <summary>Two triangles per grid cell, wound so they face away from the axis.</summary>
        private static void AddQuads(ClothWrapSpec s, List<float> pos, List<int> tri, int first)
        {
            int w = s.Around + 1;
            for (int k = 0; k < s.Along; k++)
            for (int j = 0; j < s.Around; j++)
            {
                int a = first + k * w + j, b = a + 1, c = a + w, d = c + 1;
                AddTriangle(s, pos, tri, a, b, c);
                AddTriangle(s, pos, tri, b, d, c);
            }
        }

        private static void AddTriangle(ClothWrapSpec s, List<float> p, List<int> tri, int a, int b, int c)
        {
            // Face normal (Unity: cross(b-a, c-a)) vs. the direction away from the axis at the triangle.
            Sub(p, b, a, out float ux, out float uy, out float uz);
            Sub(p, c, a, out float vx, out float vy, out float vz);
            float nx = uy * vz - uz * vy, nz = ux * vy - uy * vx;
            float mx = (p[a * 3] + p[b * 3] + p[c * 3]) / 3f;
            float my = (p[a * 3 + 1] + p[b * 3 + 1] + p[c * 3 + 1]) / 3f;
            float mz = (p[a * 3 + 2] + p[b * 3 + 2] + p[c * 3 + 2]) / 3f;
            Shape(s, my, out float cx, out float cz, out _, out _);
            if (nx * (mx - cx) + nz * (mz - cz) > 0f) { tri.Add(a); tri.Add(b); tri.Add(c); }
            else { tri.Add(a); tri.Add(c); tri.Add(b); }
        }

        private static void Shape(ClothWrapSpec s, float y, out float cx, out float cz, out float hx, out float hz)
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
