using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheForest.Buildings.World;
using TheForest.Player.Actions;
using TheForest.Utils;
using UnityEngine;

namespace VrFixes
{
    /// <summary>
    /// During scripted animations VR switches to "theatre mode": the scene is shown on a screen in a small dark room. Every
    /// such action sets VRPlayerControl.useGhostMode, and VRTheatreController switches the theatre on whenever that flag is
    /// set, without knowing why. Here each action notes its case when it starts; when the theatre is about to switch on,
    /// a case set to immersive skips it, so the view stays first person. Ghost mode itself (the animation driving the
    /// body) is unchanged.
    /// </summary>
    internal static class ImmersiveCamera
    {
        internal enum Case { None, Caves, Climbing, Drinking, Raft, Skinning, KeypadDoors, Artifact, StoryScenes, HangingInCave, Death }

        /// <summary>Per case: true = immersive (first person), false = theatre (vanilla).</summary>
        internal static readonly Dictionary<Case, bool> Immersive = new Dictionary<Case, bool>();

        /// <summary>Per immersive case: true = the headset only rotates the view, which is pinned to the character's head.</summary>
        internal static readonly Dictionary<Case, bool> RotationOnly = new Dictionary<Case, bool>();

        private sealed class Trigger
        {
            public readonly Case Case;
            public readonly Type Type;
            public readonly string[] Methods;

            public Trigger(Case c, Type type, params string[] methods)
            {
                Case = c;
                Type = type;
                Methods = methods;
            }
        }

        // The actions that switch theatre mode on, per case.
        private static readonly Trigger[] Triggers =
        {
            new Trigger(Case.Caves, typeof(playerEnterCaveAction), "doCave"),
            new Trigger(Case.Climbing, typeof(PlayerClimbRopeAction), "enterClimbRope", "enterClimbRopeTop"),
            new Trigger(Case.Drinking, typeof(WaterSource), "enableDrinkParams"),
            new Trigger(Case.Raft, typeof(RaftPush), "onRaft"),
            new Trigger(Case.Skinning, typeof(PlayerSkinAnimalAction), "skinAnimalRoutine"),
            new Trigger(Case.KeypadDoors, typeof(playerOpenKeypadDoorAction), "openDoorRoutine", "lockPlayerParams"),
            new Trigger(Case.Artifact, typeof(playerPlaceArtifactAction), "placeArtifactRoutine"),
            new Trigger(Case.StoryScenes, typeof(PlayerStats), "dragAwayCutScene", "EndgameWakeUp"),
            new Trigger(Case.HangingInCave, typeof(PlayerStats), "hangingInCaveCutScene"),
            new Trigger(Case.StoryScenes, typeof(PlayerEndCrashAction), "doEndPlaneCrashRoutine", "doShutDownRoutine", "lockPlayerParams"),
            new Trigger(Case.StoryScenes, typeof(PlayerGirlPickupAction), "girlToMachineRoutine"),
            new Trigger(Case.StoryScenes, typeof(PlayerGirlTransformAction), "doGirlTransformRoutine", "lockPlayerParams"),
            new Trigger(Case.StoryScenes, typeof(PlayerPickupTimmyAction), "pickupTimmyRoutine", "lockPlayerParams"),
            new Trigger(Case.StoryScenes, typeof(TriggerCutScene), "startPlayerInPlane"),
            new Trigger(Case.Death, typeof(PlayerStats), "DeathInWater", "FallDownDead", "KillMeFast", "KillPlayer"),
        };

        private static Case _case;       // the action that started last
        private static bool _wasGhost;   // scene active (ghost mode or a pin) in the previous frame
        private static Case _skipping;   // case whose theatre is being skipped (for logging once)

