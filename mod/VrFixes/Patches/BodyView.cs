using System.Collections.Generic;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrFixes
{
    /// <summary>
    /// The player's body in first-person scenes (theatre skipped). VR first person keeps the body meshes
    /// (VRPlayerAdapter.PlayerMeshesToDisable) as shadows only, and shows the VR hands instead of the animated arms; the
    /// theatre shows the body and arms for its screen. Here the body is shown the same way in first person, except the
    /// head (its meshes are on the "Camera" layer, which the eye camera doesn't draw, so they stay invisible either way),
    /// and everything is put back afterwards.
    /// </summary>
    internal static class BodyView
    {
        public static bool Enabled = true;

        private static readonly List<KeyValuePair<Renderer, ShadowCastingMode>> Changed = new List<KeyValuePair<Renderer, ShadowCastingMode>>();
        private static bool _shown, _armsWereActive, _handsWereActive;

        public static bool Shown => _shown;

        public static void Show()
        {
            if (_shown || !Enabled) return;
            var adapter = LocalPlayer.vrAdapter;
            if (!adapter || adapter.PlayerMeshesToDisable == null) return;
            int headLayer = LayerMask.NameToLayer("Camera");
            var skippedHead = new List<string>();
            foreach (var go in adapter.PlayerMeshesToDisable)
            {
                if (!go) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r || r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue; // only what VR hid
                    if (r == adapter.PlayerHead1 || r.gameObject.layer == headLayer || IsHead(r.name))
                    {
                        skippedHead.Add(r.name);
                        continue;
                    }
                    Changed.Add(new KeyValuePair<Renderer, ShadowCastingMode>(r, r.shadowCastingMode));
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
            }
            if (Plugin.Verbose)
            {
                Plugin.Debug("immersive camera: body meshes shown: " + string.Join(", ", Changed.ConvertAll(p => p.Key.name).ToArray()));
                Plugin.Debug("immersive camera: head meshes kept hidden: " + string.Join(", ", skippedHead.ToArray()));
            }
            if (adapter.defaultPlayerArms && adapter.VRPlayerHands)
            {
                _armsWereActive = adapter.defaultPlayerArms.gameObject.activeSelf;
                _handsWereActive = adapter.VRPlayerHands.gameObject.activeSelf;
                adapter.defaultPlayerArms.gameObject.SetActive(true);
                adapter.VRPlayerHands.gameObject.SetActive(false);
            }
            _shown = true;
            Plugin.Debug($"immersive camera: body shown ({Changed.Count} meshes, animated arms instead of VR hands)");
        }

        // Head pieces (the head itself, hoods, hair, beards, face details): they would block the view.
        private static readonly string[] HeadParts = { "head", "hair", "beard", "eyelash", "eyebrow", "eye", "teeth", "mask" };

        private static bool IsHead(string name)
        {
            var lower = name.ToLowerInvariant();
            foreach (var part in HeadParts)
                if (lower.Contains(part)) return true;
            return false;
        }

        public static void Restore()
        {
            if (!_shown) return;
            foreach (var pair in Changed)
                if (pair.Key) pair.Key.shadowCastingMode = pair.Value;
            Changed.Clear();
            var adapter = LocalPlayer.vrAdapter;
            if (adapter && adapter.defaultPlayerArms && adapter.VRPlayerHands)
            {
                adapter.defaultPlayerArms.gameObject.SetActive(_armsWereActive);
                adapter.VRPlayerHands.gameObject.SetActive(_handsWereActive);
            }
            _shown = false;
            Plugin.Debug("immersive camera: body hidden again");
        }
    }
}
