using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// The underwater air bar (HudGui.AirReserve, with its low-air outline) is part of the flat-screen HUD, whose camera
    /// VR turns off, so in VR you never see how much air is left. Here the game's own bar is moved onto a small panel in
    /// front of the view, on the layer of the VR overlay camera that also draws the action prompts (it draws after the
    /// eye camera, so the underwater blur does not cover it). The game keeps filling and showing/hiding it as before.
    /// </summary>
    internal static class AirHud
    {
        public static float Distance = 0.8f, Height = -0.22f, Size = 0.0005f;

        private static GameObject _root;
        private static Transform _bar;
        private static bool _gaveUp;

        /// <summary>Called a few times per second; sets the panel up once per HUD/player (they are recreated on load).</summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled || !Scene.HudGui || !Scene.HudGui.AirReserve || !LocalPlayer.MainCamTr || !LocalPlayer.MainCam) return;
            var bar = Scene.HudGui.AirReserve.transform.parent;
            if (!bar) return;
            if (_root && bar == _bar && bar.parent == _root.transform)
            {
                bool show = PlayerPreferences.ShowHud;
                if (_root.activeSelf != show) _root.SetActive(show);
                return;
            }
            if (_gaveUp && bar == _bar) return;
            _bar = bar;
            _gaveUp = false;

            var overlay = FindOverlayCamera();
            if (!overlay)
            {
                _gaveUp = true;
                Plugin.Log.LogWarning("UnderwaterAirBar: no VR overlay camera found, air bar stays hidden");
                return;
            }
            int layer = LowestLayer(overlay.cullingMask);

            if (_root) Object.Destroy(_root);
            _root = new GameObject("VrFixes_AirHud") { layer = layer };
            var t = _root.transform;
            t.SetParent(LocalPlayer.MainCamTr, false);
            t.localPosition = new Vector3(0f, Height, Distance);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * Size;
            var panel = _root.AddComponent<UIPanel>();
            panel.depth = 1000;

            bar.SetParent(t, false);
            bar.localPosition = Vector3.zero;
            bar.localRotation = Quaternion.identity;
            foreach (var child in bar.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            foreach (var widget in bar.GetComponentsInChildren<UIWidget>(true)) widget.ParentHasChanged();
            Plugin.Debug($"air bar: moved to the VR overlay ('{overlay.name}', layer {layer}), {Distance} m ahead");
        }

        /// <summary>The camera under the VR eye camera that draws on top of it (depth-only clear, few layers).</summary>
        private static Camera FindOverlayCamera()
        {
            Camera best = null;
            foreach (var cam in LocalPlayer.MainCamTr.GetComponentsInChildren<Camera>(true))
            {
                if (cam == LocalPlayer.MainCam || !cam.enabled || cam.clearFlags != CameraClearFlags.Depth || cam.cullingMask == 0) continue;
                if (best == null || Bits(cam.cullingMask) < Bits(best.cullingMask)) best = cam;
            }
            return best;
        }

        private static int Bits(int mask)
        {
            int n = 0;
            for (; mask != 0; mask &= mask - 1) n++;
            return n;
        }

        private static int LowestLayer(int mask)
        {
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) return i;
            return 0;
        }
    }
}
