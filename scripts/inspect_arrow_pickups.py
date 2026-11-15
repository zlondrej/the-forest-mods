"""Dump PickUp MonoBehaviours (incl. children) on arrow-related prefabs from The Forest's resources.assets.

Usage: python -I scripts/inspect_arrow_pickups.py [name-substring ...]
"""
import struct
import os
import sys
import UnityPy

GAME_DIR = os.environ.get("GAME_DIR") or sys.exit("Set GAME_DIR to The Forest install folder")
GAME_DATA = os.path.join(GAME_DIR, "TheForest_Data")
NAME_FILTER = [s.lower() for s in (sys.argv[1:] or ["fakearrow", "fakebolt"])]

env = UnityPy.load(os.path.join(GAME_DATA, "resources.assets"))


def comp_ptrs(go):
    for comp in go.m_Components:
        yield comp.component if hasattr(comp, "component") else comp


def dump_go(go, depth):
    ind = "   " * depth
    print(f"{ind}== '{go.m_Name}'")
    transform = None
    for ptr in comp_ptrs(go):
        try:
            c = ptr.deref()
        except Exception as e:  # noqa: BLE001
            print(f"{ind}   <unresolvable component> {e}")
            continue
        tname = c.type.name
        if tname in ("Transform", "RectTransform"):
            transform = c.read()
            continue
        if tname != "MonoBehaviour":
            continue
        mb = c.read(check_read=False)
        script = mb.m_Script.deref().read() if mb.m_Script else None
        sname = script.m_ClassName if script else "?"
        raw = c.get_raw_data()
        print(f"{ind}   MonoBehaviour {sname}: {len(raw)} bytes")
        if sname == "PickUp":
            # Header: m_GameObject PPtr(12) + m_Enabled(4, aligned) + m_Script PPtr(12) + m_Name string
            off = 12 + 4 + 12
            (nlen,) = struct.unpack_from("<i", raw, off)
            off += 4 + nlen
            off = (off + 3) & ~3
            # PickUp fields: _amount (RandomRange: 2 ints), _itemId, _bonusItems[] (each: RandomRange + int)
            amin, amax, item_id, nbonus = struct.unpack_from("<iiii", raw, off)
            off += 16 + nbonus * 12
            active_bonus, bonus_val = struct.unpack_from("<if", raw, off)
            print(f"{ind}     _amount={amin}..{amax} _itemId={item_id} _bonusItems={nbonus}")
            print(f"{ind}     _properties._activeBonus={active_bonus} _activeBonusValue={bonus_val}")
    if transform is not None:
        for child in transform.m_Children:
            cgo = child.deref().read().m_GameObject.deref().read()
            dump_go(cgo, depth + 1)


for obj in env.objects:
    if obj.type.name != "GameObject":
        continue
    go = obj.read()
    if any(f in go.m_Name.lower() for f in NAME_FILTER):
        tr = None
        for ptr in comp_ptrs(go):
            if ptr.deref().type.name == "Transform":
                tr = ptr.deref().read()
        # only roots
        if tr is not None and tr.m_Father.path_id == 0:
            dump_go(go, 0)
