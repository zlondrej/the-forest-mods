using System.Collections.Generic;
using TheForest.UI;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>Arrow-type icons (<see cref="ArrowIconPainter"/>) as textures, made once per type.</summary>
    internal static class ArrowIconTextures
    {
        public const int Size = 152; // same as the game's arrow icon, so they fit where it is used
        private static readonly Dictionary<int, Texture2D> Textures = new Dictionary<int, Texture2D>();

        public static Texture2D For(ArrowKey key)
        {
            var arrow = key.Bonus == ArrowBonus.BoneAmmo || key.Base == ArrowBonus.BoneAmmo ? IconBase.Bone
                : key.Bonus == ArrowBonus.ModernAmmo || key.Base == ArrowBonus.ModernAmmo ? IconBase.Modern
                : IconBase.Wooden;
            var coating = key.Bonus == ArrowBonus.BurningAmmo ? IconCoating.Fire
                : key.Bonus == ArrowBonus.PoisonnedAmmo ? IconCoating.Poison
                : IconCoating.None;
            int id = (int)arrow * 3 + (int)coating;
            if (Textures.TryGetValue(id, out var cached)) return cached; // null after a failure: keep the game's icon
            Texture2D texture = null;
            try
            {
                var pixels = ArrowIconPainter.Paint(arrow, coating, Size);
                texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                {
                    name = $"ArrowFixes_{arrow}_{coating}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, true);
                Object.DontDestroyOnLoad(texture);
            }
            catch (System.Exception e)
            {
                // An icon must never break arrow switching or the basket: fall back to the game's icon.
                Plugin.Log.LogError($"Arrow icon {arrow}/{coating} could not be made, using the game's icon. {e}");
                if (texture) Object.Destroy(texture);
                texture = null;
            }
            Textures[id] = texture;
            return texture;
        }

        /// <summary>Show <paramref name="texture"/> on a HUD texture, remembering its own texture for <see cref="Restore"/>.</summary>
        public static void Swap(UITexture target, Texture texture, Dictionary<UITexture, Texture> originals)
        {
            if (!target) return;
            if (!originals.ContainsKey(target)) originals[target] = target.mainTexture;
            if (!texture) texture = originals[target];
            if (target.mainTexture != texture) target.mainTexture = texture;
        }

        public static void Restore(Dictionary<UITexture, Texture> originals)
        {
            foreach (var pair in originals)
                if (pair.Key && pair.Key.mainTexture != pair.Value) pair.Key.mainTexture = pair.Value;
        }
    }

    /// <summary>
    /// The arrow basket's put/take prompts each show the game's arrow icon (with a small down/up arrow); show the
    /// selected arrow type's icon there instead, and add a Rotate prompt (a copy of the take prompt without its icon,
    /// switched to the Rotate action, so the game draws the right key / controller button). Everything is part of the
    /// prompt widget, so it shows and hides with it; the game's icons come back when the basket works the vanilla way.
    /// </summary>
    internal static class BasketIcon
    {
        private static readonly Dictionary<UITexture, Texture> Originals = new Dictionary<UITexture, Texture>();
        private static ItemDualActionWidget _widget;
        private static UITexture _takeIcon, _putIcon;
        private static GameObject _rotatePrompt;

        public static void Show(ItemDualActionWidget widget, ArrowKey key)
        {
            if (!widget) return;
            if (_widget != widget) Setup(widget);
            var texture = ArrowIconTextures.For(key);
            ArrowIconTextures.Swap(_takeIcon, texture, Originals);
            ArrowIconTextures.Swap(_putIcon, texture, Originals);
            if (_rotatePrompt && !_rotatePrompt.activeSelf) _rotatePrompt.SetActive(true);
        }

        public static void Hide()
        {
            ArrowIconTextures.Restore(Originals);
            if (_rotatePrompt && _rotatePrompt.activeSelf) _rotatePrompt.SetActive(false);
        }

        private static void Setup(ItemDualActionWidget widget)
        {
            Hide();
            Originals.Clear();
            if (_rotatePrompt) Object.Destroy(_rotatePrompt);
            _widget = widget;
            var take = widget._takeActionIcon ? widget._takeActionIcon.transform : null;
            var put = widget._craftActionIcon ? widget._craftActionIcon.transform : null;
            _takeIcon = ArrowTexture(take);
            _putIcon = ArrowTexture(put);

            // Rotate prompt: a copy of the take prompt without its arrow icon, on the other side of the put prompt.
            if (take && put)
            {
                _rotatePrompt = Object.Instantiate(take.gameObject, widget.transform, false);
                _rotatePrompt.name = "ArrowFixes_RotatePrompt";
                _rotatePrompt.SetActive(false); // unregisters the copied Take action
                var copiedArrow = _rotatePrompt.transform.Find("Arrow");
                if (copiedArrow) Object.Destroy(copiedArrow.gameObject);
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
                _rotatePrompt.transform.localPosition = put.localPosition - (take.localPosition - put.localPosition);
                _rotatePrompt.SetActive(true); // registers Rotate
            }
            Dbg.Log($"basket: prompt icons found (take {(bool)_takeIcon}, put {(bool)_putIcon}, rotate {(bool)_rotatePrompt})");
        }

        /// <summary>The game's arrow icon of a prompt: its "Arrow" child (a texture).</summary>
        private static UITexture ArrowTexture(Transform prompt)
        {
            var arrow = prompt ? prompt.Find("Arrow") : null;
            return arrow ? arrow.GetComponent<UITexture>() : null;
        }

        private static void SetEnumField(object target, string field, string value)
        {
            var f = HarmonyLib.AccessTools.Field(target.GetType(), field);
            if (f != null && f.FieldType.IsEnum) f.SetValue(target, System.Enum.Parse(f.FieldType, value));
        }
    }
}
