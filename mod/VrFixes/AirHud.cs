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
            if (_sharpCamera.enabled == on) return;
            _sharpCamera.enabled = on;
            Plugin.Debug($"air bar: its camera {(on ? "on" : "off")}");
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
                if (!bar.gameObject.activeSelf)
                {
                    bar.gameObject.SetActive(true);
                    Plugin.Debug("air bar: its holder had been switched off, switched it back on");
                }
                Diagnose();
                return;
            }
            if (_gaveUp && bar == _bar && _setUpBlurred == Blurred) return;
            _bar = bar;
            _gaveUp = false;
            _setUpBlurred = Blurred;

            int layer;
            string drawnBy;
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
                drawnBy = $"VR overlay '{overlay.name}'";
            }
            else
            {
                layer = HudLayer();
                _sharpCamera = MakeSharpCamera(layer);
                drawnBy = $"own camera (depth {_sharpCamera.depth})";
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
            Plugin.Debug($"air bar: moved in front of the view, drawn by {drawnBy} on layer {layer}, {Distance} m ahead");
        }

        /// <summary>
        /// The layer for the sharp bar: "HUD" (8), the layer of the game's NGUI HUD, where the bar lives in vanilla. The
        /// rest of the HUD is far from the player (the cameras that draw it look at the HUD's own area), so the sharp
        /// camera next to the eyes should see only the bar. Logs the other active cameras that draw this layer too, in
        /// case one of them draws the bar again (blurred or doubled).
        /// </summary>
        private static int HudLayer()
        {
            int layer = LayerMask.NameToLayer("HUD");
            if (layer < 0) layer = 8;
            if (Plugin.Verbose)
            {
                var others = new System.Collections.Generic.List<string>();
                foreach (var cam in Camera.allCameras)
                    if (cam != _sharpCamera && (cam.cullingMask & (1 << layer)) != 0)
                        others.Add($"'{cam.name}' (depth {cam.depth}, {(cam.targetTexture ? "to a texture" : "to the screen")})");
                Plugin.Debug($"air bar: layer {layer} ('{LayerMask.LayerToName(layer)}') is also drawn by: " +
                             (others.Count > 0 ? string.Join(", ", others.ToArray()) : "no other active camera"));
            }
            return layer;
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

        // ------------------------------------------------------------------ diagnostics ([Debug] VerboseLogging)

        // A field in the flat-screen build, a property in VR: read it by name.
        internal static WaterViz WaterVizNow()
        {
            var t = HarmonyLib.Traverse.Create(typeof(LocalPlayer));
            var member = t.Property("WaterViz");
            if (!member.PropertyExists()) member = t.Field("WaterViz");
            return member.GetValue() as WaterViz;
        }

        private static int _dumpsThisDive;
        private static float _nextDump;

        /// <summary>While the bar is on (underwater), log what NGUI and the cameras do with it: a few times per dive.</summary>
        private static void Diagnose()
        {
            if (!Plugin.Verbose) return;
            var air = Scene.HudGui.AirReserve;
            var water = WaterVizNow();
            bool underwater = air.gameObject.activeSelf || (water && water.ScreenCoverage > 0.5f);
            if (!underwater)
            {
                _dumpsThisDive = 0;
                return;
            }
            if (_dumpsThisDive >= 3 || Time.unscaledTime < _nextDump) return;
            _dumpsThisDive++;
            _nextDump = Time.unscaledTime + 3f;

            var sb = new System.Text.StringBuilder("air bar state (underwater):");
            sb.Append($"\n  screen under water={(water ? water.ScreenCoverage : -1f):0.00}, bar on (self)={air.gameObject.activeSelf}, active chain:");
            for (var t = air.transform; t; t = t.parent) sb.Append($" {t.name}={(t.gameObject.activeSelf ? "on" : "OFF")}");
            var cam = LocalPlayer.MainCamTr;
            var rt = _root.transform;
            sb.Append($"\n  root active={_root.activeInHierarchy} ShowHud={PlayerPreferences.ShowHud} layer={_root.layer} local={rt.localPosition} scale={rt.localScale.x}" +
                      $" distance={Vector3.Distance(cam.position, rt.position):0.00}m angle={Vector3.Angle(cam.forward, rt.position - cam.position):0}°");
            foreach (var w in _bar.GetComponentsInChildren<UIWidget>(true))
            {
                sb.Append($"\n  widget '{w.name}' active={w.gameObject.activeInHierarchy} enabled={w.enabled} layer={w.gameObject.layer}" +
                          $" panel={(w.panel ? w.panel.name : "none")} visible={w.isVisible} alpha={w.finalAlpha:0.00} size={w.width}x{w.height}" +
                          $" pos={w.transform.position} drawCall={(w.drawCall ? w.drawCall.name : "none")}");
                if (w is UISprite s) sb.Append($" sprite='{s.spriteName}' fill={s.fillAmount:0.00} atlas={(s.atlas ? s.atlas.name : "none")}");
            }
            var panel = _root.GetComponent<UIPanel>();
            if (panel)
            {
                sb.Append($"\n  panel '{panel.name}' enabled={panel.enabled} alpha={panel.alpha:0.00} drawCalls={panel.drawCalls.Count}");
                foreach (var dc in panel.drawCalls)
                {
                    if (!dc) continue;
                    var r = dc.GetComponent<Renderer>();
                    sb.Append($"\n    drawCall '{dc.name}' active={dc.gameObject.activeInHierarchy} layer={dc.gameObject.layer}" +
                              $" renderer={(r ? r.enabled.ToString() : "none")} tris={dc.triangles} queue={dc.finalRenderQueue}" +
                              $" shader={(dc.dynamicMaterial ? dc.dynamicMaterial.shader.name : "none")} pos={dc.transform.position}" +
                              (r ? $" bounds={r.bounds.size}" : ""));
                }
            }
            foreach (var c in Camera.allCameras)
            {
                if ((c.cullingMask & (1 << _root.layer)) == 0) continue;
                sb.Append($"\n  camera '{c.name}' draws layer {_root.layer}: depth={c.depth} clear={c.clearFlags} near={c.nearClipPlane}" +
                          $" target={(c.targetTexture ? c.targetTexture.name : "screen")} eye={c.stereoTargetEye}");
            }
            Plugin.Debug(sb.ToString());
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
