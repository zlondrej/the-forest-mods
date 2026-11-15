using System;
using System.Collections.Generic;
using System.Reflection;
using Bolt;
using HarmonyLib;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;
using ArrowBonus = TheForest.Items.Craft.WeaponStatUpgrade.Types;

namespace ArrowFixes
{
    /// <summary>
    /// Arrow basket (ItemHolder of type Arrow) with arrow types: looking at it, Rotate picks an arrow type (like the
    /// campfire picks food); "put" puts in an arrow of that type and "take" takes one out. The HUD prompt shows the
    /// type's icon (<see cref="BasketIcon"/>) and the arrows in the basket show their real type (<see cref="BasketVisuals"/>).
    ///
    /// Each slot's arrow properties live in the basket's StorageItemBonusManager (saved with the world). Vanilla only
    /// uses it in single player; here it is also used in multiplayer, with the host as the authority:
    ///  - put / take requests go to the host in the vanilla ItemHolderAddItem / ItemHolderTakeItem events, with the
    ///    arrow type in their spare ContentType / ContentInfo / ContentValue fields (vanilla ignores those for ItemHolder);
    ///  - the host hands out taken arrows with their properties (PlayerAddItem) and sends every client the basket's
    ///    slot list after each change (ItemHolderAddItem host -> clients, which vanilla clients ignore for ItemHolder);
    ///  - a client asks for the slot list when it looks at a basket (an ItemHolderAddItem without Target, which a vanilla
    ///    host ignores) and only switches to typed baskets once a host with the mod answered; otherwise vanilla behaviour.
    /// </summary>
    [HarmonyPatch] // class-level attribute required, or the class processor skips the method patches
    internal static class ArrowBasket
    {
        // Message ids in ContentType (vanilla never uses these values for ItemHolder events).
        private const int MsgRequest = 0x4A7F01, MsgHeader = 0x4A7F02, MsgSlot = 0x4A7F03, MsgPut = 0x4A7F04, MsgTake = 0x4A7F05;

        private static readonly Dictionary<int, ArrowKey> Selection = new Dictionary<int, ArrowKey>();
        private static readonly HashSet<int> HostHasMod = new HashSet<int>(); // client: baskets the host answered for
        private static readonly MethodInfo PlayOneshot = AccessTools.Method(typeof(FMODCommon), "PlayOneshot", new[] { typeof(string), typeof(Transform) });
        private static Func<bool> _rotate, _take, _put;

        private static bool Prepare()
        {
            _rotate = InputCompat.ButtonDown("Rotate");
            _take = InputCompat.ButtonDown("Take");
            _put = InputCompat.ButtonDown("Craft"); // the basket's "add" button
            return true;
        }

        internal static bool IsArrowBasket(ItemHolder b) => b && b._type == HolderTypes.Arrow && b._bonusManager;

        // ================================================================== local player (replaces ItemHolder.Update)

        [HarmonyPatch(typeof(ItemHolder), "Update")]
        [HarmonyPrefix]
        private static bool UpdatePrefix(ItemHolder __instance)
        {
            var b = __instance;
            if (!IsArrowBasket(b)) return true;
            bool authority = !BoltNetwork.isRunning || BoltNetwork.isServer;
            if (!authority && !HostHasMod.Contains(b.GetInstanceID()))
            {
                BasketIcon.Hide();
                return true; // vanilla until a host with the mod answered
            }
            if (BoltNetwork.isClient) b.Items = b.state.ItemCount;
            else if (BoltNetwork.isServer) b.state.ItemCount = b.Items;
            EnsureSlots(b);

            var available = Available(b);
            var key = Selected(b, available);
            if (_rotate() && available.Count > 1)
            {
                key = Next(key, available);
                Selection[b.GetInstanceID()] = key;
                LocalPlayer.Sfx.PlayWhoosh();
                Dbg.Log($"basket: selected {key}");
            }

            bool canTake = b.Items > 0 && IndexOf(b, key) >= 0;
            bool canPut = b.Items < b.ItemsRender.Length && Carries(b._itemid, key);

            if (canTake && _take())
            {
                if (authority)
                {
                    if (LocalPlayer.Inventory.AddItem(b._itemid, 1, false, false, Properties(key)) && TryTake(b, key))
                        LocalPlayer.Sfx.PlayItemCustomSfx(b._itemid);
                }
                else
                {
                    var e = ItemHolderTakeItem.Create(GlobalTargets.OnlyServer);
                    e.Target = b.entity;
                    e.Player = LocalPlayer.Entity;
                    e.ContentType = MsgTake;
                    e.ContentValue = Encode(key);
                    e.Send();
                    LocalPlayer.Sfx.PlayItemCustomSfx(b._itemid);
                }
                Dbg.Log($"basket: take {key}");
            }
            else if (canPut && _put())
            {
                ArrowCyclingPatches.SortByKey(b._itemid, key); // the inventory removes its "next" arrow: make it this type
                if (LocalPlayer.Inventory.RemoveItem(b._itemid))
                {
                    PlayPutSound(b);
                    if (authority)
                    {
                        if (!TryAdd(b, key)) LocalPlayer.Inventory.AddItem(b._itemid, 1, false, false, Properties(key));
                    }
                    else
                    {
                        var e = ItemHolderAddItem.Create(GlobalTargets.OnlyServer);
                        e.Target = b.entity;
                        e.Player = LocalPlayer.Entity;
                        e.ContentType = MsgPut;
                        e.ContentInfo = Encode(key);
                        e.Send();
                    }
                    Dbg.Log($"basket: put {key}");
                }
            }

            var widget = Scene.HudGui.HolderWidgets[(int)b._type];
            widget.Show(canTake, canPut, b.TakeIcon.transform);
            BasketIcon.Show(widget, key);
            return false;
        }

