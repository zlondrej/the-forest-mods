using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// Root cause of "bone arrows revert to normal arrows": the fired-arrow prefab 'ArrowBone' has a PickUp whose
    /// _properties.ActiveBonus is -1 (the 'ArrowModern' prefab correctly has ModernAmmo). So a bone arrow that lands
    /// in the world is picked up as a plain arrow, and one that hits an enemy is turned into a plain stuck arrow
    /// (arrowStickToTarget.stickArrowToNearestBone reads the bonus from this same PickUp).
    /// </summary>
    [HarmonyPatch(typeof(arrowTrajectory), "Awake")]
    internal static class BoneArrowPickupPatch
    {
        private static void Postfix(arrowTrajectory __instance)
        {
            if (!__instance.name.StartsWith("ArrowBone") || !__instance._pickup) return;
            var pickup = __instance._pickup.GetComponent<PickUp>();
            if (!pickup) return;
            if (pickup._properties == null) pickup._properties = new ItemProperties();
            if (pickup._properties.ActiveBonus == ArrowUtil.NoBonus)
            {
                pickup._properties.ActiveBonus = ArrowBonus.BoneAmmo;
            }
        }
    }
}
