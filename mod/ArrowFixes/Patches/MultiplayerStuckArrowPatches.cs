using Bolt;
using HarmonyLib;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// Outgoing stuck-arrow sync. Vanilla only encodes bone (1) and bolt (2); modern arrows were sent as normal.
    /// Also, when a dead creature's arrows move to its ragdoll, the host sends bonus -1 for every arrow, so other
    /// players got plain arrows. If no bonus is passed, read it from the stuck arrow's own PickUp.
    /// </summary>
    [HarmonyPatch(typeof(arrowStickToTarget), nameof(arrowStickToTarget.sendArrowMP))]
    internal static class SendArrowMpPatch
    {
        private static bool Prefix(arrowStickToTarget __instance, GameObject fakeArrow, int closestJointIndex,
            ref ArrowBonus arrowBonus, bool crossbow)
        {
            if (crossbow || !fakeArrow) return true;
            if (arrowBonus == ArrowUtil.NoBonus) arrowBonus = ArrowUtil.BonusOfStuckArrow(fakeArrow);
            if (arrowBonus != ArrowBonus.ModernAmmo) return true;

            var ev = stuckArrowsSync.Create(GlobalTargets.Others);
            ev.target = __instance.transform.root.GetComponent<BoltEntity>();
            ev.pos = fakeArrow.transform.localPosition;
            ev.rot = fakeArrow.transform.localRotation;
            ev.type = ArrowUtil.NetModern;
            ev.index = closestJointIndex;
            if (ev.target) ev.Send();
            return false;
        }
    }

    /// <summary>Incoming stuck-arrow sync with the new modern-arrow code (vanilla would spawn a normal arrow).</summary>
    [HarmonyPatch(typeof(CoopServerCallbacks), nameof(CoopServerCallbacks.OnEvent), typeof(stuckArrowsSync))]
    internal static class ReceiveModernArrowPatch
    {
        private static bool Prefix(CoopServerCallbacks __instance, stuckArrowsSync evnt)
        {
            if (evnt.removeArrow || evnt.type != ArrowUtil.NetModern) return true;
            if (!__instance.ValidateSender(evnt) || !evnt.target) return false;

            var ast = evnt.target.GetComponent<arrowStickToTarget>();
            if (!ast) ast = evnt.target.GetComponentInChildren<arrowStickToTarget>();
            if (!ast || !ast.fakeArrowModernPickup) return false;

            // Same as the vanilla handler, with the modern pickup prefab.
            var go = Object.Instantiate(ast.fakeArrowModernPickup, evnt.target.transform.position, evnt.target.transform.rotation);
            go.transform.parent = ast.stickToJoints.Length == 0 ? ast.baseJoint : ast.stickToJoints[evnt.index];
            go.transform.localPosition = evnt.pos;
            go.transform.localRotation = evnt.rot;
            ast.stuckArrows.Add(go.transform, evnt.index);
            var setup = go.GetComponent<fakeArrowSetup>();
            if (setup && BoltNetwork.isRunning)
            {
                setup.storedIndex = ast.stuckArrows.Count - 1;
                setup.entityTarget = evnt.target;
            }
            return false;
        }
    }

    /// <summary>
    /// When arrows move from an enemy to its ragdoll, callers pass a type looked up from a parallel list that can be
    /// misaligned (e.g. clsragdollify skips destroyed arrows when indexing). Use the arrow's own PickUp instead.
    /// </summary>
    [HarmonyPatch(typeof(arrowStickToTarget), nameof(arrowStickToTarget.applyStuckArrowToDummy))]
    internal static class ApplyStuckArrowToDummyPatch
    {
        private static void Prefix(Transform arrow, ref int arrowType)
        {
            if (!arrow || arrowType == ArrowUtil.StuckBolt) return;
            arrowType = ArrowUtil.StuckTypeOf(ArrowUtil.BonusOfStuckArrow(arrow.gameObject));
        }

        private static void Postfix(arrowStickToTarget __instance, int arrowType)
        {
            // Keep the parallel type list aligned with stuckArrows for anything that reads it later.
            if (__instance.stuckArrowsTypeList.Count < __instance.stuckArrows.Count)
                __instance.stuckArrowsTypeList.Add(arrowType);
        }
    }
}
