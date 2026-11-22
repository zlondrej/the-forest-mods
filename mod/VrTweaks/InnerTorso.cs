using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrTweaks
{
    /// <summary>
    /// The neck belongs to the head mesh, and the head isn't drawn in VR, so the drawn body had an open collar: looking
    /// down at it, you saw into the body. Here a bare torso is drawn inside the clothes, like a tailor's dummy without
    /// a neck: the shoulders, upper chest and upper back around the collar, closed over the neck by one smooth surface
    /// carrying on the torso's slopes to a low, rounded top. Whatever the collar's shape and size, skin is seen through it. One for every outfit:
    /// made from the T-shirt (the outfit fitting the body closest; its mesh is readable, most outfits' aren't), inside
    /// it, a little at the collar and more further from it (so it stays inside the other outfits too), with the head's
    /// skin (one texel of the throat) and moved by the same bones. No shadow: the body's is there.
    /// </summary>
    internal static class InnerTorso
    {
        private const string Source = "char_TShirt_default";

        // Metres: how far from the collar the torso reaches, and from how far (the T-shirt's hem, folding in, is left
        // out: the surface over the neck starts there); how far inside the T-shirt it is at the collar and from how far
        // away from it fully (Depth). Measured against every outfit's: the other shirts' collars are up to 2 cm lower
        // and wider at the back, and their backs and shoulders looser. The surface over the neck follows the torso
        // within Band of its edge.
        private const float Reach = 0.12f, Hem = 0.025f, Edge = 0.012f, Depth = 0.025f, Ease = 0.06f, Band = 0.03f;

        private const int Points = 40; // around the neck

        // From the torso's edge to the top: how far along each ring is.
        private static readonly float[] Rings = { 0.12f, 0.25f, 0.38f, 0.5f, 0.62f, 0.74f, 0.85f, 0.93f };

        private static readonly Dictionary<Mesh, Mesh> Built = new Dictionary<Mesh, Mesh>(); // by the T-shirt's mesh
        private static VRPlayerAdapter _adapter;
        private static SkinnedMeshRenderer _torso, _head;
        private static bool _tried;
        private static MaterialPropertyBlock _block;

        /// <summary>Every frame: drawn on <paramref name="layer"/> while <paramref name="show"/> (the body is drawn).</summary>
        public static void Update(VRPlayerAdapter adapter, bool show, int layer)
        {
            if (adapter != _adapter)
            {
                if (_torso) Object.Destroy(_torso.gameObject);
                _torso = null;
                _head = null;
                _tried = false;
                _adapter = adapter;
            }
            if (show && !_tried && adapter)
            {
                _tried = true;
                _torso = Make(adapter);
            }
            if (!_torso) return;
            var head = Head(adapter);
            int cameraLayer = LayerMask.NameToLayer("Camera");
            // The head drawn itself (knocked down, far enough from your eyes): its own neck.
            bool headDrawn = head && head.enabled && head.gameObject.activeInHierarchy && head.gameObject.layer != cameraLayer
                             && head.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
            bool on = show && head && !headDrawn;
            if (_torso.enabled != on) _torso.enabled = on;
            if (!on) return;
            if (_torso.gameObject.layer != layer) _torso.gameObject.layer = layer;
            if (head != _head)
            {
                _head = head;
                _torso.sharedMaterial = head.sharedMaterial; // this character's skin
                SetSkinTexel(_torso.sharedMesh, head);
            }
            if (_block == null) _block = new MaterialPropertyBlock();
            head.GetPropertyBlock(_block); // blood, mud
            _torso.SetPropertyBlock(_block);
        }

        /// <summary>The head mesh drawn for this character ("char_head1_geo", "char_head4_geo"...; not its shadow copy).</summary>
        private static SkinnedMeshRenderer Head(VRPlayerAdapter adapter)
        {
            if (_head && _head.gameObject.activeInHierarchy) return _head;
            if (adapter.PlayerMeshesToDisable != null)
                foreach (var go in adapter.PlayerMeshesToDisable)
                {
                    if (!go) continue;
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                    {
                        var lower = r.name.ToLowerInvariant();
                        if (r.sharedMesh && lower.StartsWith("char_head") && lower.Contains("geo") && !lower.Contains("hair")
                            && !lower.Contains("shadow"))
                            return r;
                    }
                }
            return adapter.PlayerHead1 as SkinnedMeshRenderer;
        }

        private static SkinnedMeshRenderer Make(VRPlayerAdapter adapter)
        {
            SkinnedMeshRenderer source = null;
            if (adapter.PlayerMeshesToDisable != null)
                foreach (var go in adapter.PlayerMeshesToDisable)
                {
                    if (!go || source) continue;
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) // worn or not
                        if (r.name == Source && r.sharedMesh) { source = r; break; }
                }
            if (!source)
                foreach (var r in adapter.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.name == Source && r.sharedMesh) { source = r; break; }
            if (!source)
            {
                Plugin.Log.LogWarning($"InnerTorso: no {Source} on the player: the collar stays open");
                return null;
            }
            if (!Built.TryGetValue(source.sharedMesh, out var mesh))
            {
                mesh = Build(source);
                Built[source.sharedMesh] = mesh;
            }
            if (!mesh) return null;
            // Not under the player: the game goes through its clothes' and bones' children.
            var torso = new GameObject("VrTweaks inner torso").AddComponent<SkinnedMeshRenderer>();
            torso.sharedMesh = mesh;
            torso.bones = source.bones;
            torso.rootBone = source.rootBone;
            torso.localBounds = source.localBounds;
            torso.quality = source.quality;
            torso.shadowCastingMode = ShadowCastingMode.Off;
            torso.receiveShadows = source.receiveShadows;
            torso.enabled = false;
            return torso;
        }

        /// <summary>All of it one colour: the head's skin at the throat (its front-most vertex moved by the neck).</summary>
        private static void SetSkinTexel(Mesh torso, SkinnedMeshRenderer head)
        {
            var mesh = head.sharedMesh;
            if (!torso || !mesh || !mesh.isReadable) return;
            var bones = head.bones;
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var uvs = mesh.uv;
            if (uvs.Length != vertices.Length || weights.Length != vertices.Length) return;
            int best = -1;
            for (int i = 0; i < vertices.Length; i++)
            {
                int bone = weights[i].boneIndex0; // the strongest
                if (bone >= bones.Length || !bones[bone] || !bones[bone].name.StartsWith("char_Neck")) continue;
                if (best < 0 || vertices[i].z > vertices[best].z) best = i;
            }
            if (best < 0) return;
            var uv = new Vector2[torso.vertexCount];
            for (int i = 0; i < uv.Length; i++) uv[i] = uvs[best];
            torso.uv = uv;
        }

        private static Mesh Build(SkinnedMeshRenderer source)
        {
            var mesh = source.sharedMesh;
            if (!mesh.isReadable)
            {
                Plugin.Log.LogWarning($"InnerTorso: {source.name}'s mesh isn't readable: the collar stays open");
                return null;
            }
            var bones = source.bones;
            var bindposes = mesh.bindposes;
            int neckBone = -1;
            for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
                if (bones[i] && bones[i].name == "char_Neck") { neckBone = i; break; }
            if (neckBone < 0) return null;
            var neck = bindposes[neckBone].inverse.MultiplyPoint3x4(Vector3.zero);
            float scale = (bones[neckBone].localToWorldMatrix * bindposes[neckBone]).MultiplyVector(Vector3.up).magnitude;
            if (scale <= 1e-6f) return null;

            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var weights = mesh.boneWeights;
            var triangles = mesh.triangles;
            if (normals.Length != vertices.Length || weights.Length != vertices.Length) return null;

            // Welded points (seams split vertices; one colour, so none needed), normals averaged over them.
            var ids = new Dictionary<long, int>();
            var weld = new int[vertices.Length];
            var points = new List<Vector3>();
            var pointNormals = new List<Vector3>();
            var pointWeights = new List<BoneWeight>();
            for (int i = 0; i < vertices.Length; i++)
            {
                long key = Key(vertices[i]);
                if (!ids.TryGetValue(key, out int id))
                {
                    id = points.Count;
                    ids[key] = id;
                    points.Add(vertices[i]);
                    pointNormals.Add(Vector3.zero);
                    pointWeights.Add(weights[i]);
                }
                weld[i] = id;
                pointNormals[id] += normals[i];
            }
            for (int p = 0; p < pointNormals.Count; p++) pointNormals[p] = pointNormals[p].normalized;

            // Which way front faces wind (Unity: the side Cross(b - a, c - a) points to, if the normals agree), and the
            // T-shirt's openings: edges of one triangle only.
            int frontVotes = 0;
            var edges = new Dictionary<long, int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                var n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                frontVotes += Vector3.Dot(n, normals[a] + normals[b] + normals[c]) >= 0f ? 1 : -1;
                Count(edges, weld[a], weld[b]);
                Count(edges, weld[b], weld[c]);
                Count(edges, weld[c], weld[a]);
            }
            bool crossIsFront = frontVotes >= 0;
            var collar = Around(edges, points, neck, 0.15f / scale, true);
            if (collar == null)
            {
                Plugin.Log.LogWarning($"InnerTorso: no collar found on {source.name}");
                return null;
            }

            // The torso: the T-shirt within reach of the collar but off its hem (which folds in), its sleeves left out,
            // moved inside it.
            var collarPoints = new Vector3[collar.Count];
            for (int i = 0; i < collar.Count; i++) collarPoints[i] = points[collar[i]];
            var kept = new bool[points.Count];
            var moved = new Vector3[points.Count];
            var distances = new float[points.Count];
            for (int p = 0; p < points.Count; p++)
            {
                float nearest = float.MaxValue;
                foreach (var q in collarPoints) nearest = Mathf.Min(nearest, (points[p] - q).sqrMagnitude);
                float distance = distances[p] = Mathf.Sqrt(nearest) * scale;
                int bone = pointWeights[p].boneIndex0;
                bool arm = bone < bones.Length && bones[bone] && bones[bone].name.Contains("Arm");
                kept[p] = distance >= Hem && distance <= Reach && !arm;
                float s = Mathf.Clamp01(distance / Ease);
                float depth = Edge + (Depth - Edge) * s * s * (3f - 2f * s);
                moved[p] = points[p] - pointNormals[p] * (depth / scale);
            }
            var outVertices = new List<Vector3>();
            var outWeights = new List<BoneWeight>();
            var index = new Dictionary<int, int>();
            var outTriangles = new List<int>();
            var torsoEdges = new Dictionary<long, int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = weld[triangles[t]], b = weld[triangles[t + 1]], c = weld[triangles[t + 2]];
                if (!kept[a] || !kept[b] || !kept[c] || a == b || b == c || c == a) continue;
                outTriangles.Add(Use(a, moved, pointWeights, index, outVertices, outWeights));
                outTriangles.Add(Use(b, moved, pointWeights, index, outVertices, outWeights));
                outTriangles.Add(Use(c, moved, pointWeights, index, outVertices, outWeights));
                Count(torsoEdges, a, b);
                Count(torsoEdges, b, c);
                Count(torsoEdges, c, a);
            }
            var middle = Vector3.zero;
            foreach (int p in collar) middle += moved[p];
            middle /= collar.Count;
            var inner = Around(torsoEdges, new List<Vector3>(moved), middle, float.MaxValue, false);
            if (inner == null)
            {
                Plugin.Log.LogWarning($"InnerTorso: {source.name}: no torso left around the neck");
                return null;
            }

            // Over the neck, one smooth surface: its heights a thin-plate spline through the torso near its edge, so
            // the torso's slopes carry on into it and meet in a low, rounded top, with neither a crater nor a bump.
            // Rings at even angles, from where the torso ends (A) evenly in to the middle.
            var platePoints = new List<Vector2>();
            var plateHeights = new List<float>();
            for (int p = 0; p < points.Count; p++)
                if (kept[p] && distances[p] <= Hem + Band)
                {
                    platePoints.Add(new Vector2(moved[p].x, moved[p].z) * scale); // metres: better conditioned
                    plateHeights.Add(moved[p].y * scale);
                }
            var plate = ThinPlate.Fit(platePoints, plateHeights);
            if (plate == null)
            {
                Plugin.Log.LogWarning($"InnerTorso: {source.name}: no surface over the neck");
                return null;
            }
            var a0 = new Vector3[Points];
            var aWeight = new BoneWeight[Points];
            var centre = Vector3.zero;
            for (int k = 0; k < Points; k++)
            {
                float theta = -Mathf.PI + 2f * Mathf.PI * k / Points;
                if (!AtAngle(inner, moved, pointNormals, pointWeights, middle, theta, false, out a0[k], out _, out aWeight[k]))
                {
                    Plugin.Log.LogWarning($"InnerTorso: {source.name}: the torso isn't all the way round the neck");
                    return null;
                }
                centre += a0[k];
            }
            centre /= Points;
            var neckWeight = new BoneWeight { boneIndex0 = neckBone, weight0 = 1f };

            // All wound the same way (flipped together at the end).
            var rings = new List<int[]>();
            foreach (float t in Rings)
            {
                var ring = new int[Points];
                for (int k = 0; k < Points; k++)
                {
                    var a = a0[k];
                    float x = a.x + (centre.x - a.x) * t, z = a.z + (centre.z - a.z) * t;
                    ring[k] = outVertices.Count;
                    outVertices.Add(new Vector3(x, plate.At(new Vector2(x, z) * scale) / scale, z));
                    outWeights.Add(Blend(aWeight[k], neckWeight, t));
                }
                rings.Add(ring);
            }
            int topIndex = outVertices.Count;
            outVertices.Add(new Vector3(centre.x, plate.At(new Vector2(centre.x, centre.z) * scale) / scale, centre.z));
            outWeights.Add(neckWeight);

            var made = new List<int>();
            var edge = new List<int>(inner);
            edge.Sort((x, y) => Turn(moved[x], middle).CompareTo(Turn(moved[y], middle)));
            int ne = edge.Count, ie = 0, jr = 0;
            var ringTurn = new float[Points];
            for (int k = 0; k < Points; k++) ringTurn[k] = 2f * Mathf.PI * k / Points;
            var edgeIndex = new int[ne];
            var edgeTurn = new float[ne];
            for (int i = 0; i < ne; i++)
            {
                edgeIndex[i] = Use(edge[i], moved, pointWeights, index, outVertices, outWeights);
                edgeTurn[i] = Turn(moved[edge[i]], middle);
            }
            var ring0 = rings[0];
            while (ie < ne || jr < Points) // the torso's edge zipped to the first ring, both in angle order
            {
                float nextEdge = ie + 1 < ne ? edgeTurn[ie + 1] : edgeTurn[0] + 2f * Mathf.PI;
                float nextRing = jr + 1 < Points ? ringTurn[jr + 1] : 2f * Mathf.PI;
                if (jr >= Points || (ie < ne && nextEdge <= nextRing))
                {
                    made.AddRange(new[] { edgeIndex[ie % ne], edgeIndex[(ie + 1) % ne], ring0[jr % Points] });
                    ie++;
                }
                else
                {
                    made.AddRange(new[] { edgeIndex[ie % ne], ring0[(jr + 1) % Points], ring0[jr % Points] });
                    jr++;
                }
            }
            for (int r = 1; r < rings.Count; r++)
            {
                var below = rings[r - 1];
                var ring = rings[r];
                for (int k = 0; k < Points; k++)
                {
                    int k1 = (k + 1) % Points;
                    made.AddRange(new[] { below[k], below[k1], ring[k1], below[k], ring[k1], ring[k] });
                }
            }
            var last = rings[rings.Count - 1];
            for (int k = 0; k < Points; k++)
                made.AddRange(new[] { last[k], last[(k + 1) % Points], topIndex });
            // Facing up, on the whole.
            float up = 0f;
            for (int i = 0; i + 2 < made.Count; i += 3)
                up += Vector3.Cross(outVertices[made[i + 1]] - outVertices[made[i]], outVertices[made[i + 2]] - outVertices[made[i]]).y;
            if ((up >= 0f) != crossIsFront)
                for (int i = 0; i + 2 < made.Count; i += 3)
                {
                    int swap = made[i];
                    made[i] = made[i + 1];
                    made[i + 1] = swap;
                }
            outTriangles.AddRange(made);

            var torso = new Mesh { name = "VrTweaks inner torso" };
            torso.vertices = outVertices.ToArray();
            torso.uv = new Vector2[outVertices.Count];
            var tangents = new Vector4[outVertices.Count];
            for (int i = 0; i < tangents.Length; i++) tangents[i] = new Vector4(1f, 0f, 0f, 1f);
            torso.tangents = tangents;
            torso.boneWeights = outWeights.ToArray();
            torso.bindposes = bindposes;
            torso.triangles = outTriangles.ToArray();
            torso.RecalculateNormals();
            torso.RecalculateBounds();
            Plugin.Log.LogInfo($"InnerTorso: made from {source.name}: {outVertices.Count} vertices, {outTriangles.Count / 3} triangles");
            return torso;
        }

        /// <summary>The angle around the neck, from straight left (-x), going the way the rings are made, in [0, 2Ď€).</summary>
        private static float Turn(Vector3 p, Vector3 middle)
        {
            float a = Mathf.Atan2(p.z - middle.z, p.x - middle.x) + Mathf.PI;
            return a >= 2f * Mathf.PI ? a - 2f * Mathf.PI : a;
        }

        /// <summary>
        /// A thin-plate spline: the smoothest surface (least bending) through given heights over a plane, as a thin
        /// metal sheet would lie bent through them.
        /// </summary>
        private sealed class ThinPlate
        {
            private Vector2[] _points;
            private double[] _weights;
            private double _a0, _ax, _ay;

            public static ThinPlate Fit(List<Vector2> points, List<float> heights)
            {
                int n = points.Count, size = n + 3;
                if (n < 3) return null;
                // [K P; P' 0] [w; a] = [h; 0], K(i, j) = U(|pi - pj|), P(i) = (1, x, y)
                var m = new double[size][]; // jagged: rows swap cheaply
                for (int i = 0; i < size; i++) m[i] = new double[size + 1];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++) m[i][j] = U((points[i] - points[j]).magnitude);
                    m[i][n] = m[n][i] = 1.0;
                    m[i][n + 1] = m[n + 1][i] = points[i].x;
                    m[i][n + 2] = m[n + 2][i] = points[i].y;
                    m[i][size] = heights[i];
                }
                // Gaussian elimination, partial pivoting.
                for (int c = 0; c < size; c++)
                {
                    int pivot = c;
                    for (int r = c + 1; r < size; r++)
                        if (System.Math.Abs(m[r][c]) > System.Math.Abs(m[pivot][c])) pivot = r;
                    if (System.Math.Abs(m[pivot][c]) < 1e-12) return null;
                    if (pivot != c)
                    {
                        var swap = m[c];
                        m[c] = m[pivot];
                        m[pivot] = swap;
                    }
                    for (int r = c + 1; r < size; r++)
                    {
                        double f = m[r][c] / m[c][c];
                        if (f == 0.0) continue;
                        for (int k = c; k <= size; k++) m[r][k] -= f * m[c][k];
                    }
                }
                var x = new double[size];
                for (int r = size - 1; r >= 0; r--)
                {
                    double sum = m[r][size];
                    for (int k = r + 1; k < size; k++) sum -= m[r][k] * x[k];
                    x[r] = sum / m[r][r];
                }
                var plate = new ThinPlate { _points = points.ToArray(), _weights = new double[n] };
                System.Array.Copy(x, plate._weights, n);
                plate._a0 = x[n];
                plate._ax = x[n + 1];
                plate._ay = x[n + 2];
                return plate;
            }

            public float At(Vector2 p)
            {
                double h = _a0 + _ax * p.x + _ay * p.y;
                for (int i = 0; i < _points.Length; i++) h += _weights[i] * U((p - _points[i]).magnitude);
                return (float)h;
            }

            private static double U(double r) => r > 0.0 ? r * r * System.Math.Log(r) : 0.0;
        }

        /// <summary>
        /// Where the line from the middle out at <paramref name="theta"/> (seen from above) crosses the loop: the highest
        /// crossing (<paramref name="highest"/>) or the one nearest the middle; its position, normal and bones there.
        /// </summary>
        private static bool AtAngle(List<int> loop, Vector3[] positions, List<Vector3> normals, List<BoneWeight> weights,
                                    Vector3 middle, float theta, bool highest, out Vector3 position, out Vector3 normal,
                                    out BoneWeight weight)
        {
            position = normal = Vector3.zero;
            weight = default;
            bool found = false;
            float best = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                int a = loop[i], b = loop[(i + 1) % loop.Count];
                float angleA = Mathf.Atan2(positions[a].z - middle.z, positions[a].x - middle.x);
                float angleB = Mathf.Atan2(positions[b].z - middle.z, positions[b].x - middle.x);
                float span = Mathf.DeltaAngle(angleA * Mathf.Rad2Deg, angleB * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (Mathf.Abs(span) < 1e-6f) continue;
                float t = Mathf.DeltaAngle(angleA * Mathf.Rad2Deg, theta * Mathf.Rad2Deg) * Mathf.Deg2Rad / span;
                if (t < 0f || t > 1f) continue;
                var p = Vector3.Lerp(positions[a], positions[b], t);
                float score = highest ? p.y : -new Vector2(p.x - middle.x, p.z - middle.z).magnitude;
                if (found && score <= best) continue;
                found = true;
                best = score;
                position = p;
                normal = Vector3.Lerp(normals[a], normals[b], t).normalized;
                weight = Blend(weights[a], weights[b], t);
            }
            return found;
        }

        private static int Use(int point, Vector3[] moved, List<BoneWeight> weights, Dictionary<int, int> index,
                               List<Vector3> vertices, List<BoneWeight> outWeights)
        {
            if (index.TryGetValue(point, out int i)) return i;
            i = vertices.Count;
            index[point] = i;
            vertices.Add(moved[point]);
            outWeights.Add(weights[point]);
            return i;
        }

        /// <summary>
        /// The opening (edges of one triangle only) that goes around <paramref name="centre"/> seen from above, in order:
        /// the first within <paramref name="band"/> of it in height (<paramref name="firstFound"/>), or the closest in.
        /// </summary>
        private static List<int> Around(Dictionary<long, int> edges, List<Vector3> points, Vector3 centre, float band, bool firstFound)
        {
            var links = new Dictionary<int, List<int>>();
            foreach (var e in edges)
            {
                if (e.Value != 1) continue;
                int a = (int)(e.Key >> 32), b = (int)(e.Key & 0xffffffffL);
                Link(links, a, b);
                Link(links, b, a);
            }
            var seen = new HashSet<int>();
            List<int> found = null;
            float foundRadius = 0f;
            foreach (int start in links.Keys)
            {
                if (seen.Contains(start)) continue;
                var loop = new List<int> { start };
                seen.Add(start);
                int prev = -1, cur = start;
                while (true)
                {
                    int next = -1;
                    foreach (int n in links[cur])
                        if (n != prev && !seen.Contains(n)) { next = n; break; }
                    if (next < 0) break;
                    prev = cur;
                    cur = next;
                    loop.Add(cur);
                    seen.Add(cur);
                }
                if (loop.Count < 6) continue;
                float sumY = 0f;
                foreach (int p in loop) sumY += points[p].y;
                if (Mathf.Abs(sumY / loop.Count - centre.y) > band) continue;
                bool inside = false;
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    var a = points[loop[i]];
                    var b = points[loop[j]];
                    if ((a.z > centre.z) != (b.z > centre.z) && centre.x < (b.x - a.x) * (centre.z - a.z) / (b.z - a.z) + a.x)
                        inside = !inside;
                }
                if (!inside) continue;
                if (firstFound) return loop;
                float radius = Radius(loop, points, centre);
                if (found == null || radius < foundRadius)
                {
                    found = loop;
                    foundRadius = radius;
                }
            }
            return found;
        }

        /// <summary>How far the loop is from <paramref name="centre"/> seen from above, on average.</summary>
        private static float Radius(List<int> loop, List<Vector3> points, Vector3 centre)
        {
            float sum = 0f;
            foreach (int p in loop) sum += new Vector2(points[p].x - centre.x, points[p].z - centre.z).magnitude;
            return sum / loop.Count;
        }

        private static BoneWeight Blend(BoneWeight a, BoneWeight b, float t)
        {
            var sums = new Dictionary<int, float>();
            Add(sums, a.boneIndex0, a.weight0 * (1f - t));
            Add(sums, a.boneIndex1, a.weight1 * (1f - t));
            Add(sums, a.boneIndex2, a.weight2 * (1f - t));
            Add(sums, a.boneIndex3, a.weight3 * (1f - t));
            Add(sums, b.boneIndex0, b.weight0 * t);
            Add(sums, b.boneIndex1, b.weight1 * t);
            Add(sums, b.boneIndex2, b.weight2 * t);
            Add(sums, b.boneIndex3, b.weight3 * t);
            var list = new List<KeyValuePair<int, float>>(sums);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            var w = new BoneWeight();
            float total = 0f;
            for (int i = 0; i < list.Count && i < 4; i++) total += list[i].Value;
            if (total <= 0f) return a;
            if (list.Count > 0) { w.boneIndex0 = list[0].Key; w.weight0 = list[0].Value / total; }
            if (list.Count > 1) { w.boneIndex1 = list[1].Key; w.weight1 = list[1].Value / total; }
            if (list.Count > 2) { w.boneIndex2 = list[2].Key; w.weight2 = list[2].Value / total; }
            if (list.Count > 3) { w.boneIndex3 = list[3].Key; w.weight3 = list[3].Value / total; }
            return w;
        }

        private static void Add(Dictionary<int, float> sums, int bone, float weight)
        {
            if (weight <= 0f) return;
            sums.TryGetValue(bone, out float sum);
            sums[bone] = sum + weight;
        }

        private static long Key(Vector3 v)
        {
            const int Offset = 1 << 20;
            long x = Mathf.RoundToInt(v.x * 10000f) + Offset, y = Mathf.RoundToInt(v.y * 10000f) + Offset,
                 z = Mathf.RoundToInt(v.z * 10000f) + Offset;
            return (x << 42) | (y << 21) | z;
        }

        private static void Count(Dictionary<long, int> edges, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            edges.TryGetValue(key, out int n);
            edges[key] = n + 1;
        }

        private static void Link(Dictionary<int, List<int>> links, int a, int b)
        {
            if (!links.TryGetValue(a, out var list)) links[a] = list = new List<int>();
            list.Add(b);
        }
    }
}
