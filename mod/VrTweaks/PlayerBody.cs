using System.Collections.Generic;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrTweaks
{
    /// <summary>
    /// VR draws the player's body only as a shadow (VRPlayerAdapter sets PlayerMeshesToDisable to shadows only), so
    /// looking down there is nothing below the floating VR hands. Here the body is drawn: torso and legs, clothes and
    /// armour, following the VR body as the game animates it. Left out: the head (it would block the view; its meshes
    /// are also on a layer the eye camera doesn't draw) and the bare arms and hands (the VR hands are drawn where your
    /// controllers are; the body's own would be a second pair). Sleeves stay, as part of the shirts and jackets.
    /// Armour (<see cref="Armor"/>) and the backpack (<see cref="Backpack"/>) can be left out, as they block the view
    /// looking down: they then only cast their shadow, as in vanilla.
    /// </summary>
    internal static class PlayerBody
    {
        public enum ArmorShown { None, Limbs, All }

        public static bool Visible = true;

        /// <summary>Which armour is drawn: none, only on arms and legs, or all (chest, shoulders and body too).</summary>
        public static ArmorShown Armor = ArmorShown.Limbs;

        /// <summary>Draw the backpack.</summary>
        public static bool Backpack = false;

        /// <summary>Metres the body sits further back behind your eyes than worked out (negative = forward).</summary>
        public static float BackOffset = 0f;

        private static VRPlayerAdapter _done;
        private static int _drawnWith = -1; // the Armor and Backpack settings the body is drawn with
        private static readonly List<Renderer> Shown = new List<Renderer>();

        /// <summary>A few times per second: once per player (it is recreated on load), draw the body; or undraw it.</summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled) return;
            int settings = (int)Armor * 2 + (Backpack ? 1 : 0);
            if (!Visible || settings != _drawnWith)
            {
                Hide(); // drawn again below with the new settings
                if (!Visible) return;
            }
            var adapter = LocalPlayer.vrAdapter;
            if (!adapter || adapter == _done || adapter.PlayerMeshesToDisable == null) return;
            _done = adapter;
            _drawnWith = settings;
            Shown.Clear();
            foreach (var go in adapter.PlayerMeshesToDisable)
            {
                if (!go) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r || r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
                    if (r == adapter.PlayerHead1 || BodyView.IsHead(r.name)) continue;
                    if (IsBackpack(r.name))
                    {
                        if (!Backpack) continue;
                    }
                    else if (IsArmor(r.transform))
                    {
                        if (Armor == ArmorShown.None || (Armor == ArmorShown.Limbs && !OnLimb(r.transform))) continue;
                    }
                    else if (IsArm(r.name)) continue;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    Shown.Add(r);
                }
            }
            Plugin.Log.LogInfo($"PlayerBody: {Shown.Count} body meshes drawn (armour: {Armor}, backpack: {Backpack})");
        }

        /// <summary>Back to vanilla: the body's meshes only cast their shadow.</summary>
        private static void Hide()
        {
            if (!_done) return;
            foreach (var r in Shown)
                if (r && r.shadowCastingMode == ShadowCastingMode.On)
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            Shown.Clear();
            _done = null;
            Plugin.Log.LogInfo("PlayerBody: body drawn as a shadow only");
        }

        private static bool IsBackpack(string name) => name.IndexOf("backpack", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Armour: "...Armor..."/"...Armour..." (bone, lizard, stealth, creepy armour) and the leaf armour's leaves and
        /// ferns, which hang from the bones ("leaves_04/fern_03").
        /// </summary>
        private static bool IsArmor(Transform t)
        {
            for (; t; t = t.parent)
            {
                var name = t.name.ToLowerInvariant();
                if (name.Contains("armor") || name.Contains("armour") || name.StartsWith("leaves") || name.StartsWith("fern")) return true;
                if (name.StartsWith("char_") || name == "clothingparent") return false; // up to the bone or the clothes group
            }
            return false;
        }

        /// <summary>
        /// Armour on an arm or a leg (not the chest, shoulders or body): by its name (".._L_Arm", "..TopLeg..") or the
        /// bone it hangs from (char_LeftForeArm, char_RightUpLeg...).
        /// </summary>
        private static bool OnLimb(Transform t)
        {
            if (IsLimbName(t.name.ToLowerInvariant().Replace("armor", "").Replace("armour", ""))) return true;
            for (var bone = t.parent; bone; bone = bone.parent)
            {
                var name = bone.name.ToLowerInvariant();
                if (!name.StartsWith("char_")) continue;
                return IsLimbName(name);
            }
            return false;
        }

        private static bool IsLimbName(string lower) =>
            lower.Contains("arm") || lower.Contains("hand") || lower.Contains("leg") || lower.Contains("foot");

        /// <summary>Bare arms and hands ("char_arms_GEO1", "...JustHands..."), and sleeves made for the VR arms.</summary>
        private static bool IsArm(string name)
        {
            var lower = name.ToLowerInvariant().Replace("armor", "");
            return lower.Contains("arm") || lower.Contains("hand");
        }

        // The game keeps your head around VRCameraOrigin, a point on the body (the body follows when you walk away
        // from it, and recentring puts your head right on it). Moving that point forward, and your view with it, puts
        // the body further back behind your eyes; the body itself, its animation and arms stay as the game makes them.
        private const float OffsetSpeed = 0.5f; // metres per second, easing in
        private static VRPlayerControl _vr;
        private static float _vanillaZ, _offset;

        /// <summary>Every frame.</summary>
        public static void UpdateOffset()
        {
            if (!ForestVR.Enabled) return;
            var vr = LocalPlayer.vrPlayerControl;
            if (!vr || !vr.VRCameraOrigin || !vr.VRCameraRig) return;
            var origin = vr.VRCameraOrigin;
            if (vr != _vr)
            {
                _vr = vr; // a new player: its origin is as the game set it
                _vanillaZ = origin.localPosition.z;
                _offset = 0f;
                _ahead = 0f;
                _aheadSamples = 0;
            }

            bool inScene = InScene();
            if (!inScene) MeasureAhead(vr, origin);
            float goal = Visible && _done && _aheadSamples >= AheadSamples ? BackOffset - Mathf.Max(0f, _ahead - EyesAheadOfSpine) : 0f;
            if (inScene)
                _offset = 0f; // the scene places the view itself (and some read the origin as the game set it)
            else if (_offset != goal)
            {
                // Eased, the view moving along so it doesn't jump.
                float next = Mathf.MoveTowards(_offset, goal, OffsetSpeed * Time.deltaTime);
                vr.VRCameraRig.position += Forward(origin) * (next - _offset);
                _offset = next;
            }

            var local = origin.localPosition; // the game changes its height (crouching), never its z
            float scale = origin.parent ? Mathf.Max(1e-3f, origin.parent.lossyScale.z) : 1f;
            float z = _vanillaZ + _offset / scale;
            if (!Mathf.Approximately(local.z, z)) origin.localPosition = new Vector3(local.x, local.y, z);
        }

        // The game's head point (VRCameraOrigin) sits well ahead of the body (about 25 cm ahead of the spine in the
        // player prefab), and the spine leaned the body forward to bring the head under your eyes. The spine now stays
        // upright (SpinePatch), so the body comes forward instead: by how far the head point is ahead of the spine,
        // less where your eyes are on an upright body (a little ahead of the spine). Measured per player, averaged
        // over its first frames outside scenes (the spine sways with the animation).
        private const float EyesAheadOfSpine = 0.06f;
        private const int AheadSamples = 60;
        private static float _ahead;
        private static int _aheadSamples;

        private static void MeasureAhead(VRPlayerControl vr, Transform origin)
        {
            if (_aheadSamples >= AheadSamples || vr.spineBones == null || vr.spineBones.Length == 0 || !vr.spineBones[0]) return;
            var forward = Forward(origin);
            var vanillaOrigin = origin.position - forward * _offset;
            float ahead = Vector3.Dot(vanillaOrigin - vr.spineBones[0].position, forward);
            _ahead += (ahead - _ahead) / ++_aheadSamples;
            if (_aheadSamples == AheadSamples)
                Plugin.Log.LogInfo($"PlayerBody: the game's head point is {_ahead:0.00} m ahead of the spine; the body comes {Mathf.Max(0f, _ahead - EyesAheadOfSpine):0.00} m forward, upright");
        }

        private static Vector3 Forward(Transform origin)
        {
            var forward = origin.parent ? origin.parent.forward : origin.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.zero;
        }

        /// <summary>Scenes (theatre mode or immersive camera) and the plane crash: the game or the scene places the view.</summary>
        private static bool InScene()
        {
            var head = LocalPlayer.CamFollowHead;
            return _vr.useGhostMode || (head && head.followAnim)
                   || (LocalPlayer.Inventory && LocalPlayer.Inventory.CurrentView == PlayerInventory.PlayerViews.PlaneCrash);
        }
    }
}
