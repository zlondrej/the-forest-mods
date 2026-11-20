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
    /// The body can sit further back behind your eyes (<see cref="BackOffset"/>), so the chest isn't in front of them.
    /// </summary>
    internal static class PlayerBody
    {
        public static bool Visible = true;

        /// <summary>Metres the body sits further back behind your eyes than in vanilla (negative = forward).</summary>
        public static float BackOffset = 0.15f;

        private static VRPlayerAdapter _done;
        private static readonly List<Renderer> Shown = new List<Renderer>();

        /// <summary>A few times per second: once per player (it is recreated on load), draw the body; or undraw it.</summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled) return;
            if (!Visible)
            {
                Hide();
                return;
            }
            var adapter = LocalPlayer.vrAdapter;
            if (!adapter || adapter == _done || adapter.PlayerMeshesToDisable == null) return;
            _done = adapter;
            Shown.Clear();
            foreach (var go in adapter.PlayerMeshesToDisable)
            {
                if (!go) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r || r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
                    if (r == adapter.PlayerHead1 || BodyView.IsHead(r.name) || IsArm(r.name)) continue;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    Shown.Add(r);
                }
            }
            Plugin.Log.LogInfo($"PlayerBody: {Shown.Count} body meshes drawn");
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

        /// <summary>Bare arms and hands ("char_arms_GEO1", "...JustHands..."; also arm armour pieces), not "...Armor..." as such.</summary>
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
            }

            float goal = Visible && _done ? BackOffset : 0f;
            if (InScene())
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
