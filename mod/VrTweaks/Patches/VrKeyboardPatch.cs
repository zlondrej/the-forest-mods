using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine.Events;

namespace VrTweaks
{
    /// <summary>
    /// VR text fields (multiplayer: server browser filter, join password, admin password, server name) open SteamVR's
    /// keyboard. For each key, the game replaced the field with the text read back from SteamVR (GetKeyboardText),
    /// which holds only the latest character: each letter replaced the one before. Here the field is built from the
    /// typed characters themselves: each is added to the field, backspace removes the last one.
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
        private static bool _listening;

        // VREvent_t.data.keyboard.cNewInput0..7: the typed character(s), UTF-8, zero-padded.
        private static FieldInfo _data, _keyboard;
        private static FieldInfo[] _input;

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
            if (show == null || onChar == null || _active == null || _onClosed == null || events == null || eventType == null || vrEvent == null)
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
        /// Action&lt;string&gt; finalizeInputAction): the field's text plus the typed characters.
        /// </summary>
        private static bool OnCharPrefix(object[] __args)
        {
            if (!Active()) return false;
            var current = (__args[1] as Func<string>)?.Invoke() ?? "";
            var finalize = __args[2] as Action<string>;
            var text = new StringBuilder(current);
            foreach (char c in Typed(__args[0]))
            {
                if (c == '\b')
                {
                    if (text.Length > 0) text.Length--;
                }
                else if (c >= ' ') text.Append(c); // not Enter/Escape and other control keys
            }
            finalize?.Invoke(text.ToString());
            return false;
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

        private static void ShowPrefix(out bool __state) => __state = Active();

        /// <summary>The keyboard opened (it wasn't open, nor just closed): listen for Done.</summary>
        private static void ShowPostfix(bool __state)
        {
            if (__state || !Active() || _listening) return;
            _listen.Invoke(_doneEvent, new object[] { _onDone });
            _listening = true;
        }

        /// <summary>Done: the text is already in the field; end the keyboard as the game's KeyboardClosed would.</summary>
        private static void OnDone<T>(T vrEvent)
        {
            _remove.Invoke(_doneEvent, new object[] { _onDone });
            _listening = false;
            if (Active()) _onClosed.Invoke(null, new object[] { vrEvent });
        }
    }
}
