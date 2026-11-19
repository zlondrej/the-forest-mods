using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine.Events;

namespace VrTweaks
{
    /// <summary>
    /// VR text fields (multiplayer: server browser filter, join password, admin password, server name) open SteamVR's
    /// keyboard. The game takes the text only from per-character events and ends its "keyboard open" state only on
    /// SteamVR's KeyboardClosed event, which current SteamVR marks deprecated: it sends KeyboardDone when you press
    /// Done. Missing those, the field stayed empty and the keyboard never opened again (the game thought it still open).
    /// Here Done also ends it: the whole text is read from SteamVR, put in the field, and the game's own close runs.
    /// All by reflection: SteamVR's types exist only in the VR build (SteamVR.dll).
    /// </summary>
    internal static class VrKeyboardPatch
    {
        private static FieldInfo _active;
        private static MethodInfo _onClosed, _listen, _remove;
        private static object _doneEvent;
        private static Delegate _onDone;
        private static Func<string> _keyboardText;
        private static Action<string> _pending, _finalize;
        private static bool _listening;

        /// <summary>VR build only (CoopSteamNGUI.ShowVRKeyboard and SteamVR).</summary>
        public static bool Apply(Harmony harmony)
        {
            var show = AccessTools.Method(typeof(CoopSteamNGUI), "ShowVRKeyboard");
            _active = AccessTools.Field(typeof(CoopSteamNGUI), "_vrKeyboardActive");
            _onClosed = AccessTools.Method(typeof(CoopSteamNGUI), "OnKeyboardClosed");
            var events = Type.GetType("Valve.VR.SteamVR_Events, SteamVR", false);
            var eventType = Type.GetType("Valve.VR.EVREventType, SteamVR", false);
            var vrEvent = Type.GetType("Valve.VR.VREvent_t, SteamVR", false);
            var steamVr = Type.GetType("Valve.VR.SteamVR, SteamVR", false);
            if (show == null || _active == null || _onClosed == null || events == null || eventType == null || vrEvent == null || steamVr == null)
                return false;

            _doneEvent = events.GetMethod("System", new[] { eventType })
                .Invoke(null, new[] { Enum.Parse(eventType, "VREvent_KeyboardDone") });
            _listen = _doneEvent.GetType().GetMethod("Listen");
            _remove = _doneEvent.GetType().GetMethod("Remove");
            var onDone = typeof(VrKeyboardPatch).GetMethod(nameof(OnDone), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(vrEvent);
            _onDone = Delegate.CreateDelegate(typeof(UnityAction<>).MakeGenericType(vrEvent), onDone);

            var instance = steamVr.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            var overlay = steamVr.GetProperty("overlay", BindingFlags.Public | BindingFlags.Instance);
            var getText = overlay?.PropertyType.GetMethod("GetKeyboardText");
            if (_listen == null || _remove == null || instance == null || overlay == null || getText == null) return false;
            _keyboardText = () =>
            {
                var text = new StringBuilder(1024);
                getText.Invoke(overlay.GetValue(instance.GetValue(null, null), null), new object[] { text, 1024u });
                return text.ToString();
            };

            harmony.Patch(show, prefix: new HarmonyMethod(typeof(VrKeyboardPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(VrKeyboardPatch), nameof(Postfix)));
            return true;
        }

        private static bool Active() => (bool)_active.GetValue(null);

        private static void Prefix(Action<string> finalizeInputAction, out bool __state)
        {
            __state = Active();
            _pending = finalizeInputAction;
        }

        /// <summary>The keyboard opened for this field (not already open, not just closed): listen for Done.</summary>
        private static void Postfix(bool __state)
        {
            if (__state || !Active()) return;
            _finalize = _pending;
            if (_listening) return;
            _listen.Invoke(_doneEvent, new object[] { _onDone });
            _listening = true;
        }

        private static void OnDone<T>(T vrEvent)
        {
            try
            {
                var finalize = _finalize;
                _finalize = null;
                finalize?.Invoke(_keyboardText());
            }
            catch (Exception e) { Plugin.Log.LogWarning($"VrKeyboard: could not take the typed text. {e.Message}"); }
            _remove.Invoke(_doneEvent, new object[] { _onDone });
            _listening = false;
            if (Active()) _onClosed.Invoke(null, new object[] { vrEvent }); // the game's own close (flags, listeners)
        }
    }
}
