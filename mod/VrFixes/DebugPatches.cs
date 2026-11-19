using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// [Debug] VerboseLogging only: log VR drop and throw attempts, and what drives the rope climbing animation (to see
    /// why the fast rope slide rarely starts in VR).
    /// </summary>
    internal static class DebugPatches
    {
        public static void Apply(Harmony harmony)
        {
            // VR build only (the flat build has no GetDropInput).
            var getDrop = AccessTools.Method(typeof(TheForest.Utils.Input), "GetDropInput");
            if (getDrop != null)
                harmony.Patch(getDrop, postfix: new HarmonyMethod(typeof(DebugPatches), nameof(GetDropInputPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), nameof(PlayerInventory.DropEquipedWeapon)),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(DropPrefix)));
            harmony.Patch(AccessTools.Method(typeof(VRThrowable), "ThrowProjectile"),
                postfix: new HarmonyMethod(typeof(DebugPatches), nameof(ThrowPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerInventory), "ThrowProjectile"),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(InventoryThrowPrefix)));
            harmony.Patch(AccessTools.Method(typeof(playerAnimatorControl), "Update"),
                postfix: new HarmonyMethod(typeof(DebugPatches), nameof(RopeStatePostfix)));
            harmony.Patch(AccessTools.Method(typeof(Explode), "RunExplode"),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(RunExplodePrefix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerStats), "Explosion", new[] { typeof(float), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(PlayerExplosionPrefix)),
                postfix: new HarmonyMethod(typeof(DebugPatches), nameof(PlayerHealthPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerStats), nameof(PlayerStats.Burn)),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(PlayerBurnPrefix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerStats), nameof(PlayerStats.Hit)),
                prefix: new HarmonyMethod(typeof(DebugPatches), nameof(PlayerHitPrefix)),
                postfix: new HarmonyMethod(typeof(DebugPatches), nameof(PlayerHealthPostfix)));
            harmony.Patch(AccessTools.Method(typeof(HudGui), nameof(HudGui.TogglePauseMenu)),
                postfix: new HarmonyMethod(typeof(DebugPatches), nameof(PauseMenuPostfix)));
        }

        // ------------------------------------------------------------------ pause menu

        /// <summary>
        /// VR pause menu opened: where the eye is against the controllers and the body, and how the VR rig and the
        /// skeleton sit (the view was off from the controllers after the plane intro).
        /// </summary>
        private static void PauseMenuPostfix(bool on)
        {
            if (!on || !ForestVR.Enabled) return;
            var vr = LocalPlayer.vrPlayerControl;
            var eye = LocalPlayer.MainCamTr;
            if (!vr || !eye) return;
            string Rel(Transform t) => t ? (t.position - eye.position).ToString("F2") : "-";
            string Local(Transform t) => t ? $"{t.localPosition.ToString("F2")} rot {t.localEulerAngles.ToString("F0")} under '{(t.parent ? t.parent.name : "-")}'" : "-";
            var joints = LocalPlayer.vrAdapter ? LocalPlayer.vrAdapter.JointsOffsetTransform : null;
            // The controllers' type (Valve's Hand) is in another assembly in the VR build: read them as plain components.
            Transform Hand(string field) => Traverse.Create(vr).Field(field).GetValue() is Component c && c ? c.transform : null;
            Plugin.Debug("pause menu: relative to the eye: left hand " + Rel(Hand("LeftHand")) +
                         ", right hand " + Rel(Hand("RightHand")) +
                         ", camera origin " + Rel(vr.VRCameraOrigin) + ", player " + Rel(LocalPlayer.Transform) +
                         ", head joint " + Rel(LocalPlayer.CamFollowHead ? LocalPlayer.CamFollowHead.headJnt : null));
            Plugin.Debug($"pause menu: eye local {eye.localPosition.ToString("F2")}; rig {Local(vr.VRCameraRig)}; offset {Local(vr.VROffsetTransform)}; skeleton {Local(joints)}");
        }

        // ------------------------------------------------------------------ self damage

        /// <summary>
        /// An explosion goes off: how far the player is, and whether the player's hit collider (tag playerHitDetect,
        /// what explosions damage) is inside its radius.
        /// </summary>
        private static void RunExplodePrefix(Explode __instance)
        {
            if (!LocalPlayer.Transform) return;
            var position = __instance.transform.position;
            int hitDetect = 0;
            string hitDetectInfo = "";
            foreach (var collider in Physics.OverlapSphere(position, __instance.radius))
            {
                if (!collider || !collider.CompareTag("playerHitDetect")) continue;
                hitDetect++;
                hitDetectInfo += $" '{collider.name}' {Vector3.Distance(position, collider.transform.position):0.0} m (enabled={collider.enabled}, under player={collider.transform.IsChildOf(LocalPlayer.Transform)})";
            }
            Plugin.Debug($"self damage: explosion '{__instance.name}' radius={__instance.radius:0.0} m, player {Vector3.Distance(position, LocalPlayer.Transform.position):0.0} m away, " +
                         $"player hit colliders in range: {hitDetect}{hitDetectInfo}");
        }

        private static void PlayerExplosionPrefix(PlayerStats __instance, float dist, bool fromPlayer)
        {
            Plugin.Debug($"self damage: player Explosion(dist={dist:0.0}, fromPlayer={fromPlayer}) health={__instance.Health:0.0} " +
                         $"alreadyExploding={Traverse.Create(__instance).Field("isExplode").GetValue()} view={(LocalPlayer.Inventory ? LocalPlayer.Inventory.CurrentView.ToString() : "-")} " +
                         $"godMode={Cheats.GodMode}");
        }

        private static void PlayerBurnPrefix(PlayerStats __instance)
        {
            // IsLit is private; LocalPlayer.WaterViz is a field in one build and a property in the other. Fire calls this
            // every frame while it touches the player: log only catching fire.
            if (Traverse.Create(__instance).Field("IsLit").GetValue() is bool lit && lit) return;
            Plugin.Debug($"self damage: player Burn() health={__instance.Health:0.0} alreadyLit={Traverse.Create(__instance).Field("IsLit").GetValue()} " +
                         $"swimming={(LocalPlayer.AnimControl && LocalPlayer.AnimControl.swimming)}");
        }

        private static void PlayerHitPrefix(PlayerStats __instance, int damage, bool ignoreArmor)
        {
            Plugin.Debug($"self damage: player Hit(damage={damage}, ignoreArmor={ignoreArmor}) health={__instance.Health:0.0} godMode={Cheats.GodMode}");
        }

        private static void PlayerHealthPostfix(PlayerStats __instance)
        {
            Plugin.Debug($"self damage: player health now {__instance.Health:0.0}");
        }

        // ------------------------------------------------------------------ rope

        private static readonly AccessTools.FieldRef<playerAnimatorControl, float> VerticalSpeed =
            AccessTools.FieldRefAccess<playerAnimatorControl, float>("verticalSpeed");

        private static readonly AccessTools.FieldRef<playerAnimatorControl, float> YVel =
            AccessTools.FieldRefAccess<playerAnimatorControl, float>("yVel");

        private static float _nextRopeLog;

        /// <summary>
        /// While on a rope, log what drives the climbing animation (twice a second). The fast slide is picked by the
        /// animator from these values; in flat-screen sprint starts it, in VR it rarely does.
        /// </summary>
        private static void RopeStatePostfix(playerAnimatorControl __instance)
        {
            if (__instance != LocalPlayer.AnimControl || !__instance.onRope || Time.time < _nextRopeLog) return;
            _nextRopeLog = Time.time + 0.5f;
            var animator = LocalPlayer.Animator;
            var fpc = LocalPlayer.FpCharacter;
            object startedMoving = null;
            try
            {
                var vr = Traverse.Create(typeof(LocalPlayer)).Field("vrPlayerControl");
                if (!vr.FieldExists()) vr = Traverse.Create(typeof(LocalPlayer)).Property("vrPlayerControl");
                var control = vr.GetValue();
                if (control != null) startedMoving = Traverse.Create(control).Field("_startedMoving").GetValue();
            }
            catch { }
            Plugin.Debug($"rope state: animator vSpeed={(animator ? animator.GetFloat("vSpeed") : float.NaN):0.00}" +
                         $" climbSpeed={(animator ? animator.GetFloat("climbSpeed") : float.NaN):0.00}" +
                         $" verticalSpeed={VerticalSpeed(__instance):0.00} input={(fpc ? fpc.inputVelocity : Vector3.zero)}" +
                         $" run={(fpc ? fpc.run : false)} startedMoving={startedMoving ?? "n/a"} yVel={YVel(__instance):0.0}" +
                         $" stamina={(LocalPlayer.Stats ? LocalPlayer.Stats.Stamina : -1f):0.0}");
        }

        private static int _lastDropFrame = -1;

        private static void GetDropInputPostfix(bool __result)
        {
            if (!__result || _lastDropFrame == Time.frameCount || !ForestVR.Enabled) return;
            _lastDropFrame = Time.frameCount;
            var inv = LocalPlayer.Inventory;
            if (!inv) return;
            var held = inv.EquipmentSlots != null ? inv.EquipmentSlots[0] : null;
            var item = held ? held.ItemCache : null;
            Plugin.Debug($"drop pressed: view={inv.CurrentView}, right hand={(item != null ? item._name : "empty")}" +
                         (item != null ? $" (max {item._maxAmount}, droppable {item.MatchType(TheForest.Items.Item.Types.Droppable)}, " +
                                         $"VRBlockPutAway {item._VRBlockPutAway}, blockPutAway {item._blockPutAway})" : "") +
                         $", BlockDrop={inv.BlockDrop}, UseAltWorldPrefab={inv.UseAltWorldPrefab}, upsideDown={LocalPlayer.AnimControl?.upsideDown}" +
                         $", carry={LocalPlayer.AnimControl?.carry}, logs={inv.Logs?.Amount}");
        }

        private static void DropPrefix(bool equipPrevious)
        {
            var held = LocalPlayer.Inventory?.EquipmentSlots?[0];
            Plugin.Debug($"drop: dropping {(held && held.ItemCache != null ? held.ItemCache._name : "?")} (equipPrevious {equipPrevious})");
        }

        private static void ThrowPostfix(VRThrowable __instance)
        {
            float force = __instance.lastThrowForce.magnitude;
            Plugin.Debug($"throw: '{__instance.name}' force {force:0} (needs 500 to throw){(force < 500f ? " - too slow, not thrown" : "")}");
        }

        private static void InventoryThrowPrefix(PlayerInventory __instance)
        {
            var held = __instance.EquipmentSlots?[0];
            Plugin.Debug($"throw: inventory throws {(held && held.ItemCache != null ? held.ItemCache._name : "nothing (right hand empty)")}");
        }
    }
}
