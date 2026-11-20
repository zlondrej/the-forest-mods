using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine.Events;

namespace VrTweaks
{
    /// <summary>
    /// VR text fields (multiplayer: server browser filter, join password, admin password, server name) open SteamVR's
    /// keyboard, and the game updates the field on every key event. It replaced the field with the text read back from
    /// SteamVR (GetKeyboardText), which current SteamVR answers with only the latest character: each letter replaced
    /// the one before. The key events' own characters may also be missing, depending on the SteamVR version. Here each
    /// key event updates the field as you type:
    /// - if SteamVR's text proves to be the whole text (two or more characters), the field takes it, as vanilla meant;
    /// - else the event's character (or, missing, SteamVR's single character) is added; backspace removes the last one.
    /// Also, the game ends its "keyboard open" state only on SteamVR's KeyboardClosed event, which current SteamVR marks
    /// deprecated (Done sends KeyboardDone): Done runs the game's own close too, so the keyboard can open again.
    /// All by reflection: SteamVR's types exist only in the VR build (SteamVR.dll).
    /// </summary>
    internal static class VrKeyboardPatch
    {
        private static FieldInfo _active;
        private static MethodInfo _onClosed, _listen, _remove;
        private static object _doneEvent;
        private static Delegate _onDone;
        private static Func<string> _keyboardText;
        private static bool _listening;

        // VREvent_t.data.keyboard.cNewInput0..7: the typed character(s), UTF-8, zero-padded.
        private static FieldInfo _data, _keyboard;
        private static FieldInfo[] _input;

        // The open keyboard: its field's setter, and what the key events carried (for the log line on Done).
        private static Action<string> _finalize;
        private static bool _wholeText;
        private static int _events, _fromEvent, _fromSteamVr;

        /// <summary>VR build only (CoopSteamNGUI's VR keyboard and SteamVR).</summary>
        public static bool Apply(Harmony harmony)
        {
            var show = AccessTools.Method(typeof(CoopSteamNGUI), "ShowVRKeyboard");
            var onChar = AccessTools.Method(typeof(CoopSteamNGUI), "OnVRKeyboard");
            _active = AccessTools.Field(typeof(CoopSteamNGUI), "_vrKeyboardActive");
            _onClosed = AccessTools.Method(typeof(CoopSteamNGUI), "OnKeyboardClosed");
            var events = Type.GetType("Valve.VR.SteamVR_Events, SteamVR", false);
            var eventType = Type.GetType("Valve.VR.EVREventType, SteamVR", false);
            var vrEvent = Type.GetType("Valve.VR.VREvent_t, SteamVR", false);
            var steamVr = Type.GetType("Valve.VR.SteamVR, SteamVR", false);
            if (show == null || onChar == null || _active == null || _onClosed == null || events == null || eventType == null
                || vrEvent == null || steamVr == null)
                return false;

            _data = vrEvent.GetField("data");
            _keyboard = _data?.FieldType.GetField("keyboard");
            if (_keyboard == null) return false;
            _input = new FieldInfo[8];
            for (int i = 0; i < 8; i++)
            {
                _input[i] = _keyboard.FieldType.GetField("cNewInput" + i);
                if (_input[i] == null) return false;
            }

            var instance = steamVr.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            var overlay = steamVr.GetProperty("overlay", BindingFlags.Public | BindingFlags.Instance);
            var getText = overlay?.PropertyType.GetMethod("GetKeyboardText");
            if (instance == null || getText == null) return false;
            _keyboardText = () =>
            {
                var text = new StringBuilder(1024);
                getText.Invoke(overlay.GetValue(instance.GetValue(null, null), null), new object[] { text, 1024u });
                return text.ToString();
            };

            _doneEvent = events.GetMethod("System", new[] { eventType })
                .Invoke(null, new[] { Enum.Parse(eventType, "VREvent_KeyboardDone") });
            _listen = _doneEvent.GetType().GetMethod("Listen");
            _remove = _doneEvent.GetType().GetMethod("Remove");
            if (_listen == null || _remove == null) return false;
            var onDone = typeof(VrKeyboardPatch).GetMethod(nameof(OnDone), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(vrEvent);
            _onDone = Delegate.CreateDelegate(typeof(UnityAction<>).MakeGenericType(vrEvent), onDone);

            harmony.Patch(onChar, prefix: new HarmonyMethod(typeof(VrKeyboardPatch), nameof(OnCharPrefix)));
            harmony.Patch(show, prefix: new HarmonyMethod(typeof(VrKeyboardPatch), nameof(ShowPrefix)),
                postfix: new HarmonyMethod(typeof(VrKeyboardPatch), nameof(ShowPostfix)));
            return true;
        }

        private static bool Active() => (bool)_active.GetValue(null);

        /// <summary>
        /// Replaces the game's OnVRKeyboard(VREvent_t args, Func&lt;string&gt; getCurrentStringAction,
        /// Action&lt;string&gt; finalizeInputAction): updates the field for one key event.
        /// </summary>
        private static bool OnCharPrefix(object[] __args)
        {
            if (!Active()) return false;
            try
            {
                var current = (__args[1] as Func<string>)?.Invoke() ?? "";
                var finalize = __args[2] as Action<string>;
                string typed = Typed(__args[0]);
                string steamVr = KeyboardText();
                _events++;
                if (typed.Length > 0) _fromEvent++;
                if (!string.IsNullOrEmpty(steamVr)) _fromSteamVr++;

                string text;
                if (steamVr != null && (_wholeText || steamVr.Length >= 2))
                {
                    _wholeText = true; // SteamVR keeps the whole text: take it as it is (edits included)
                    text = steamVr;
                }
                else text = Edit(current, typed.Length > 0 ? typed : steamVr ?? "");
                if (text != current) finalize?.Invoke(text);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"VrKeyboard: {e.Message}"); }
            return false;
        }