        [HarmonyPatch(typeof(ItemHolder), "GrabEnter")]
        [HarmonyPostfix]
        private static void GrabEnterPostfix(ItemHolder __instance)
        {
            if (!IsArrowBasket(__instance)) return;
            BasketVisuals.Refresh(__instance);
            if (!BoltNetwork.isClient || !__instance.entity || !__instance.entity.isAttached) return;
            // Ask the host for the slot list (no Target: a host without the mod ignores this).
            var e = ItemHolderAddItem.Create(GlobalTargets.OnlyServer);
            e.Player = __instance.entity;
            e.ContentType = MsgRequest;
            e.Send();
        }

        [HarmonyPatch(typeof(ItemHolder), "GrabExit")]
        [HarmonyPostfix]
        private static void GrabExitPostfix(ItemHolder __instance)
        {
            if (IsArrowBasket(__instance)) BasketIcon.Hide();
        }

        // ================================================================== authority (single player or host)

        private static bool TryAdd(ItemHolder b, ArrowKey key)
        {
            EnsureSlots(b);
            if (b.Items >= b.ItemsRender.Length) return false;
            b._bonusManager.SetItemProperties(b.Items, b._itemid, Properties(key));
            b.Items++;
            b.ItemsRender[b.Items - 1].SetActive(true);
            Changed(b);
            return true;
        }

        private static bool TryTake(ItemHolder b, ArrowKey key)
        {
            EnsureSlots(b);
            int i = IndexOf(b, key), top = b.Items - 1;
            if (i < 0) return false;
            var slots = b._bonusManager._itemsProperties;
            var swap = slots[i];
            slots[i] = slots[top];
            slots[top] = swap;
            b.Items--;
            b.ItemsRender[b.Items].SetActive(false);
            b._bonusManager.UnsetItemProperties(b.Items);
            Changed(b);
            return true;
        }

        private static void Changed(ItemHolder b)
        {
            if (BoltNetwork.isServer && b.entity && b.entity.isAttached)
            {
                b.state.ItemCount = b.Items;
                b.entity.Freeze(false);
                SendSlots(b, null);
            }
            BasketVisuals.Refresh(b);
        }

        /// <summary>Host: the basket's count and slot types, to one client or (null) to all.</summary>
        private static void SendSlots(ItemHolder b, BoltConnection to)
        {
            Send(b, to, MsgHeader, b.Items);
            for (int i = 0; i < b.Items; i++)
                Send(b, to, MsgSlot, (i << 16) | Encode(SlotKey(b, i)));
        }

        private static void Send(ItemHolder b, BoltConnection to, int msg, int info)
        {
            var e = to != null ? ItemHolderAddItem.Create(to) : ItemHolderAddItem.Create(GlobalTargets.AllClients);
            e.Target = b.entity;
            e.ContentType = msg;
            e.ContentInfo = info;
            e.Send();
        }

        /// <summary>Host: give a client the arrow it took (with its properties).</summary>
        private static void Give(BoltEntity player, ArrowKey key, int itemId)
        {
            if (!player) return;
            var e = player.isOwner ? PlayerAddItem.Create(GlobalTargets.OnlySelf) : PlayerAddItem.Create(player.source);
            var p = Properties(key);
            e.Player = player;
            e.ItemId = itemId;
            e.Amount = 1;
            e.ActiveBonus = (int)p.ActiveBonus;
            e.ActiveBonusValue = p.ActiveBonusValue;
            e.Send();
        }

