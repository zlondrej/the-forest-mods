using System.Collections.Generic;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrTweaks
{
    /// <summary>
    /// VR draws the player's body only as a shadow (VRPlayerAdapter sets PlayerMeshesToDisable to shadows only), so
    /// looking down there is nothing below the floating VR hands. Here the body is drawn: torso and legs, clothes and
    /// armour, following the VR body as the game animates it. Left out: the head (it would block the view; its meshes
    /// are also on a layer the eye camera doesn't draw) and the bare arms and hands (the VR hands are drawn where your
    /// controllers are; the body's own would be a second pair). Sleeves stay, as part of the shirts and jackets.
    /// Armour (<see cref="Armor"/>) and the backpack (<see cref="Backpack"/>) can be left out, as they block the view
    /// looking down. Armour left out isn't drawn at all, not even its shadow (a shadow of armour you don't see looks
    /// wrong); a backpack left out still casts its shadow, as in vanilla.
    /// </summary>
    internal static class PlayerBody
    {
        public enum ArmorShown { None, Limbs, All }

        public static bool Visible = true;

        /// <summary>Which armour is drawn: none, only on arms and legs, or all (chest, shoulders and body too).</summary>
        public static ArmorShown Armor = ArmorShown.All;

        /// <summary>Draw the backpack.</summary>
        public static bool Backpack = true;

        /// <summary>Metres the body moves back behind your eyes when you look down (negative = forward); see BodyPlacement.</summary>
        public static float BackOffset = 0.5f;

        /// <summary>Metres the watch sits further along the forearm, towards the fingertips (negative = towards the elbow).</summary>
        public static float WatchOffset = 0.03f;

        /// <summary>Metres the watch is moved sideways across the wrist (its angle unchanged).</summary>
        public static float WatchSide = 0.017f;

        /// <summary>The body's own arms or hands are drawn (not the VR hands): the watch is placed on them.</summary>
        public static bool BodyHandsShown => _drawnArms;

        /// <summary>The same looking ahead or up (BackOffset is blended in as you look down).</summary>
        public static float BackOffsetAhead = 0.1f;

        private static VRPlayerAdapter _done;
        private static int _drawnWith = -1; // the Armor and Backpack settings the body is drawn with

        // Every renderer changed, with how the game had it (put back by Hide).
        private struct Was
        {
            public bool Enabled;
            public ShadowCastingMode Mode;
        }

        private static readonly Dictionary<Renderer, Was> Changed = new Dictionary<Renderer, Was>();
        private static int _drawn;
        private static readonly List<string> ArmorDrawn = new List<string>(), ArmorLeftOut = new List<string>(); // for the log

        /// <summary>
        /// A few times per second: once per player (it is recreated on load), draw the body; or undraw it.
        /// How vanilla VR leaves them (VRPlayerAdapter.ConvertToVR): the clothes and the skinned armour (lizard,
        /// stealth, creepy, some bone pieces) cast only a shadow; of the armour hung on the bones, the leaf armour's
        /// leaves are drawn as in flat-screen and the bone armour's plates are switched off altogether. So the armour on
        /// the skeleton is gone through too, and here every piece follows the Armor setting instead: drawn (with its
        /// shadow) or not at all.
        /// </summary>
        public static void Tick()
        {
            if (!ForestVR.Enabled || BodyView.Shown) return; // in a first-person scene: the scene's body view first
            int settings = (int)Armor * 2 + (Backpack ? 1 : 0);
            if (!Visible || settings != _drawnWith)
            {
                Hide(); // drawn again below with the new settings
                if (!Visible) return;
            }
            var adapter = LocalPlayer.vrAdapter;
            if (!adapter || adapter == _done || adapter.PlayerMeshesToDisable == null) return;
            _done = adapter;
            _drawnWith = settings;
            Changed.Clear();
            ArmorDrawn.Clear();
            ArmorLeftOut.Clear();
            _drawn = 0;
            foreach (var go in adapter.PlayerMeshesToDisable)
                if (go)
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        Draw(adapter, r);
            if (adapter.JointsOffsetTransform)
                foreach (var r in adapter.JointsOffsetTransform.GetComponentsInChildren<MeshRenderer>(true))
                    if (r && IsArmor(r.transform) && !IsHeld(r.transform)) Draw(adapter, r);
            Plugin.Log.LogInfo($"PlayerBody: {_drawn} body meshes drawn (armour: {Armor}, backpack: {Backpack}); armour drawn: "
                               + $"{string.Join(", ", ArmorDrawn.ToArray())}; left out: {string.Join(", ", ArmorLeftOut.ToArray())}");
        }

        private static void Draw(VRPlayerAdapter adapter, Renderer r)
        {
            if (!r || Changed.ContainsKey(r)) return;
            if (r == adapter.PlayerHead1 || BodyView.IsHead(r.name)) return;
            if (IsArmor(r.transform))
            {
                bool drawn = Armor == ArmorShown.All || (Armor == ArmorShown.Limbs && OnLimb(r.transform));
                Set(r, drawn, ShadowCastingMode.On); // left out: not even its shadow
                (drawn ? ArmorDrawn : ArmorLeftOut).Add(r.name);
                return;
            }
            if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) return; // what VR draws anyway
            if (IsBackpack(r.name) ? !Backpack : IsArm(r.name)) return; // the backpack left out keeps its shadow
            Set(r, r.enabled, ShadowCastingMode.On);
        }

        private static void Set(Renderer r, bool enabled, ShadowCastingMode mode)
        {
            Changed[r] = new Was { Enabled = r.enabled, Mode = r.shadowCastingMode };
            r.enabled = enabled;
            r.shadowCastingMode = mode;
            if (enabled) _drawn++;
        }

        /// <summary>Back to vanilla: every renderer as the game had it.</summary>
        private static void Hide()
        {
            if (!_done) return;
            foreach (var pair in Changed)
            {
                if (!pair.Key) continue;
                pair.Key.enabled = pair.Value.Enabled;
                pair.Key.shadowCastingMode = pair.Value.Mode;
            }
            Changed.Clear();
            _done = null;
            Plugin.Log.LogInfo("PlayerBody: body drawn as a shadow only");
        }

        /// <summary>Something held in a hand ("rightHandHeld", "leftHandHeld" and below), not worn.</summary>
        private static bool IsHeld(Transform t)
        {
            for (; t; t = t.parent)
                if (t.name.IndexOf("Held", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // The arms and hands. VR draws its own hands mesh (char_VR_Hands_GEO, VRPlayerAdapter.VRPlayerHands): hands and
        // wrists only, stiff at the wrist. The outfit switches the body's own arms meshes on, which VR only lets cast
        // a shadow: the bare arms with hands for short sleeves (char_arms_GEO1, VRPlayerAdapter.defaultPlayerArms),
        // just the hands for long sleeves (char_arms_JustHands_GEO). The one the outfit wears is drawn instead of the
        // VR hands (same skeleton, so the same finger poses; both drawn, the hands would overlap): forearms with short
        // sleeves, hands that bend at the wrist with either. In the world view only: the inventory draws the VR hands
        // on its own layer. The watch is its own object on the left hand bone, drawn either way.
        private static VRPlayerAdapter _armsAdapter;
        private static Renderer _justHands, _drawnArms;
        private static bool _armsSwitchedOn; // the bare arms switched back on here (see UpdateArms)

        /// <summary>Every frame.</summary>
        public static void UpdateArms()
        {
            if (!ForestVR.Enabled || BodyView.Shown) return; // the scene's body view has the arms meanwhile
            var adapter = LocalPlayer.vrAdapter;
            if (adapter != _armsAdapter)
            {
                _armsAdapter = adapter;
                _drawnArms = null; // a new player: as the game set it up
                _armsSwitchedOn = false;
                _justHands = adapter ? FindRenderer(adapter, "char_arms_JustHands_GEO") : null;
            }
            if (!adapter || !adapter.VRPlayerHands) return;
            var hands = adapter.VRPlayerHands;
            var vr = LocalPlayer.vrPlayerControl;
            Renderer worn = null;
            if (Visible && _done && hands.gameObject.activeInHierarchy && vr && !vr.useGhostMode && LocalPlayer.Inventory
                && LocalPlayer.Inventory.CurrentView == TheForest.Items.Inventory.PlayerInventory.PlayerViews.World)
            {
                var arms = adapter.defaultPlayerArms;
                if (arms && _justHands && !arms.gameObject.activeSelf && !_justHands.gameObject.activeSelf
                    && arms.transform.parent && arms.transform.parent.gameObject.activeInHierarchy)
                {
                    // Neither switched on: the outfit chose the bare arms (it switches one or the other on), and VR's
                    // setup switched them off afterwards (VRPlayerAdapter.ConvertToVR). Back on, while drawn here.
                    arms.gameObject.SetActive(true);
                    _armsSwitchedOn = true;
                }
                if (arms && arms.gameObject.activeInHierarchy) worn = arms;
                else if (_justHands && _justHands.gameObject.activeInHierarchy) worn = _justHands;
            }
            if (!worn && _armsSwitchedOn && adapter.defaultPlayerArms)
            {
                adapter.defaultPlayerArms.gameObject.SetActive(false); // as VR's setup had them
                _armsSwitchedOn = false;
            }
            if (worn == _drawnArms) return;
            if (_drawnArms) _drawnArms.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            if (worn) worn.shadowCastingMode = ShadowCastingMode.On;
            hands.shadowCastingMode = worn ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            _drawnArms = worn;
        }

        private static Renderer FindRenderer(VRPlayerAdapter adapter, string name)
        {
            if (adapter.PlayerMeshesToDisable == null) return null;
            foreach (var go in adapter.PlayerMeshesToDisable)
            {
                if (!go) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r && r.name == name) return r;
            }
            return null;
        }

        private static bool IsBackpack(string name) => name.IndexOf("backpack", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Armour: "...Armor..."/"...Armour..." (bone, lizard, stealth, creepy armour) and the leaf armour's leaves and
        /// ferns, which hang from the bones ("leaves_04/fern_03").
        /// </summary>
        private static bool IsArmor(Transform t)
        {
            for (; t; t = t.parent)
            {
                var name = t.name.ToLowerInvariant();
                if (name.Contains("armor") || name.Contains("armour") || name.StartsWith("leaves") || name.StartsWith("fern")) return true;
                if (name.StartsWith("char_") || name == "clothingparent") return false; // up to the bone or the clothes group
            }
            return false;
        }

        /// <summary>
        /// Armour on an arm or a leg (not the chest, shoulders or body): by its name (".._L_Arm", "..TopLeg..") or the
        /// bone it hangs from (char_LeftForeArm, char_RightUpLeg...).
        /// </summary>
        private static bool OnLimb(Transform t)
        {
            if (IsLimbName(t.name.ToLowerInvariant().Replace("armor", "").Replace("armour", ""))) return true;
            for (var bone = t.parent; bone; bone = bone.parent)
            {
                var name = bone.name.ToLowerInvariant();
                if (!name.StartsWith("char_")) continue;
                return IsLimbName(name);
            }
            return false;
        }

        private static bool IsLimbName(string lower) =>
            lower.Contains("arm") || lower.Contains("hand") || lower.Contains("leg") || lower.Contains("foot");

        /// <summary>Bare arms and hands ("char_arms_GEO1", "...JustHands..."), and sleeves made for the VR arms.</summary>
        private static bool IsArm(string name)
        {
            var lower = name.ToLowerInvariant().Replace("armor", "");
            return lower.Contains("arm") || lower.Contains("hand");
        }
    }
}
