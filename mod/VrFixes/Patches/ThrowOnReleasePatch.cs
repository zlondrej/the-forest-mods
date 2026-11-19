using System;
using System.Linq;
using HarmonyLib;

namespace VrFixes
{
    /// <summary>
    /// VR throwing (VRThrowable: spear, molotov, bombs, rocks, tennis ball) tries a throw both when the throw trigger is
    /// pressed and when it is released, using the hand's speed at that moment. Pressing the trigger while the hand is
    /// still moving (e.g. swinging back to wind up) threw the item right away, often backwards. Here only the release
    /// throws.
    /// </summary>
    [HarmonyPatch(typeof(VRThrowable), "ThrowProjectile")]
    internal static class ThrowOnReleasePatch
    {
        private static Func<bool> _throwHeld;

        private static bool Prepare()
        {
            // Input.VRThrowInput and Input.GetButton(Input.Action) exist in the VR build only.
            var input = typeof(TheForest.Utils.Input);
            var throwInput = AccessTools.PropertyGetter(input, "VRThrowInput");
            if (throwInput == null) return true; // flat-screen build: patch stays inert
            var getButton = AccessTools.GetDeclaredMethods(input).FirstOrDefault(m => m.Name == "GetButton" && m.IsStatic
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == throwInput.ReturnType);
            if (getButton == null) return true;
            _throwHeld = () => (bool)getButton.Invoke(null, new[] { throwInput.Invoke(null, null) });
            return true;
        }

        private static bool Prefix()
        {
            // The trigger still held: this is the press, not the release.
            return _throwHeld == null || !ForestVR.Enabled || !_throwHeld();
        }
    }
}