        [HarmonyPatch(typeof(CoopServerCallbacks), nameof(CoopServerCallbacks.OnEvent), typeof(ItemHolderAddItem))]
        [HarmonyPrefix]
        private static bool ServerAddEvent(ItemHolderAddItem evnt)
        {
            switch (evnt.ContentType)
            {
                case MsgRequest:
                {
                    var b = evnt.Player ? evnt.Player.GetComponentInChildren<ItemHolder>() : null;
                    if (IsArrowBasket(b)) { EnsureSlots(b); SendSlots(b, evnt.RaisedBy); }
                    return false;
                }
                case MsgPut:
                {
                    var b = evnt.Target ? evnt.Target.GetComponentInChildren<ItemHolder>() : null;
                    if (!IsArrowBasket(b)) return true;
                    var key = Decode(evnt.ContentInfo);
                    if (!TryAdd(b, key)) Give(evnt.Player, key, b._itemid); // full: give it back
                    return false;
                }
                default:
                    return true;
            }
        }

        [HarmonyPatch(typeof(CoopServerCallbacks), nameof(CoopServerCallbacks.OnEvent), typeof(ItemHolderTakeItem))]
        [HarmonyPrefix]
        private static bool ServerTakeEvent(ItemHolderTakeItem evnt)
        {
            if (evnt.ContentType != MsgTake) return true;
            var b = evnt.Target ? evnt.Target.GetComponentInChildren<ItemHolder>() : null;
            if (!IsArrowBasket(b)) return true;
            var key = Decode(evnt.ContentValue);
            if (TryTake(b, key)) Give(evnt.Player, key, b._itemid);
            return false;
        }

        // Host: keep slots in step when a player without the mod adds / takes the vanilla way (type unknown: plain).
        [HarmonyPatch(typeof(ItemHolder), nameof(ItemHolder.AddItemMP))]
        [HarmonyPrefix]
        private static void VanillaAddPrefix(ItemHolder __instance, out int __state) => __state = __instance.Items;

        [HarmonyPatch(typeof(ItemHolder), nameof(ItemHolder.AddItemMP))]
        [HarmonyPostfix]
        private static void VanillaAddPostfix(ItemHolder __instance, int __state)
        {
            if (!IsArrowBasket(__instance) || __instance.Items <= __state) return;
            EnsureSlots(__instance);
            __instance._bonusManager.UnsetItemProperties(__instance.Items - 1);
            Changed(__instance);
        }

        [HarmonyPatch(typeof(ItemHolder), nameof(ItemHolder.TakeItemMP))]
        [HarmonyPrefix]
        private static void VanillaTakePrefix(ItemHolder __instance, out int __state) => __state = __instance.Items;

        [HarmonyPatch(typeof(ItemHolder), nameof(ItemHolder.TakeItemMP))]
        [HarmonyPostfix]
        private static void VanillaTakePostfix(ItemHolder __instance, int __state)
        {
            if (!IsArrowBasket(__instance) || __instance.Items >= __state) return;
            EnsureSlots(__instance);
            __instance._bonusManager.UnsetItemProperties(__instance.Items);
            Changed(__instance);
        }

        // ================================================================== clients

        [HarmonyPatch(typeof(CoopClientCallbacks), nameof(CoopClientCallbacks.OnEvent), typeof(ItemHolderAddItem))]
        [HarmonyPrefix]
        private static bool ClientAddEvent(ItemHolderAddItem evnt)
        {
            if (evnt.ContentType != MsgHeader && evnt.ContentType != MsgSlot) return true;
            var b = evnt.Target ? evnt.Target.GetComponentInChildren<ItemHolder>() : null;
            if (!IsArrowBasket(b)) return false;
            EnsureSlots(b);
            if (evnt.ContentType == MsgHeader)
            {
                if (HostHasMod.Add(b.GetInstanceID())) Dbg.Log("basket: host has the mod, typed basket on");
                for (int i = Mathf.Max(0, evnt.ContentInfo); i < b._bonusManager._itemsProperties.Length; i++)
                    b._bonusManager.UnsetItemProperties(i);
            }
            else
            {
                int index = evnt.ContentInfo >> 16;
                if (index >= 0 && index < b._bonusManager._itemsProperties.Length)
                    b._bonusManager.SetItemProperties(index, b._itemid, Properties(Decode(evnt.ContentInfo & 0xFFFF)));
            }
            BasketVisuals.Refresh(b);
            return false;
        }

