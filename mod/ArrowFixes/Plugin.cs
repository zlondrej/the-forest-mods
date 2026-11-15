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

            Apply(harmony, "BoneArrowPickup",
                "Fired bone arrows give a bone arrow back when picked up (and stick into enemies as bone arrows).",
                typeof(BoneArrowPickupPatch));

            Apply(harmony, "UpgradeCombos",
                "Cloth (flaming) and poison upgrades keep the bone/modern base of an arrow.",
                typeof(CraftComboPatch), typeof(FireBasePrefabPatch), typeof(PrefabForBonusPatch),
                typeof(BowArrowRendererPatch), typeof(BowSwitchAwayFromLitArrowPatch), typeof(OnAmmoFiredCleanupPatch),
                typeof(CoatingEffectAlignPatch));

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
                    harmony.CreateClassProcessor(patchClass).Patch();
                }
            });
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