        /// <summary>The text with the typed characters added; backspace removes the last one, other control keys none.</summary>
        private static string Edit(string text, string typed)
        {
            var result = new StringBuilder(text);
            foreach (char c in typed)
            {
                if (c == '\b')
                {
                    if (result.Length > 0) result.Length--;
                }
                else if (c >= ' ') result.Append(c);
            }
            return result.ToString();
        }

        private static string Typed(object vrEvent)
        {
            var keyboard = _keyboard.GetValue(_data.GetValue(vrEvent));
            var bytes = new byte[8];
            int n = 0;
            while (n < 8)
            {
                bytes[n] = Convert.ToByte(_input[n].GetValue(keyboard));
                if (bytes[n] == 0) break;
                n++;
            }
            return Encoding.UTF8.GetString(bytes, 0, n);
        }

        private static string KeyboardText()
        {
            try { return _keyboardText(); }
            catch { return null; }
        }

        private static void ShowPrefix(Action<string> finalizeInputAction, out bool __state)
        {
            __state = Active();
            if (!__state) _finalize = finalizeInputAction;
        }

        /// <summary>The keyboard opened (it wasn't open, nor just closed): a fresh field; listen for Done.</summary>
        private static void ShowPostfix(bool __state)
        {
            if (__state || !Active()) return;
            _wholeText = false;
            _events = _fromEvent = _fromSteamVr = 0;
            if (_listening) return;
            _listen.Invoke(_doneEvent, new object[] { _onDone });
            _listening = true;
        }

        /// <summary>Done: the text is already in the field; end the keyboard as the game's KeyboardClosed would.</summary>
        private static void OnDone<T>(T vrEvent)
        {
            _remove.Invoke(_doneEvent, new object[] { _onDone });
            _listening = false;
            if (!Active()) return;
            // No key events at all: SteamVR's text is all there is.
            var text = _events == 0 ? KeyboardText() : null;
            if (!string.IsNullOrEmpty(text)) _finalize?.Invoke(text);
            Plugin.Log.LogInfo($"VrKeyboard: done after {_events} key events ({_fromEvent} with a character, " +
                               $"{_fromSteamVr} with SteamVR text, whole text: {_wholeText})");
            _finalize = null;
            _onClosed.Invoke(null, new object[] { vrEvent });
        }
    }
}
