using TheForest.Items.Craft;
using TheForest.Items.Inventory;
using TheForest.Items.World;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// An arrow's upgrade lives in <see cref="ItemProperties.ActiveBonus"/>, which holds a single value.
    /// Bone/Modern are the arrow's "base"; Burning (cloth) and Poisoned are "coatings" that used to
    /// overwrite the base. When an arrow is coated, its base is kept in <see cref="ItemProperties.ActiveBonusValue"/>
    /// (unused for arrows in vanilla; already saved and synced with the properties).
    /// </summary>
    internal static class ArrowUtil
    {
        public const ArrowBonus NoBonus = (ArrowBonus)(-1);

        // stuckArrowsTypeList / applyStuckArrowToDummy codes used by arrowStickToTarget
        public const int StuckNormal = 0, StuckBone = 1, StuckModern = 2, StuckBolt = 3;

        // stuckArrowsSync.type network codes. Vanilla: 0 normal, 1 bone, 2 bolt. 3 is new (vanilla peers fall back to normal).
        public const int NetModern = 3;

        public static bool IsBase(ArrowBonus t) => t == ArrowBonus.BoneAmmo || t == ArrowBonus.ModernAmmo;

        public static bool IsCoating(ArrowBonus t) => t == ArrowBonus.BurningAmmo || t == ArrowBonus.PoisonnedAmmo;

        public static ArrowBonus GetBase(ItemProperties p)
        {
            if (p == null) return NoBonus;
            if (IsBase(p.ActiveBonus)) return p.ActiveBonus;
            if (IsCoating(p.ActiveBonus))
            {
                var stored = (ArrowBonus)Mathf.RoundToInt(p.ActiveBonusValue);
                return IsBase(stored) ? stored : NoBonus;
            }
            return NoBonus;
        }

        /// <summary>The bonus of the PickUp on a fake (stuck) arrow, or NoBonus.</summary>
        public static ArrowBonus BonusOfStuckArrow(GameObject fakeArrow)
        {
            if (!fakeArrow) return NoBonus;
            var pickup = fakeArrow.GetComponentInChildren<PickUp>(true);
            return pickup && pickup._properties != null ? pickup._properties.ActiveBonus : NoBonus;
        }

        public static int StuckTypeOf(ArrowBonus bonus) =>
            bonus == ArrowBonus.BoneAmmo ? StuckBone : bonus == ArrowBonus.ModernAmmo ? StuckModern : StuckNormal;
    }
}
