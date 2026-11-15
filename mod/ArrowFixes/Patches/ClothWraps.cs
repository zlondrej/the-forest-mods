using System.Collections.Generic;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// Generated cloth wraps (<see cref="ClothWrapBuilder"/>) for cloth-coated bone/modern arrows. A wrap is a child of
    /// the bone/modern model object, with an identity pose because the wrap is built in that model's mesh space; it uses
    /// the game's own cloth material.
    /// </summary>
    internal static class ClothWraps
    {
        private const string WrapName = "ArrowFixes_ClothWrap";
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        public static ClothWrapSpec SpecFor(ArrowBonus @base) =>
            @base == ArrowBonus.BoneAmmo ? ClothWrapSpecs.Bone :
            @base == ArrowBonus.ModernAmmo ? ClothWrapSpecs.Modern : null;

        /// <summary>Centre of the generated wrap for <paramref name="base"/> on <paramref name="model"/>, in world space.</summary>
        public static Vector3? Center(Transform model, ArrowBonus @base)
        {
            var spec = SpecFor(@base);
            if (!model || spec == null) return null;
            float y = (spec.Y0 + spec.Y1) / 2f;
            ClothWrapBuilder.Shaft(spec, y, out float cx, out float cz, out _, out _);
            return model.TransformPoint(new Vector3(cx, y, cz));
        }

        /// <summary>Show the wrap on <paramref name="model"/> (creating it if needed) or hide it.</summary>
        public static void Set(Transform model, ArrowBonus @base, Material clothMaterial, bool show)
        {
            if (!model) return;
            var existing = model.Find(WrapName);
            if (!show)
            {
                if (existing && existing.gameObject.activeSelf) existing.gameObject.SetActive(false);
                return;
            }
            var spec = SpecFor(@base);
            if (spec == null || !clothMaterial) return;
            var wrap = existing ? existing.gameObject : Create(model, spec, clothMaterial);
            var renderer = wrap.GetComponent<MeshRenderer>();
            if (renderer && renderer.sharedMaterial != clothMaterial) renderer.sharedMaterial = clothMaterial;
            if (!wrap.activeSelf) wrap.SetActive(true);
        }

        private static GameObject Create(Transform model, ClothWrapSpec spec, Material material)
        {
            var go = new GameObject(WrapName) { layer = model.gameObject.layer };
            go.transform.SetParent(model, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(spec);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var modelRenderer = model.GetComponent<Renderer>();
            if (modelRenderer)
            {
                renderer.shadowCastingMode = modelRenderer.shadowCastingMode;
                renderer.receiveShadows = modelRenderer.receiveShadows;
            }
            Dbg.Log($"wrap: created {spec.Name} on '{model.name}' (layer {go.layer})");
            return go;
        }

        private static Mesh MeshFor(ClothWrapSpec spec)
        {
            if (Meshes.TryGetValue(spec.Name, out var cached) && cached) return cached;
            var d = ClothWrapBuilder.Build(spec);
            int n = d.Positions.Length / 3;
            var vertices = new Vector3[n];
            var normals = new Vector3[n];
            var uvs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = new Vector3(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
                normals[i] = new Vector3(d.Normals[i * 3], d.Normals[i * 3 + 1], d.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(d.Uvs[i * 2], d.Uvs[i * 2 + 1]);
            }
            var mesh = new Mesh { name = spec.Name, vertices = vertices, normals = normals, uv = uvs, triangles = d.Triangles };
            mesh.RecalculateBounds();
            Object.DontDestroyOnLoad(mesh);
            Meshes[spec.Name] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------ bow

        /// <summary>Bow arrow: wrap on the bone/modern model when the current arrow is a cloth bone/modern arrow.</summary>
        public static void UpdateBow(BowController bow, InventoryItemView arrow)
        {
            var b = arrow ? ArrowUtil.GetBase(arrow.Properties) : ArrowUtil.NoBonus;
            bool cloth = arrow && arrow.ActiveBonus == ArrowBonus.BurningAmmo;
            var material = BowClothMaterial(bow);
            Set(bow._boneAmmoAnimationRenderer ? bow._boneAmmoAnimationRenderer.transform : null, ArrowBonus.BoneAmmo, material,
                cloth && b == ArrowBonus.BoneAmmo);
            Set(bow._modernAmmoAnimationRenderer ? bow._modernAmmoAnimationRenderer.transform : null, ArrowBonus.ModernAmmo, material,
                cloth && b == ArrowBonus.ModernAmmo);
        }

        private static Material BowClothMaterial(BowController bow)
        {
            foreach (var t in ArrowTipAlign.BowCloth(bow))
            {
                var r = t.GetComponentInChildren<Renderer>(true);
                if (r && r.sharedMaterial) return r.sharedMaterial;
            }
            return null;
        }

        // ------------------------------------------------------------ inventory

        /// <summary>
        /// Inventory / crafting-mat arrow: called from the view's cloth listener. The bone/modern model objects are
        /// switched on and off by their own listeners, so a wrap under them is only visible while its model is.
        /// </summary>
        public static void UpdateView(InventoryItemView view, ActiveBonusListener clothListener, ArrowBonus comboBase)
        {
            Material material = null;
            var targets = new List<Transform>();
            ArrowTipAlign.AddTargets(targets, clothListener._targetGOs, clothListener._targetObjects);
            foreach (var t in targets)
            {
                var r = t.GetComponentInChildren<Renderer>(true);
                if (r && r.sharedMaterial) { material = r.sharedMaterial; break; }
            }
            foreach (var @base in new[] { ArrowBonus.BoneAmmo, ArrowBonus.ModernAmmo })
                Set(ViewModel(view, @base), @base, material, @base == comboBase);
        }

        /// <summary>The object an inventory arrow view shows for <paramref name="bonus"/> ('BoneAmmo', 'ModernArrow').</summary>
        private static Transform ViewModel(InventoryItemView view, ArrowBonus bonus)
        {
            foreach (var l in view.GetComponentsInChildren<ActiveBonusListener>(true))
            {
                if (l._itemView != view || l._bonusToActivate != bonus || l._inverse) continue;
                var targets = new List<Transform>();
                ArrowTipAlign.AddTargets(targets, l._targetGOs, l._targetObjects);
                foreach (var t in targets)
                    if (t.GetComponent<MeshFilter>()) return t;
            }
            return null;
        }
    }
}