        [HarmonyPatch(typeof(ItemHolder), "ItemCountChangedMP")]
        [HarmonyPostfix]
        private static void CountChangedPostfix(ItemHolder __instance)
        {
            if (IsArrowBasket(__instance)) BasketVisuals.Refresh(__instance);
        }

        [HarmonyPatch(typeof(ItemHolder), "OnDeserialized")]
        [HarmonyPostfix]
        private static void DeserializedPostfix(ItemHolder __instance)
        {
            if (IsArrowBasket(__instance)) BasketVisuals.Refresh(__instance);
        }

        // ================================================================== helpers

        internal static ArrowKey SlotKey(ItemHolder b, int i) => ArrowKey.Of(b._bonusManager.GetItemProperties(i));

        private static void EnsureSlots(ItemHolder b)
        {
            var m = b._bonusManager;
            int n = b.ItemsRender.Length;
            if (m._itemsProperties == null) { m.Init(n); return; }
            if (m._itemsProperties.Length < n)
            {
                var grown = new TheForest.Buildings.World.StorageItemBonusManager.ItemData[n];
                Array.Copy(m._itemsProperties, grown, m._itemsProperties.Length);
                m._itemsProperties = grown;
            }
        }

        private static int IndexOf(ItemHolder b, ArrowKey key)
        {
            for (int i = b.Items - 1; i >= 0; i--)
                if (SlotKey(b, i) == key) return i;
            return -1;
        }

        private static bool Carries(int itemId, ArrowKey key)
        {
            if (!LocalPlayer.Inventory.InventoryItemViewsCache.TryGetValue(itemId, out var views) || views == null) return false;
            foreach (var v in views)
                if (v && v.gameObject.activeSelf && ArrowKey.Of(v.Properties) == key) return true;
            return false;
        }

        /// <summary>Types you carry plus types in the basket.</summary>
        private static HashSet<ArrowKey> Available(ItemHolder b)
        {
            var set = new HashSet<ArrowKey>();
            for (int i = 0; i < b.Items; i++) set.Add(SlotKey(b, i));
            if (LocalPlayer.Inventory.InventoryItemViewsCache.TryGetValue(b._itemid, out var views) && views != null)
                foreach (var v in views)
                    if (v && v.gameObject.activeSelf) set.Add(ArrowKey.Of(v.Properties));
            return set;
        }

        private static ArrowKey Selected(ItemHolder b, HashSet<ArrowKey> available)
        {
            if (Selection.TryGetValue(b.GetInstanceID(), out var key) && (available.Count == 0 || available.Contains(key))) return key;
            if (Selection.ContainsKey(b.GetInstanceID())) key = Next(key, available); // the selected type ran out
            else if (b.Items > 0) key = SlotKey(b, b.Items - 1);
            else
            {
                var next = BowUtil.CurrentArrow(LocalPlayer.Inventory, b._itemid);
                key = next ? ArrowKey.Of(next.Properties) : ArrowKey.Of(null);
            }
            Selection[b.GetInstanceID()] = key;
            return key;
        }

        private static ArrowKey Next(ArrowKey current, HashSet<ArrowKey> available)
        {
            var order = ArrowKey.CycleOrder;
            int start = Array.IndexOf(order, current);
            for (int step = 1; step <= order.Length; step++)
            {
                var candidate = order[((start < 0 ? -1 : start) + step) % order.Length];
                if (available.Contains(candidate)) return candidate;
            }
            return current;
        }

        private static void PlayPutSound(ItemHolder b)
        {
            if (!string.IsNullOrEmpty(b.addItemEvent) && PlayOneshot != null)
                PlayOneshot.Invoke(null, new object[] { b.addItemEvent, b.transform });
            else
                LocalPlayer.Sfx.PlayPutDown(b.gameObject);
        }

        /// <summary>Arrow type as a 16-bit number: low byte upgrade + 1, high byte stored base + 1.</summary>
        private static int Encode(ArrowKey k) => (((int)k.Bonus + 1) & 0xFF) | ((((int)k.Base + 1) & 0xFF) << 8);

        private static ArrowKey Decode(int code) =>
            new ArrowKey((ArrowBonus)((code & 0xFF) - 1), (ArrowBonus)(((code >> 8) & 0xFF) - 1));

        internal static ItemProperties Properties(ArrowKey k)
        {
            var p = new ItemProperties { ActiveBonus = k.Bonus };
            p.ActiveBonusValue = ArrowUtil.IsCoating(k.Bonus) && ArrowUtil.IsBase(k.Base) ? (int)k.Base : 0f;
            return p;
        }
    }
}
