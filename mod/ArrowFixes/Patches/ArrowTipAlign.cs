using System.Collections.Generic;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;
using Object = UnityEngine.Object;

namespace ArrowFixes
{
    /// <summary>
    /// The cloth wrap mesh and the fire effect are made for the wooden arrow model. On a bone/modern model the fire
    /// effect is moved along the arrow by (model tip - wooden tip), measured from the meshes; the cloth wrap (a single
    /// mesh shaped for the wooden arrow) is hidden instead.
    /// </summary>
    internal static class ArrowTipAlign
    {
        private static readonly Dictionary<Transform, Vector3> OriginalScale = new Dictionary<Transform, Vector3>();
        private static readonly Dictionary<int, List<Transform>> BowClothCache = new Dictionary<int, List<Transform>>();

        /// <summary>
        /// World-space shift that moves something placed for the wooden arrow onto the same spot of <paramref name="model"/>.
        /// <paramref name="nearTip"/> is any point on the tip half of the wooden arrow (used to tell tip from tail).
        /// </summary>
        public static Vector3 TipShift(Renderer wooden, Renderer model, Vector3 nearTip)
        {
            if (!wooden || !model || wooden == model) return Vector3.zero;
            if (!TryAxis(wooden, nearTip, out var axis)) return Vector3.zero;
            return axis * (MaxAlong(model, axis) - MaxAlong(wooden, axis));
        }

        /// <summary>
        /// Hide or show <paramref name="t"/> by scaling it to zero. Uses scale rather than SetActive / Renderer.enabled so
        /// it doesn't fight the game's ActiveBonusListener, which switches the same objects on and off.
        /// </summary>
        public static void SetHidden(Transform t, bool hidden)
        {
            if (!t) return;
            if (!OriginalScale.TryGetValue(t, out var original)) OriginalScale[t] = original = t.localScale;
            t.localScale = hidden ? Vector3.zero : original;
        }

        /// <summary>Cloth wrap objects of the arrow on the bow (switched by ActiveBonusListener(BurningAmmo)).</summary>
        public static List<Transform> BowCloth(BowController bow)
        {
            int key = bow.GetInstanceID();
            if (BowClothCache.TryGetValue(key, out var cached) && cached.TrueForAll(t => t)) return cached;

            var found = new List<Transform>();
            foreach (var root in new[] { bow.gameObject, bow._ammoAnimated })
            {
                if (!root) continue;
                foreach (var l in root.GetComponentsInChildren<ActiveBonusListener>(true))
                    if (l._bonusToActivate == ArrowBonus.BurningAmmo && !l._inverse) AddTargets(found, l._targetGOs, l._targetObjects);
            }
            if (found.Count == 0 && bow._ammoAnimated)
            {
                foreach (var r in bow._ammoAnimated.GetComponentsInChildren<Renderer>(true))
                    if (r.name.ToLowerInvariant().Contains("cloth") && !found.Contains(r.transform)) found.Add(r.transform);
            }
            Dbg.Log($"align: bow '{bow.name}' cloth objects: [{string.Join(", ", found.ConvertAll(t => t.name).ToArray())}]");
            BowClothCache[key] = found;
            return found;
        }

        public static void AddTargets(List<Transform> into, List<GameObject> gos, List<Object> objects)
        {
            if (gos != null)
                foreach (var go in gos)
                    if (go && !into.Contains(go.transform)) into.Add(go.transform);
            if (objects != null)
                foreach (var o in objects)
                    if (o is Component c && c && !into.Contains(c.transform)) into.Add(c.transform);
        }

        /// <summary>Unit vector along the wooden arrow's length (its longest mesh axis), pointing to the tip.</summary>
        private static bool TryAxis(Renderer wooden, Vector3 nearTip, out Vector3 axis)
        {
            axis = Vector3.zero;
            if (!TryLocalBounds(wooden, out var b)) return false;
            var tr = wooden.transform;
            float best = -1f;
            for (int k = 0; k < 3; k++)
            {
                var local = Vector3.zero;
                local[k] = b.size[k];
                var world = tr.TransformVector(local);
                if (world.sqrMagnitude > best)
                {
                    best = world.sqrMagnitude;
                    axis = world;
                }
            }
            if (best <= 1e-8f) return false;
            axis.Normalize();
            if (Vector3.Dot(nearTip - tr.TransformPoint(b.center), axis) < 0f) axis = -axis;
            return true;
        }

        /// <summary>Furthest extent of the renderer's mesh along <paramref name="axis"/> (world units).</summary>
        private static float MaxAlong(Renderer r, Vector3 axis)
        {
            float max = float.MinValue;
            if (TryLocalBounds(r, out var b))
            {
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    max = Mathf.Max(max, Vector3.Dot(r.transform.TransformPoint(corner), axis));
                }
            }
            else
            {
                var wb = r.bounds;
                max = Vector3.Dot(wb.center, axis) + Mathf.Abs(axis.x) * wb.extents.x + Mathf.Abs(axis.y) * wb.extents.y +
                      Mathf.Abs(axis.z) * wb.extents.z;
            }
            return max;
        }

        private static bool TryLocalBounds(Renderer r, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (r is SkinnedMeshRenderer smr)
            {
                if (!smr.sharedMesh) return false;
                bounds = smr.sharedMesh.bounds;
                return true;
            }
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) return false;
            bounds = mf.sharedMesh.bounds;
            return true;
        }
    }
}
