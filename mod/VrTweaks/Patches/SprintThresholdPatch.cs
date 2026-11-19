using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace VrTweaks
{
    /// <summary>
    /// VR auto-run sprints while the movement stick is pushed past a fixed 0.85 of its range (the game's own value in
    /// FirstPersonCharacter.Update, used twice: to sprint, and to tell a fresh push that clears exhaustion). Here that
    /// value is <see cref="Threshold"/>: lower to sprint without pushing the stick all the way (sticks that don't reach
    /// the edge, especially diagonally), higher to walk with the stick further out. Flat-screen keeps 0.85.
    /// </summary>
    [HarmonyPatch(typeof(FirstPersonCharacter), "Update")]
    internal static class SprintThresholdPatch
    {
        public const float Vanilla = 0.85f;
        public static float Threshold = Vanilla;

        private static float Current() => ForestVR.Enabled ? Threshold : Vanilla;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var current = AccessTools.Method(typeof(SprintThresholdPatch), nameof(Current));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float f && f == Vanilla)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, current) { labels = instruction.labels, blocks = instruction.blocks };
                    continue;
                }
                yield return instruction;
            }
            if (replaced == 0) Plugin.Log.LogWarning("SprintThreshold: the game's 0.85 sprint threshold wasn't found, auto-run sprints as in vanilla");
        }
    }
}
