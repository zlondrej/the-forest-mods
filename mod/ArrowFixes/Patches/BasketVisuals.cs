using System.Collections.Generic;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// The arrows lying in an arrow basket are plain wooden arrow models (the same 'Arrow' mesh as the inventory arrow).
    /// Give each one the look of its slot's arrow type: bone/modern models, the cloth (game's wrap on wooden arrows,
    /// generated wraps on bone/modern) and the flaming/poison tint. The extra models are copied from the inventory
    /// arrow, with the same offsets they have there, so they line up with the wooden model they replace.
    /// </summary>
    internal static class BasketVisuals
    {
        private sealed class Part
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
        }

        private const string BoneName = "ArrowFixes_Bone", ModernName = "ArrowFixes_Modern", ClothName = "ArrowFixes_Cloth";
        private static Part _bone, _modern, _cloth;
        private static InventoryItemView _source;
        private static readonly HashSet<ItemHolder> Pending = new HashSet<ItemHolder>();

        /// <summary>Update the basket's arrows (later, if the inventory arrow to copy from isn't there yet).</summary>
        public static void Refresh(ItemHolder basket)
        {
            if (!basket) return;
            if (!Ready(basket._itemid)) { Pending.Add(basket); return; }
            for (int i = 0; i < basket.ItemsRender.Length; i++)
            {
                var render = basket.ItemsRender[i];
                if (render) Apply(render, i < basket.Items ? ArrowBasket.SlotKey(basket, i) : ArrowKey.Of(null));
            }
        }

        /// <summary>Called regularly (Plugin.Update): finish baskets that were waiting for the inventory.</summary>
        public static void Tick()
        {
            if (Pending.Count == 0 || !LocalPlayer.Inventory) return;
            var waiting = new List<ItemHolder>(Pending);
            Pending.Clear();
            foreach (var b in waiting) Refresh(b);
        }

        private static void Apply(GameObject render, ArrowKey key)
        {
            var wood = render.GetComponent<Renderer>();
            var @base = key.Bonus == ArrowBonus.BoneAmmo || key.Base == ArrowBonus.BoneAmmo ? ArrowBonus.BoneAmmo
                : key.Bonus == ArrowBonus.ModernAmmo || key.Base == ArrowBonus.ModernAmmo ? ArrowBonus.ModernAmmo
                : ArrowUtil.NoBonus;
            bool cloth = key.Bonus == ArrowBonus.BurningAmmo;
            Color? tint = ArrowUtil.IsCoating(key.Bonus) ? ArrowVisualPatches.TintFor(_source, key.Bonus) : (Color?)null;

            if (wood)
            {
                wood.enabled = @base == ArrowUtil.NoBonus;
                ArrowVisualPatches.SetTint(wood, @base == ArrowUtil.NoBonus ? tint : null);
            }
            var bone = Child(render, BoneName, _bone, @base == ArrowBonus.BoneAmmo);
            var modern = Child(render, ModernName, _modern, @base == ArrowBonus.ModernAmmo);
            Child(render, ClothName, _cloth, cloth && @base == ArrowUtil.NoBonus);
            var clothMaterial = _cloth.Materials.Length > 0 ? _cloth.Materials[0] : null;
            if (bone)
            {
                ArrowVisualPatches.SetTint(bone.GetComponent<Renderer>(), tint);
                ClothWraps.Set(bone.transform, ArrowBonus.BoneAmmo, clothMaterial, cloth);
            }
            if (modern)
            {
                ArrowVisualPatches.SetTint(modern.GetComponent<Renderer>(), tint);
                ClothWraps.Set(modern.transform, ArrowBonus.ModernAmmo, clothMaterial, cloth);
            }
        }

        /// <summary>The extra model under a basket arrow: shown/hidden, created only when first needed.</summary>
        private static GameObject Child(GameObject render, string name, Part part, bool show)
        {
            var t = render.transform.Find(name);
            if (!t)
            {
                if (!show || part == null) return null;
                var go = new GameObject(name) { layer = render.layer };
                t = go.transform;
                t.SetParent(render.transform, false);
                t.localPosition = part.Position;
                t.localRotation = part.Rotation;
                t.localScale = part.Scale;
                go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = part.Materials;
                var wood = render.GetComponent<Renderer>();
                if (wood) { r.shadowCastingMode = wood.shadowCastingMode; r.receiveShadows = wood.receiveShadows; }
            }
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            return show ? t.gameObject : null;
        }

        /// <summary>Copy the bone/modern/cloth parts of the inventory arrow (once).</summary>
        private static bool Ready(int arrowItemId)
        {
            if (_bone != null && _modern != null && _cloth != null) return true;
            var inventory = LocalPlayer.Inventory;
            if (!inventory || !inventory.InventoryItemViewsCache.TryGetValue(arrowItemId, out var views) || views == null || views.Count == 0)
                return false;
            var view = views[0];
            if (!view) return false;
            Part bone = null, modern = null, cloth = null;
            foreach (var l in view.GetComponentsInChildren<ActiveBonusListener>(true))
            {
                if (l._itemView != view || l._inverse) continue;
                var targets = new List<Transform>();
                ArrowTipAlign.AddTargets(targets, l._targetGOs, l._targetObjects);
                foreach (var t in targets)
                {
                    var part = Capture(view.transform, t);
                    if (part == null) continue;
                    if (l._bonusToActivate == ArrowBonus.BoneAmmo) bone = part;
                    else if (l._bonusToActivate == ArrowBonus.ModernAmmo) modern = part;
                    else if (l._bonusToActivate == ArrowBonus.BurningAmmo) cloth = part;
                }
            }
            if (bone == null || modern == null || cloth == null)
            {
                Dbg.Changed("basketParts", $"basket: inventory arrow parts not found (bone {bone != null}, modern {modern != null}, cloth {cloth != null})");
                return false;
            }
            _bone = bone; _modern = modern; _cloth = cloth; _source = view;
            Dbg.Log("basket: copied bone/modern/cloth models from the inventory arrow");
            return true;
        }

        private static Part Capture(Transform root, Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (!mf || !mf.sharedMesh || !mr || t.parent != root) return null; // the parts are direct children of the arrow
            return new Part
            {
                Mesh = mf.sharedMesh, Materials = mr.sharedMaterials,
                Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale,
            };
        }
    }
}
