using System.Collections.Generic;
using TheForest.UI;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// The selected arrow type's icon (<see cref="ArrowIconPainter"/>) above the arrow basket's take/put prompts, with a
    /// Rotate button prompt next to it (a copy of the take prompt switched to the Rotate action, so the game draws the
    /// right key / controller button). Both are children of the prompt widget, so they show and hide with it.
    /// </summary>
    internal static class BasketIcon
    {
        private const int Size = 152;
        private static readonly Dictionary<int, Texture2D> Textures = new Dictionary<int, Texture2D>();
        private static UITexture _icon;
        private static GameObject _rotatePrompt;

        public static void Show(ItemDualActionWidget widget, ArrowKey key)
        {
            if (!widget) return;
            if (!_icon || _icon.transform.parent != widget.transform) Create(widget);
            if (!_icon) return;
            var texture = TextureFor(key);
            if (_icon.mainTexture != texture) _icon.mainTexture = texture;
            if (!_icon.gameObject.activeSelf) _icon.gameObject.SetActive(true);
            if (_rotatePrompt && !_rotatePrompt.activeSelf) _rotatePrompt.SetActive(true);
        }

        public static void Hide()
        {
            if (_icon && _icon.gameObject.activeSelf) _icon.gameObject.SetActive(false);
            if (_rotatePrompt && _rotatePrompt.activeSelf) _rotatePrompt.SetActive(false);
        }

        private static void Create(ItemDualActionWidget widget)
        {
            if (_icon) Object.Destroy(_icon.gameObject);
            if (_rotatePrompt) Object.Destroy(_rotatePrompt);
            var take = widget._takeActionIcon ? widget._takeActionIcon.transform : null;
            var put = widget._craftActionIcon ? widget._craftActionIcon.transform : null;
            var reference = take ? take.GetComponent<UIWidget>() : null;
            float promptSize = Mathf.Max(24, reference ? reference.height : 24);
            Vector3 takePos = take ? take.localPosition : Vector3.zero, putPos = put ? put.localPosition : takePos;
            // About as wide as the gap between the two prompts, centred above them.
            float gap = Mathf.Abs(takePos.x - putPos.x);
            int size = Mathf.RoundToInt(Mathf.Max(promptSize * 3f, gap * 0.9f));
            var centre = (takePos + putPos) / 2f + new Vector3(0f, promptSize * 0.6f + size * 0.5f, 0f);

            var go = new GameObject("ArrowFixes_ArrowTypeIcon") { layer = widget.gameObject.layer };
            go.transform.SetParent(widget.transform, false);
            go.transform.localPosition = centre;
            _icon = go.AddComponent<UITexture>();
            var shader = Shader.Find("Unlit/Transparent Colored");
            if (shader) _icon.shader = shader;
            _icon.width = size;
            _icon.height = size;
            _icon.depth = (reference ? reference.depth : 0) + 5;

            // Rotate prompt left of the icon: a copy of the take prompt, re-registered for the Rotate action.
            if (take)
            {
                _rotatePrompt = Object.Instantiate(take.gameObject, widget.transform, false);
                _rotatePrompt.name = "ArrowFixes_RotatePrompt";
                _rotatePrompt.SetActive(false); // unregisters the copied Take action
                var action = _rotatePrompt.GetComponent<ActionIconUISprite>();
                if (action)
                {
                    // The action field's enum differs per build (flat: InputMappingIcons.Actions; VR: Input.ActionId,
                    // plus a separate _vrAction), so set "Rotate" by name.
                    SetEnumField(action, "_action", "Rotate");
                    SetEnumField(action, "_vrAction", "Rotate");
                    action._sideIcon = ActionIcon.SideIconTypes.None;
                    action._actionTextOverride = null;
                }
                _rotatePrompt.transform.localPosition = centre - new Vector3(size * 0.5f + promptSize, 0f, 0f);
                _rotatePrompt.SetActive(true); // registers Rotate
            }
            Dbg.Log($"basket: icon created on '{widget.name}' ({size}px, prompts {takePos} / {putPos}, layer {go.layer})");
        }

        private static void SetEnumField(object target, string field, string value)
        {
            var f = HarmonyLib.AccessTools.Field(target.GetType(), field);
            if (f != null && f.FieldType.IsEnum) f.SetValue(target, System.Enum.Parse(f.FieldType, value));
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
