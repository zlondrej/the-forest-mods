using System.Collections.Generic;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;
using Object = UnityEngine.Object;

namespace ArrowFixes
{
    /// <summary>
    /// Helpers for the game's cloth wrap on the bow's wooden arrow, which is made for the wooden model: it is hidden on
    /// bone/modern models (they get a generated wrap), and its centre is where the game puts the fire effect.
    /// </summary>
    internal static class ArrowTipAlign
    {
        private static readonly Dictionary<Transform, Vector3> OriginalScale = new Dictionary<Transform, Vector3>();
        private static readonly Dictionary<int, List<Transform>> BowClothCache = new Dictionary<int, List<Transform>>();

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

        /// <summary>
        /// World position of a point given in <paramref name="t"/>'s local space, ignoring a scale-to-zero by
        /// <see cref="SetHidden"/> (so it still works while the cloth is hidden).
        /// </summary>
        public static Vector3 UnhiddenPoint(Transform t, Vector3 local)
        {
            var scale = OriginalScale.TryGetValue(t, out var original) ? original : t.localScale;
            var inParent = t.localPosition + t.localRotation * Vector3.Scale(scale, local);
            return t.parent ? t.parent.TransformPoint(inParent) : inParent;
        }

        /// <summary>Centre of the game's wooden cloth wrap on the bow, in world space (null if not found).</summary>
        public static Vector3? WoodenClothCenter(BowController bow)
        {
            foreach (var t in BowCloth(bow))
            {
                var mf = t.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh) return UnhiddenPoint(t, mf.sharedMesh.bounds.center);
            }
            return null;
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
    }
}
