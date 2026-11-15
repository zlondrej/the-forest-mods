using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ArrowFixes
{
    [BepInPlugin(Guid, "Arrow Fixes", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "forestmods.arrowfixes";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(Guid);

            Dbg.On = Config.Bind("Debug", "VerboseLogging", false,
                "Write detailed [debug] lines to BepInEx/LogOutput.log (crafting, arrow cycling, lighting, every arrow upgrade change).").Value;
            if (Dbg.On)
            {
                Try("VerboseLogging", () => PatchClass(harmony, typeof(DebugBonusChangePatches)));
            }

            Apply(harmony, "BoneArrowPickup",
                "Fired bone arrows give a bone arrow back when picked up (and stick into enemies as bone arrows).",
                typeof(BoneArrowPickupPatch));

            Apply(harmony, "UpgradeCombos",
                "Cloth (flaming) and poison upgrades keep the bone/modern base of an arrow.",
                typeof(CraftComboPatch), typeof(FireBasePrefabPatch), typeof(PrefabForBonusPatch),
                typeof(BowArrowRendererPatch), typeof(BowSwitchAwayFromLitArrowPatch), typeof(OnAmmoFiredCleanupPatch),
                typeof(CoatingEffectAlignPatch));

            Apply(harmony, "UpgradeComboCycling",
                "Arrow-type cycling (look down + Rotate) treats e.g. flaming bone and flaming wooden arrows as separate types.",
                typeof(ArrowCyclingPatches));

            Apply(harmony, "UpgradeComboVisuals",
                "Cloth/poison bone and modern arrows show their own model with the cloth wrap and fire/poison tint; cloth wrap and flame sit at the tip of the bone/modern model.",
                typeof(ArrowVisualPatches), typeof(LitArrowFlameAlignPatch));

            Apply(harmony, "MultiplayerStuckArrows",
                "Multiplayer: bone/modern arrows stuck in enemies stay bone/modern for other players.",
                typeof(SendArrowMpPatch), typeof(ReceiveModernArrowPatch), typeof(ApplyStuckArrowToDummyPatch));

            if (Config.Bind("Fixes", "VrLighterIgnitesArrow", true,
                    "VR: holding the Lighter button ignites a cloth-wrapped arrow, also while nocked.").Value)
            {
                Try("VrLighterIgnitesArrow", () => VrArrowLighting.Apply(harmony));
            }
        }

        private void Apply(Harmony harmony, string key, string description, params Type[] patchClasses)
        {
            if (!Config.Bind("Fixes", key, true, description).Value)
            {
                Log.LogInfo($"{key}: disabled in config");
                return;
            }
            Try(key, () =>
            {
                foreach (var patchClass in patchClasses)
                {
                    PatchClass(harmony, patchClass);
                }
            });
        }

        /// <summary>
        /// Patch one class; a class that patches nothing is an error (e.g. HarmonyX silently skips classes without a
        /// class-level [HarmonyPatch] attribute).
        /// </summary>
        internal static void PatchClass(Harmony harmony, Type patchClass)
        {
            var patched = harmony.CreateClassProcessor(patchClass).Patch();
            if (patched == null || patched.Count == 0)
                throw new InvalidOperationException($"{patchClass.Name} patched no methods");
            foreach (var method in patched)
                Dbg.Log($"{patchClass.Name} patched {method.DeclaringType?.Name}.{method.Name}");
        }

        private static void Try(string key, Action apply)
        {
            try
            {
                apply();
                Log.LogInfo($"{key}: applied");
            }
            catch (Exception e)
            {
                Log.LogError($"{key}: failed to apply, fix is inactive. {e}");
            }
        }
    }
}
