using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace VrTweaks
{
    /// <summary>
    /// VR building placement.
    /// - Twisting the aiming controller (the one whose ray places the building, so the right hand right-handed, the
    ///   left one left-handed) around its pointing direction turns the building, like turning a knob: <see cref="Ratio"/>
    ///   building degrees per degree of twist (2: a quarter turn of the hand turns it half around). Twisting right turns
    ///   it clockwise seen from above. Only while the rotate button is held (<see cref="OnlyWhileRotateHeld"/>, the
    ///   stick's turning paused meanwhile; a double-headed arc arrow shows around the hand), or all the time on top of it.
    /// - The building keeps facing you the same way when you turn around in your room or aim around you
    ///   (<see cref="FacesPlayer"/>): vanilla VR kept its compass direction, as your body doesn't turn with you.
    /// - The building stays as far as you aim when you turn in your room. Vanilla searched for it along a line in front
    ///   of the body, which doesn't turn with you: aiming 90° to the side, the building came up to your feet until
    ///   the body turned or moved.
    /// </summary>
    internal static class BuildingRotationPatch
    {
        public static float Ratio = 2f;
        public static bool OnlyWhileRotateHeld = true;
        public static bool FacesPlayer = true;

        private static AccessTools.FieldRef<KeepAboveTerrain, float> _turn;
        private static System.Func<bool> _rotateHeld;
        private static int _lastFrame = -10, _bearingFrame = -10, _signFrame = -10;
        private static float _lastRoll, _lastBearing;

        /// <summary>VR build only (the flat-screen build turns buildings inside Update, without UpdateRotateInput).</summary>
        public static bool Apply(Harmony harmony)
        {
            var rotateInput = AccessTools.Method(typeof(KeepAboveTerrain), "UpdateRotateInput");
            var update = AccessTools.Method(typeof(KeepAboveTerrain), "Update");
            var input = typeof(TheForest.Utils.Input);
            var actions = AccessTools.Inner(input, "Actions");
            var rotate = actions == null ? null : AccessTools.Field(actions, "Rotate");
            var getButton = rotate == null ? null : AccessTools.Method(input, "GetButton", new[] { rotate.FieldType });
            if (rotateInput == null || update == null || getButton == null) return false;
            var action = rotate.GetValue(null);
            _rotateHeld = () => (bool)getButton.Invoke(null, new[] { action });
            _turn = AccessTools.FieldRefAccess<KeepAboveTerrain, float>("turn");
            harmony.Patch(rotateInput, postfix: new HarmonyMethod(typeof(BuildingRotationPatch), nameof(Postfix)));
            harmony.Patch(update, transpiler: new HarmonyMethod(typeof(BuildingRotationPatch), nameof(AimedLineTranspiler)));
            return true;
        }

        private static void Postfix(KeepAboveTerrain __instance)
        {
            if (!ForestVR.Enabled) return;
            var events = LocalPlayer.InventoryMouseEventsVR;
            var hand = events ? events.RayOverride : null;
            if (!hand) return;
            bool held = _rotateHeld();
            if (Ratio != 0f) Twist(__instance, hand, held);
            if (FacesPlayer) KeepFacing(__instance);
            if (held && Ratio != 0f && OnlyWhileRotateHeld) ShowSign(hand);
        }

        private static void Twist(KeepAboveTerrain placer, Transform hand, bool held)
        {
            bool active = !OnlyWhileRotateHeld || held;
            if (active && OnlyWhileRotateHeld) _turn(placer) = 0f; // the hand turns it now, not the stick
            bool hasRoll = Roll(hand, out float roll);
            // Start measuring afresh after a pause (rotate button released, placement stopped, a skipped frame, the
            // hand pointing straight up or down).
            if (active && hasRoll && _lastFrame == Time.frameCount - 1)
            {
                // A positive roll is counter-clockwise seen from behind the hand: twisting right (clockwise) turns
                // the building clockwise seen from above (positive yaw).
                _turn(placer) -= Ratio * Mathf.DeltaAngle(_lastRoll, roll);
            }
            _lastRoll = roll;
            _lastFrame = active && hasRoll ? Time.frameCount : -10;
        }

        /// <summary>
        /// The building turns as its direction from your eyes does (you turning, or aiming it around you), so it keeps
        /// facing you the same way. Not while it's locked in place, nor right at your feet (the direction is unsteady).
        /// </summary>
        private static void KeepFacing(KeepAboveTerrain placer)
        {
            var eyes = LocalPlayer.MainCamTr;
            var toBuilding = placer.transform.position - (eyes ? eyes.position : placer.transform.position);
            toBuilding.y = 0f;
            if (placer.Locked || toBuilding.sqrMagnitude < 0.5f * 0.5f)
            {
                _bearingFrame = -10;
                return;
            }
            float bearing = Mathf.Atan2(toBuilding.x, toBuilding.z) * Mathf.Rad2Deg;
            if (_bearingFrame == Time.frameCount - 1) _turn(placer) += Mathf.DeltaAngle(_lastBearing, bearing);
            _lastBearing = bearing;
            _bearingFrame = Time.frameCount;
        }

        /// <summary>
        /// The hand's roll: how far its up direction is turned around its pointing direction from the world's up (as
        /// an aircraft's roll). Where it points (aiming) doesn't change it, only twisting the wrist does. Undefined
        /// when pointing almost straight up or down.
        /// </summary>
        private static bool Roll(Transform hand, out float roll)
        {
            roll = 0f;
            var forward = hand.forward;
            var reference = Vector3.up - forward * Vector3.Dot(Vector3.up, forward); // world up, across the pointing
            if (reference.sqrMagnitude < 0.04f) return false; // pointing within ~12° of straight up or down
            var up = hand.up;
            roll = Mathf.Atan2(Vector3.Dot(Vector3.Cross(reference, up), forward), Vector3.Dot(reference, up)) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        /// KeepAboveTerrain.Update places the building where the aiming ray passes a vertical line in front of the
        /// player: transform.parent.TransformPoint(startLocalPosition). That point is replaced by <see cref="AimedLinePoint"/>.
        /// </summary>
        private static IEnumerable<CodeInstruction> AimedLineTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var start = AccessTools.Field(typeof(KeepAboveTerrain), "startLocalPosition");
            var transformPoint = AccessTools.Method(typeof(Transform), nameof(Transform.TransformPoint), new[] { typeof(Vector3) });
            var aimed = AccessTools.Method(typeof(BuildingRotationPatch), nameof(AimedLinePoint));
            var list = new List<CodeInstruction>(instructions);
            for (int i = 1; i < list.Count; i++)
            {
                if (!list[i - 1].LoadsField(start) || !list[i].Calls(transformPoint)) continue;
                list[i] = new CodeInstruction(OpCodes.Call, aimed).MoveLabelsFrom(list[i]);
                return list;
            }
            Plugin.Log.LogWarning("BuildingRotation: the building's distance line wasn't found, it stays as in vanilla");
            return list;
        }

        /// <summary>
        /// The line's point, turned around the player to the direction you aim in: as far from the player as vanilla
        /// has it in front of the body, but where you aim.
        /// </summary>
        private static Vector3 AimedLinePoint(Transform parent, Vector3 local)
        {
            var point = parent.TransformPoint(local);
            if (!ForestVR.Enabled) return point;
            var events = LocalPlayer.InventoryMouseEventsVR;
            var ray = events ? events.RayOverride : null;
            if (!ray) return point;
            var offset = point - parent.position;
            var aim = ray.forward;
            if (offset.x * offset.x + offset.z * offset.z < 1e-4f || aim.x * aim.x + aim.z * aim.z < 1e-4f) return point;
            float turn = Mathf.DeltaAngle(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg, Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg);
            return parent.position + Quaternion.AngleAxis(turn, Vector3.up) * offset;
        }

        // The twist sign: a chalk-drawn double-headed arc arrow around the aiming controller while the rotate button turns the
        // building by hand (a picture, nothing to translate). Rendered by scripts/chalk-icons.html (Icons/TwistArrow.png,
        // embedded), shown by a SpriteRenderer (its shader is always in the game).
        private const float ArcRadius = 0.34f; // of the picture's size, around ArcCentre: as in the script
        private static readonly Vector2 ArcCentre = new Vector2(0.5f, 0.42f); // the controller's axis, from the bottom left
        private const float ArcRadiusMetres = 0.09f, ArcAhead = 0.10f; // around the controller, ahead along its pointer
        private static GameObject _sign;
        private static bool _signMissing;

        private static void ShowSign(Transform hand)
        {
            if (!_sign && !MakeSign()) return;
            _signFrame = Time.frameCount;
            _sign.layer = hand.gameObject.layer;
            // Around the controller's pointing axis, over its top, turning with it like a knob's marking.
            _sign.transform.SetPositionAndRotation(hand.position + hand.forward * ArcAhead, Quaternion.LookRotation(hand.forward, hand.up));
            if (!_sign.activeSelf) _sign.SetActive(true);
        }

        /// <summary>Every frame: the sign goes when it wasn't shown this frame or the last (button released, placing over).</summary>
        public static void UpdateSign()
        {
            if (_sign && _sign.activeSelf && Time.frameCount - _signFrame > 1) _sign.SetActive(false);
        }

        private static bool MakeSign()
        {
            if (_signMissing) return false;
            const string name = "VrTweaks.Icons.TwistArrow.png";
            byte[] rgba;
            int w, h;
            using (var stream = typeof(BuildingRotationPatch).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    _signMissing = true;
                    Plugin.Log.LogWarning($"BuildingRotation: {name} missing from the plugin, no twist sign");
                    return false;
                }
                rgba = ArrowFixes.PngMask.ReadRgba(stream, out w, out h);
            }
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = ((h - 1 - y) * w + x) * 4; // the PNG's rows are top first, the texture's bottom first
                pixels[y * w + x] = new Color32(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { name = "VrTweaks_TwistSign", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float pixelsPerMetre = w * ArcRadius / ArcRadiusMetres;
            var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), ArcCentre, pixelsPerMetre);
            _sign = new GameObject("VrTweaks_TwistSign");
            Object.DontDestroyOnLoad(_sign);
            var renderer = _sign.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _sign.SetActive(false);
            return true;
        }
    }
}
