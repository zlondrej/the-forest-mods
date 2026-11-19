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
        private static Case _sceneCase;  // _case when the active scene started
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
            harmony.Patch(AccessTools.Method(typeof(PlayerStats), "releaseFromHanging"),
                prefix: new HarmonyMethod(typeof(ImmersiveCamera), nameof(ReleaseFromHangingPrefix)));
            harmony.Patch(AccessTools.Method(typeof(VRPlayerControl), "canUpdateMovement"),
                postfix: new HarmonyMethod(typeof(ImmersiveCamera), nameof(CanUpdateMovementPostfix)));
            harmony.Patch(AccessTools.Method(typeof(PlayerStats), "DeathInWater"),
                prefix: new HarmonyMethod(typeof(ImmersiveCamera), nameof(DeathInWaterPrefix)));
            harmony.Patch(AccessTools.Method(typeof(TriggerCutScene), "startPlayerInPlane"),
                prefix: new HarmonyMethod(typeof(ImmersiveCamera), nameof(PlaneIntroPrefix)));
        }

        private static bool _planeIntro;

        /// <summary>The plane intro: you start seated, see <see cref="RestoreGameModeRig"/>.</summary>
        private static void PlaneIntroPrefix() => _planeIntro = true;

        private static float _drownedAt = float.NegativeInfinity;

        /// <summary>
        /// Drowning in single player never marks the player dead, so every drowning tick after it (about once a second)
        /// runs the drowning death again, and each of those a few seconds later restarts the death animation
        /// (KillMeFast: cross-fade with no blend, sounds, ghost mode, the rig detached again). The theatre hides it; in
        /// first person the body flickered between two poses and the scene dragged on. Run it once per death.
        /// </summary>
        private static bool DeathInWaterPrefix(PlayerStats __instance)
        {
            if (!ForestVR.Enabled || __instance != LocalPlayer.Stats) return true;
            if (UnityEngine.Time.time - _drownedAt < 30f) return false;
            _drownedAt = UnityEngine.Time.time;
            return true;
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
            if (Plugin.Verbose && ForestVR.Enabled && _theatre) LogState(_theatre, "hanging scene starts");
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
                HeadPin.Start(_case == Case.HangingInCave ? StillHanging : (Func<bool>)null,
                    _case == Case.Climbing && ClimbingFreeOnRope ? InClimbTransition : (Func<bool>)null);
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
            HeadPin.AlignView(vr.VRCameraRig, eye, position, HeadPin.Heading(rotation));
        }

        /// <summary>Climbing with rotation only: pin the view only while getting on and off, free on the rope.</summary>
        internal static bool ClimbingFreeOnRope = true;

        private static readonly int ClimbingTag = Animator.StringToHash("climbing");
        private static readonly int ClimbIdleTag = Animator.StringToHash("climbIdle");

        /// <summary>
        /// Getting onto or off the rope/wall: the body's animator (base layer) is not (or not only) in the plain climbing
        /// states, which the game itself tags "climbing" and "climbIdle".
        /// </summary>
        private static bool InClimbTransition()
        {
            var animator = LocalPlayer.Animator;
            if (!animator) return true;
            if (!IsClimbState(animator.GetCurrentAnimatorStateInfo(0))) return true;
            return animator.IsInTransition(0) && !IsClimbState(animator.GetNextAnimatorStateInfo(0));
        }

        private static bool IsClimbState(AnimatorStateInfo state) => state.tagHash == ClimbingTag || state.tagHash == ClimbIdleTag;

        /// <summary>
        /// Cut down from hanging: flat-screen's camera follows the animation (falling on the back, getting up), VR's code
        /// leaves that out, so the pinned view stayed upside down. Follow the head as flat-screen does.
        /// </summary>
        private static void ReleaseFromHangingPrefix()
        {
            if (Plugin.Verbose && ForestVR.Enabled && _theatre) LogState(_theatre, "cut down from hanging");
            if (!ForestVR.Enabled || !HeadPin.On) return;
            HeadPin.FollowHead = true;
            Plugin.Debug("immersive camera: cut down from hanging, the view follows the head");
        }

        /// <summary>
        /// While the view is pinned the headset's position means nothing for the body, but VR's room-scale code would
        /// still move and turn the player toward it (and bend the spine to it) when movement isn't locked, e.g. in the
        /// first second of the hanging scene: no VR movement updates while a pin is on.
        /// </summary>
        private static void CanUpdateMovementPostfix(ref bool __result)
        {
            if (__result && HeadPin.On) __result = false;
        }

        /// <summary>
        /// What the theatre's SwitchToGameMode does to the VR rig when a scene ends, which nothing did when the theatre
        /// was skipped: the VR offset back under the head anchor at its standard place, not turned; the rig kept upright
        /// (its turn only); the eye recentered over the body; the physical-crouching rig height. Scenes move these: the
        /// plane intro puts the offset at a camera spot behind the player (afterwards the view swung around a point
        /// away from the head when the body turned), the drag-away scene turns the VR space to the dragged body (the
        /// view stayed tilted).
        /// </summary>
        private static void RestoreGameModeRig(VRTheatreController theatre)
        {
            var vr = LocalPlayer.vrPlayerControl;
            var anchor = LocalPlayer.CamFollowHead ? LocalPlayer.CamFollowHead.transform : null;
            if (!vr || !vr.VROffsetTransform || !anchor) return;
            if (Plugin.Verbose)
                Plugin.Debug($"immersive camera: rig before restoring: offset at {vr.VROffsetTransform.localPosition} under '{(vr.VROffsetTransform.parent ? vr.VROffsetTransform.parent.name : "-")}', tilt {Tilts()}");
            vr.VROffsetTransform.parent = anchor;
            vr.VROffsetTransform.localPosition = RigUnderHead;
            vr.VROffsetTransform.localEulerAngles = Vector3.zero;
            Level(vr.VRCameraRig);
            RecenterOnBody();
            if (PlayerPreferences.VRUsePhysicalCrouching && vr.VRCameraRig)
            {
                var p = vr.VRCameraRig.localPosition;
                vr.VRCameraRig.localPosition = new Vector3(p.x, theatre.offsetRigHeight, p.z);
            }
            // The plane intro starts seated: the game centres the eye on the seat, and the view's height came back from
            // there. In play the game corrects the eye height only when it is off by more than 0.5 m, so a smaller
            // offset stayed all session (the pause menu, at a fixed height, sat off from the view). Centre the eye on
            // the body, height included, as the game does itself.
            if (_planeIntro)
            {
                _planeIntro = false;
                vr.centerVrSpaceAroundHead();
                Plugin.Debug("immersive camera: plane intro over, view centred on the body (height included)");
            }
            if (Plugin.Verbose && vr.VRCamera && vr.VRCameraOrigin)
                Plugin.Debug($"immersive camera: eye {vr.VRCamera.position.y - vr.VRCameraOrigin.position.y:+0.00;-0.00} m above the body's camera origin");
        }

        private static void Level(Transform t)
        {
            if (t) t.localRotation = Quaternion.Euler(0f, HeadPin.Heading(t.localRotation), 0f);
        }

        // How far each part of the view's chain leans from upright (debug).
        private static string Tilts()
        {
            var vr = LocalPlayer.vrPlayerControl;
            string Tilt(Transform t) => t ? $"{Vector3.Angle(t.up, Vector3.up):0}°" : "-";
            return $"player={Tilt(LocalPlayer.Transform)} anchor={Tilt(LocalPlayer.CamFollowHead ? LocalPlayer.CamFollowHead.transform : null)} " +
                   $"offset={Tilt(vr ? vr.VROffsetTransform : null)} rig={Tilt(vr ? vr.VRCameraRig : null)}";
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
        private static bool _loggedGhost;
        private static VRTheatreController _theatre; // LocalPlayer.vrTheatreController doesn't exist in the flat-screen build

        /// <summary>
        /// Every frame (from the plugin): if the player went away mid-scene (drowning ends in a game over that loads the
        /// menu while the scene still runs), undo what would otherwise outlive it, positional tracking above all.
        /// </summary>
        public static void CheckPlayerGone()
        {
            if (!ForestVR.Enabled || (!HeadPin.On && !BodyView.Shown && _case == Case.None)) return;
            if (LocalPlayer.vrPlayerControl) return; // Unity null: destroyed with the game scene
            HeadPin.Abandon();
            BodyView.Abandon();
            _case = _skipping = _sceneCase = Case.None;
            _wasGhost = _aligned = _setGhostFlag = _loggedGhost = false;
            _drownedAt = float.NegativeInfinity;
            _planeIntro = false;
            _theatre = null;
            Plugin.Debug("immersive camera: player gone, scene state reset");
        }

        /// <summary>Debug: the state that decides how a scene plays (to tell why one went wrong).</summary>
        private static void LogState(VRTheatreController theatre, string what)
        {
            try
            {
                var adapter = LocalPlayer.vrAdapter;
                var joints = adapter ? adapter.JointsOffsetTransform : null;
                var animator = LocalPlayer.Animator;
                Plugin.Debug($"immersive camera: {what}: case={_case} theatre={theatre.theatreOn} ghostFlag={theatre.ghostFromTheatreMode} " +
                             $"pin={(HeadPin.On ? (HeadPin.Engaged ? "pinned" : "free") : "off")} skeletonUnder='{(joints && joints.parent ? joints.parent.name : "-")}' " +
                             $"followAnim={(LocalPlayer.CamFollowHead && LocalPlayer.CamFollowHead.followAnim)} " +
                             $"hanging={(animator && animator.GetBool("hangingBool"))} upsideDown={(LocalPlayer.AnimControl && LocalPlayer.AnimControl.upsideDown)} " +
                             $"fpCharacter={(LocalPlayer.FpCharacter && LocalPlayer.FpCharacter.enabled)} " +
                             $"playerYaw={(LocalPlayer.Transform ? LocalPlayer.Transform.eulerAngles.y : 0f):0} viewYaw={(LocalPlayer.MainCamTr ? HeadPin.Heading(LocalPlayer.MainCamTr.rotation) : 0f):0} " +
                             $"tilt: {Tilts()}");
            }
            catch (Exception e) { Plugin.Debug($"immersive camera: {what} (state not available: {e.Message})"); }
        }

        private static void TheatreUpdatePostfix(VRTheatreController __instance)
        {
            _theatre = __instance;
            var vr = LocalPlayer.vrPlayerControl;
            bool ghost = vr && vr.useGhostMode;
            if (Plugin.Verbose && ghost != _loggedGhost)
            {
                _loggedGhost = ghost;
                LogState(__instance, ghost ? "ghost mode on" : "ghost mode off");
            }
            HeadPin.Tick();
            // The pin ends with ghost mode, or later for scenes that outlast it (hanging: the game ends ghost mode
            // while the player still hangs).
            if (!ghost && HeadPin.On && !HeadPin.Holding) HeadPin.Stop();
            if (!ghost && !HeadPin.On && BodyView.Shown) BodyView.Restore();
            bool active = ghost || HeadPin.On;
            if (_wasGhost && !active)
            {
                if (_skipping != Case.None && !__instance.theatreOn)
                {
                    RestoreGameModeRig(__instance);
                    __instance.vrPos1 = null;
                    __instance.vrPos2 = null;
                    __instance.useCustomPosition = false;
                    SteppedMode(__instance) = false;
                }
                if (_setGhostFlag && !__instance.theatreOn) __instance.ghostFromTheatreMode = false;
                _setGhostFlag = false;
                _aligned = false;
                if (Plugin.Verbose) LogState(__instance, "scene ended");
                // Forget the case, unless the next scene already noted its own (dying outside the caves, the death
                // scene ends in the very frame the drag-away scene starts).
                if (_case == _sceneCase) _case = Case.None;
                else Plugin.Debug($"immersive camera: next scene already started: {_case}");
                _skipping = Case.None;
            }
            else if (!_wasGhost && active)
            {
                _sceneCase = _case;
            }
            _wasGhost = active;
        }
    }
}
