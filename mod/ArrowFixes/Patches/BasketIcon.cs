using System.Collections.Generic;
using TheForest.UI;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// The selected arrow type's icon (<see cref="ArrowIconPainter"/>) next to the arrow basket's take/put prompt. It is a
    /// child of the prompt widget, so it shows and hides with it.
    /// </summary>
    internal static class BasketIcon
    {
        private const int Size = 152;
        private static readonly Dictionary<int, Texture2D> Textures = new Dictionary<int, Texture2D>();
        private static UITexture _icon;

        public static void Show(ItemDualActionWidget widget, ArrowKey key)
        {
            if (!widget) return;
            if (!_icon || _icon.transform.parent != widget.transform) Create(widget);
            if (!_icon) return;
            var texture = TextureFor(key);
            if (_icon.mainTexture != texture) _icon.mainTexture = texture;
            if (!_icon.gameObject.activeSelf) _icon.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (_icon && _icon.gameObject.activeSelf) _icon.gameObject.SetActive(false);
        }

        private static void Create(ItemDualActionWidget widget)
        {
            if (_icon) Object.Destroy(_icon.gameObject);
            var reference = widget._takeActionIcon ? widget._takeActionIcon.GetComponent<UIWidget>() : null;
            int size = Mathf.Max(48, reference ? reference.height : 48) * 3 / 2;
            var go = new GameObject("ArrowFixes_ArrowTypeIcon") { layer = widget.gameObject.layer };
            go.transform.SetParent(widget.transform, false);
            // Left of the take icon.
            var anchor = widget._takeActionIcon ? widget._takeActionIcon.transform.localPosition : Vector3.zero;
            go.transform.localPosition = anchor + new Vector3(-size * 1.1f, 0f, 0f);
            _icon = go.AddComponent<UITexture>();
            var shader = Shader.Find("Unlit/Transparent Colored");
            if (shader) _icon.shader = shader;
            _icon.width = size;
            _icon.height = size;
            _icon.depth = (reference ? reference.depth : 0) + 5;
            Dbg.Log($"basket: icon created on '{widget.name}' ({size}px, layer {go.layer})");
        }

        private static Texture2D TextureFor(ArrowKey key)
        {
            var arrow = key.Bonus == ArrowBonus.BoneAmmo || key.Base == ArrowBonus.BoneAmmo ? IconBase.Bone
                : key.Bonus == ArrowBonus.ModernAmmo || key.Base == ArrowBonus.ModernAmmo ? IconBase.Modern
                : IconBase.Wooden;
            var coating = key.Bonus == ArrowBonus.BurningAmmo ? IconCoating.Fire
                : key.Bonus == ArrowBonus.PoisonnedAmmo ? IconCoating.Poison
                : IconCoating.None;
            int id = (int)arrow * 3 + (int)coating;
            if (Textures.TryGetValue(id, out var cached) && cached) return cached;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = $"ArrowFixes_{arrow}_{coating}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            texture.LoadRawTextureData(ArrowIconPainter.Paint(arrow, coating, Size));
            texture.Apply(false, true);
            Object.DontDestroyOnLoad(texture);
            Textures[id] = texture;
            return texture;
        }
    }
}
