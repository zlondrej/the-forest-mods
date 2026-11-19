using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// The underwater air bar (HudGui.AirReserve, with its low-air outline) is part of the flat-screen HUD, whose camera
    /// VR turns off, so in VR you never see how much air is left. Here the game's own bar is moved onto a small panel in
    /// front of the view. The game keeps filling and showing/hiding it as before.
    /// Blurred: drawn by the VR overlay camera that also draws the action prompts, which the underwater blur covers.
    /// Not blurred: drawn by an own camera after the eye camera (and its blur), on the HUD layer (see HudLayer).
    /// </summary>
    internal static class AirHud
    {
        public static float Distance = 0.25f, Height = -0.175f, Size = 0.00125f;
        public static bool Blurred = false;

        private static GameObject _root;
        private static Transform _bar;
        private static bool _gaveUp;
        private static bool _setUpBlurred;
        private static Camera _sharpCamera;

        /// <summary>
        /// Every frame: the sharp camera draws only while the bar shows (underwater, in the world view). It draws the
        /// whole HUD layer, and the VR pause menu puts your hands on that layer: drawn again by this camera, they sat off
        /// from where the eye camera draws them (after the plane intro the menu looked as if the head were higher).
        /// </summary>
        public static void UpdateCamera()
        {
            if (!_sharpCamera) return;
            var hud = Scene.HudGui;
            bool barShown = _root && _root.activeInHierarchy && hud &&
                            ((hud.AirReserve && hud.AirReserve.gameObject.activeInHierarchy) ||
                             (hud.AirReserveOutline && hud.AirReserveOutline.activeInHierarchy));
            bool world = LocalPlayer.Inventory && LocalPlayer.Inventory.CurrentView == PlayerInventory.PlayerViews.World;
            bool on = barShown && world;
            if (_sharpCamera.enabled != on) _sharpCamera.enabled = on;
        }

        /// <summary>Called a few times per second; sets the panel up once per HUD/player (they are recreated on load).</summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled || !Scene.HudGui || !Scene.HudGui.AirReserve || !LocalPlayer.MainCamTr || !LocalPlayer.MainCam) return;
            var bar = Scene.HudGui.AirReserve.transform.parent;
            if (!bar) return;
            if (_root && bar == _bar && bar.parent == _root.transform && _setUpBlurred == Blurred && (Blurred || _sharpCamera))
            {
                bool show = PlayerPreferences.ShowHud;
                if (_root.activeSelf != show) _root.SetActive(show);
                // The VR build switches flat-screen HUD parts off (VR switchers); keep the bar's holder on, the game
                // still shows/hides the bar itself.
                if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
                return;
            }
            if (_gaveUp && bar == _bar && _setUpBlurred == Blurred) return;
            _bar = bar;
            _gaveUp = false;
            _setUpBlurred = Blurred;

            int layer;
            if (Blurred)
            {
                if (_sharpCamera) Object.Destroy(_sharpCamera.gameObject);
                var overlay = FindOverlayCamera();
                if (!overlay)
                {
                    _gaveUp = true;
                    Plugin.Log.LogWarning("UnderwaterAirBar: no VR overlay camera found, air bar stays hidden");
                    return;
                }
                layer = LowestLayer(overlay.cullingMask);
            }
            else
            {
                layer = HudLayer();
                _sharpCamera = MakeSharpCamera(layer);
            }

            if (_root) Object.Destroy(_root); // the bar is moved off it below, before it is destroyed at frame end
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
        }

        /// <summary>
        /// The layer for the sharp bar: "HUD" (8), the layer of the game's NGUI HUD, where the bar lives in vanilla. The
        /// rest of the HUD is far from the player (the cameras that draw it look at the HUD's own area), so the sharp
        /// camera next to the eyes sees only the bar (and, in the pause menu, the hands: see UpdateCamera).
        /// </summary>
        private static int HudLayer()
        {
            int layer = LayerMask.NameToLayer("HUD");
            return layer < 0 ? 8 : layer;
        }

        /// <summary>
        /// A camera that draws only <paramref name="layer"/>, after the eye camera and its image effects (the underwater
        /// blur). It sits next to the eye camera, so the headset moves it the same way.
        /// </summary>
        private static Camera MakeSharpCamera(int layer)
        {
            var eye = LocalPlayer.MainCam;
            if (_sharpCamera) Object.Destroy(_sharpCamera.gameObject);
            var go = new GameObject("VrFixes_AirHudCamera");
            go.transform.SetParent(eye.transform.parent, false);
            go.transform.localPosition = eye.transform.localPosition;
            go.transform.localRotation = eye.transform.localRotation;
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Depth;
            cam.cullingMask = 1 << layer;
            cam.depth = eye.depth + 2f; // after the eye camera and its overlay (depth +0)
            cam.fieldOfView = eye.fieldOfView;
            cam.stereoTargetEye = eye.stereoTargetEye;
            cam.useOcclusionCulling = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = eye.farClipPlane;
            cam.enabled = false; // UpdateCamera turns it on while the bar shows
            return cam;
        }

        /// <summary>LocalPlayer.WaterViz: a field in the flat-screen build, a property in VR, so read by name.</summary>
        internal static WaterViz WaterVizNow()
        {
            var t = HarmonyLib.Traverse.Create(typeof(LocalPlayer));
            var member = t.Property("WaterViz");
            if (!member.PropertyExists()) member = t.Field("WaterViz");
            return member.GetValue() as WaterViz;
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