        public static void Apply(Harmony harmony)
        {
            foreach (var trigger in Triggers)
            {
                foreach (var name in trigger.Methods)
                {
                    var method = AccessTools.GetDeclaredMethods(trigger.Type).FirstOrDefault(m => m.Name == name)
                                 ?? throw new MissingMethodException(trigger.Type.Name, name);
                    // One prefix per case: a static method per case keeps it simple without closures.
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(ImmersiveCamera), "Start" + trigger.Case));
                }
            }
            harmony.Patch(AccessTools.Method(typeof(VRTheatreController), nameof(VRTheatreController.SwitchToTheatreMode)),
                prefix: new HarmonyMethod(typeof(ImmersiveCamera), nameof(SwitchToTheatrePrefix)));
            harmony.Patch(AccessTools.Method(typeof(VRTheatreController), "Update"),
                postfix: new HarmonyMethod(typeof(ImmersiveCamera), nameof(TheatreUpdatePostfix)));
        }

        private static void Started(Case c) => _case = c;
        private static void StartCaves() => Started(Case.Caves);
        private static void StartClimbing() => Started(Case.Climbing);
        private static void StartDrinking() => Started(Case.Drinking);
        private static void StartRaft() => Started(Case.Raft);
        private static void StartSkinning() => Started(Case.Skinning);
        private static void StartKeypadDoors() => Started(Case.KeypadDoors);
        private static void StartArtifact() => Started(Case.Artifact);
        private static void StartStoryScenes() => Started(Case.StoryScenes);
        /// <summary>
        /// The hanging scene starts (teleport, hanging pose) well before it turns ghost mode on, and the view fell through
        /// the ground meanwhile: pin it right away when the scene is first person with rotation only.
        /// </summary>
        private static void StartHangingInCave()
        {
            Started(Case.HangingInCave);
            if (!ForestVR.Enabled || !Is(Immersive, Case.HangingInCave) || !Is(RotationOnly, Case.HangingInCave)) return;
            _hangingSince = UnityEngine.Time.time;
            _hangingSeen = false;
            HeadPin.Start(StillHanging);
            BodyView.Show();
        }

        private static bool Is(Dictionary<Case, bool> settings, Case c) => settings.TryGetValue(c, out var on) && on;
        private static void StartDeath() => Started(Case.Death);

        private static float _hangingSince;
        private static bool _hangingSeen;

        /// <summary>
        /// Hanging upside down in the cave: the animator's hangingBool, cleared when the player is cut down. Before it is
        /// first set (the scene's first seconds), hold for up to 15 s.
        /// </summary>
        private static bool StillHanging()
        {
            bool hanging = LocalPlayer.Animator && LocalPlayer.Animator.GetBool("hangingBool");
            if (hanging) _hangingSeen = true;
            return hanging || (!_hangingSeen && UnityEngine.Time.time - _hangingSince < 15f);
        }

        private static readonly AccessTools.FieldRef<VRTheatreController, bool> SteppedMode =
            AccessTools.FieldRefAccess<VRTheatreController, bool>("steppedMode");

        private static readonly AccessTools.FieldRef<VRTheatreController, UnityEngine.Vector3> StoredVrOffsetPos =
            AccessTools.FieldRefAccess<VRTheatreController, UnityEngine.Vector3>("storeVrOffsetPos");

        // Where SwitchToGameMode puts the VR rig under the head when there is no stored position.
        private static readonly UnityEngine.Vector3 RigUnderHead = new UnityEngine.Vector3(0f, -4.27549f, -0.206f);

        /// <summary>Skip the theatre (stay first person) when the action that asked for it is set to immersive.</summary>
        private static bool SwitchToTheatrePrefix(VRTheatreController __instance)
        {
            if (!ForestVR.Enabled || __instance.theatreOn) return true;
            if (_case == Case.None || !Immersive.TryGetValue(_case, out var immersive) || !immersive) return true;
            if (_skipping != _case)
            {
                _skipping = _case;
                Plugin.Debug($"immersive camera: {_case}, staying first person (theatre skipped)");
            }
            KeepRigOnHead(__instance);
            BodyView.Show();
            if (Is(RotationOnly, _case))
            {
                HeadPin.Start(_case == Case.HangingInCave ? StillHanging : (Func<bool>)null);
                // The body plays the scene as in the theatre (skeleton on the body), so the animator gets the theatre's
                // ghostMode flag too: without it the scene's full-body animations don't finish (cut down from hanging,
                // the fall never reached idle, so the controls never came back).
                if (HeadPin.On && !__instance.ghostFromTheatreMode)
                {
                    __instance.ghostFromTheatreMode = true;
                    _setGhostFlag = true;
                }
            }
            else if (AlignViewAtStart && !_aligned)
            {
                AlignViewToCharacter();
            }
            _aligned = true;
            return false;
        }

        /// <summary>At the start of a first-person scene, turn and move the view to the character's (see <see cref="AlignViewToCharacter"/>).</summary>
        internal static bool AlignViewAtStart = true;

        private static bool _aligned;       // view aligned (or pinned) for this scene
        private static bool _setGhostFlag;  // ghostFromTheatreMode set here, to clear when the scene ends

        /// <summary>
        /// The player's view looks where the character looks and sits (horizontally) at its head, as in flat-screen when
        /// the scene starts: the VR rig is turned around the eye so the view's heading matches the camera anchor's (the
        /// character's direction), and moved so the eye is over the head. Height stays the player's own. The headset
        /// tracks normally from there.
        /// </summary>
        private static void AlignViewToCharacter()
        {
            var vr = LocalPlayer.vrPlayerControl;
            var eye = LocalPlayer.MainCamTr;
            if (!vr || !vr.VRCameraRig || !eye || !HeadPin.FlatView(out var position, out var rotation)) return;
            var rig = vr.VRCameraRig;
            float turn = Mathf.DeltaAngle(HeadPin.Heading(eye.rotation), HeadPin.Heading(rotation));
            rig.RotateAround(eye.position, Vector3.up, turn);
            var offset = position - eye.position;
            offset.y = 0f;
            rig.position += offset;
            Plugin.Debug($"immersive camera: view turned {turn:0}° and moved {offset.magnitude:0.00} m to the character's head");
        }

        /// <summary>As when the theatre ends: the rig is moved so the eye is (horizontally) over the body's camera origin.</summary>
        private static void RecenterOnBody()
        {
            var vr = LocalPlayer.vrPlayerControl;
            if (!vr || !vr.VRCamera || !vr.VRCameraOrigin || !vr.VRCameraRig) return;
            var position = vr.VRCamera.position;
            position.y = vr.VRCameraOrigin.position.y;
            vr.VRCameraRig.position -= position - vr.VRCameraOrigin.position;
        }

        /// <summary>
        /// Some actions (skinning, getting onto a rope) call UseSteppedGhostMode, which detaches the VR rig from the
        /// head for the theatre; only SwitchToGameMode reattaches it, and that does nothing when the theatre never
        /// switched on. Without this the view stayed put (looking like third person) and afterwards rig and body
        /// drifted apart (stuck, flickering between two positions).
        /// </summary>
        private static void KeepRigOnHead(VRTheatreController theatre)
        {
            var vr = LocalPlayer.vrPlayerControl;
            var head = LocalPlayer.CamFollowHead ? LocalPlayer.CamFollowHead.transform : null;
            if (!vr || !vr.VROffsetTransform || !head || vr.VROffsetTransform.parent == head) return;
            vr.VROffsetTransform.parent = head;
            vr.VROffsetTransform.localPosition = SteppedMode(theatre) ? StoredVrOffsetPos(theatre) : RigUnderHead;
            vr.VROffsetTransform.localEulerAngles = UnityEngine.Vector3.zero;
            SteppedMode(theatre) = false;
            Plugin.Debug("immersive camera: VR rig reattached to the head");
        }

        /// <summary>
        /// When the scene ends (ghost mode off and no pin holding on), forget the case (an action that never turned ghost
        /// mode on is overridden later). If the theatre was skipped, also clear what SwitchToGameMode would have reset
        /// (stand positions, stepped mode).
        /// </summary>
        private static void TheatreUpdatePostfix(VRTheatreController __instance)
        {
            var vr = LocalPlayer.vrPlayerControl;
            bool ghost = vr && vr.useGhostMode;
            // The pin ends with ghost mode, or later for scenes that outlast it (hanging: the game ends ghost mode
            // while the player still hangs).
            if (!ghost && HeadPin.On && !HeadPin.Holding) HeadPin.Stop();
            if (!ghost && !HeadPin.On && BodyView.Shown) BodyView.Restore();
            bool active = ghost || HeadPin.On;
            if (_wasGhost && !active)
            {
                if (_skipping != Case.None && !__instance.theatreOn)
                {
                    KeepRigOnHead(__instance);
                    RecenterOnBody();
                    __instance.vrPos1 = null;
                    __instance.vrPos2 = null;
                    __instance.useCustomPosition = false;
                    SteppedMode(__instance) = false;
                }
                if (_setGhostFlag && !__instance.theatreOn) __instance.ghostFromTheatreMode = false;
                _setGhostFlag = false;
                _aligned = false;
                _case = Case.None;
                _skipping = Case.None;
            }
            _wasGhost = active;
        }
    }
}
