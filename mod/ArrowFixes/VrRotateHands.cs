using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheForest.Utils;
using UnityEngine;

namespace ArrowFixes
{
    /// <summary>
    /// VR: the hands whose controller has the Rotate button bound, as the game's own controller button hints find them
    /// (ControllerInputHintManager: the SteamVR action's GetActive(the hand's input source)). Each of the game's two
    /// hand objects (Valve's Hand) follows the controller of its handType, which mirrored handedness swaps. All by
    /// reflection: these exist only in the VR build (SteamVR.dll, Valve's InteractionSystem).
    /// </summary>
    internal static class VrRotateHands
    {
        private static bool _setUp, _usable;
        private static MethodInfo _fromPath, _getActive;
        private static readonly List<object> RotateActions = new List<object>();
        private static readonly List<Transform> Result = new List<Transform>();

        /// <summary>The hands with Rotate bound (empty when unknown: not VR, nothing bound, SteamVR not ready).</summary>
        public static List<Transform> Find()
        {
            Result.Clear();
            var vr = LocalPlayer.vrPlayerControl;
            if (!vr || !SetUp()) return Result;
            try
            {
                if (RotateActions.Count == 0) LoadActions();
                foreach (var hand in new[] { Hand(vr, "LeftHand"), Hand(vr, "RightHand") })
                {
                    if (!hand) continue;
                    var source = Traverse.Create(hand).Field("handType").GetValue();
                    if (source == null) continue;
                    foreach (var action in RotateActions)
                    {
                        if (!(bool)_getActive.Invoke(action, new[] { source })) continue;
                        Result.Add(hand.transform);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                _usable = false;
                Plugin.Log.LogWarning($"Arrow cycling: can't tell which controller has Rotate, looking at the bow instead. {e.Message}");
                Result.Clear();
            }
            return Result;
        }

        private static Component Hand(VRPlayerControl vr, string field) =>
            Traverse.Create(vr).Field(field).GetValue() as Component;

        private static bool SetUp()
        {
            if (_setUp) return _usable;
            _setUp = true;
            var input = Type.GetType("Valve.VR.SteamVR_Input, SteamVR", false);
            var boolAction = Type.GetType("Valve.VR.SteamVR_Action_Boolean, SteamVR", false);
            var sources = Type.GetType("Valve.VR.SteamVR_Input_Sources, SteamVR", false);
            if (input == null || boolAction == null || sources == null) return false;
            foreach (var method in input.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.Name == "GetActionFromPath" && method.IsGenericMethodDefinition && method.GetParameters().Length == 3)
                    _fromPath = method.MakeGenericMethod(boolAction);
            _getActive = AccessTools.Method(boolAction, "GetActive", new[] { sources });
            _usable = _fromPath != null && _getActive != null;
            return _usable;
        }

        /// <summary>
        /// The game's Rotate button: SteamVR action /actions/Default/in/Rotate (actions.json; the rotate axis is the
        /// separate Rotate_pos/_neg). Looked up directly, not through the game's action cache.
        /// </summary>
        private static void LoadActions()
        {
            var action = _fromPath.Invoke(null, new object[] { "/actions/Default/in/Rotate", false, true });
            if (action != null) RotateActions.Add(action);
        }
    }
}
