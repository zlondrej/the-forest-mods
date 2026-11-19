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
    /// </summary>
    internal static class PlayerBody
    {
        public static bool Visible = true;

        private static VRPlayerAdapter _done;

        /// <summary>A few times per second: once per player (it is recreated on load), draw the body.</summary>
        public static void Tick()
        {
            if (!Visible || !ForestVR.Enabled) return;
            var adapter = LocalPlayer.vrAdapter;
            if (!adapter || adapter == _done || adapter.PlayerMeshesToDisable == null) return;
            _done = adapter;
            int shown = 0;
            foreach (var go in adapter.PlayerMeshesToDisable)
            {
                if (!go) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r || r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
                    if (r == adapter.PlayerHead1 || BodyView.IsHead(r.name) || IsArm(r.name)) continue;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    shown++;
                }
            }
            Plugin.Log.LogInfo($"PlayerBody: {shown} body meshes drawn");
        }

        /// <summary>Bare arms and hands ("char_arms_GEO1", "...JustHands..."; also arm armour pieces), not "...Armor..." as such.</summary>
        private static bool IsArm(string name)
        {
            var lower = name.ToLowerInvariant().Replace("armor", "");
            return lower.Contains("arm") || lower.Contains("hand");
        }
    }
}
