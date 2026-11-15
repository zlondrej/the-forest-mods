"""Scan The Forest asset files for MonoBehaviours of given script classes and report where they live.

For arrowTrajectory, also follows the `_pickup` reference and decodes the PickUp's item id / ActiveBonus.

Usage: python -I scripts/scan_components.py <DataDirName> <ScriptClass> [ScriptClass ...]
  e.g. python -I scripts/scan_components.py TheForest_Data arrowTrajectory
"""
import glob
import os
import struct
import sys

import UnityPy

GAME_DIR = os.environ.get("GAME_DIR") or sys.exit("Set GAME_DIR to The Forest install folder")
data_dir = os.path.join(GAME_DIR, sys.argv[1])
wanted = set(sys.argv[2:])

files = sorted(glob.glob(os.path.join(data_dir, "*.assets")) + glob.glob(os.path.join(data_dir, "level*")))
files = [f for f in files if not f.endswith(".resS")]


def header_len(raw):
    off = 12 + 4 + 12  # m_GameObject PPtr, m_Enabled (aligned), m_Script PPtr
    (nlen,) = struct.unpack_from("<i", raw, off)
    off += 4 + nlen
    return (off + 3) & ~3


def script_name(obj):
    try:
        mb = obj.read(check_read=False)
        return mb.m_Script.deref().read().m_ClassName, mb
    except Exception:  # noqa: BLE001
        return None, None


def go_path(go):
    names = [go.m_Name]
    try:
        tr = next(p.deref().read() for p in comps(go) if p.deref().type.name == "Transform")
        while tr.m_Father.path_id != 0:
            tr = tr.m_Father.deref().read()
            names.append(tr.m_GameObject.deref().read().m_Name)
    except Exception:  # noqa: BLE001
        pass
    return "/".join(reversed(names))


def comps(go):
    for c in go.m_Components:
        yield c.component if hasattr(c, "component") else c


def pickup_props(go):
    """Find PickUp on go or its descendants; return (itemId, activeBonus)."""
    for p in comps(go):
        c = p.deref()
        if c.type.name == "MonoBehaviour":
            name, _ = script_name(c)
            if name == "PickUp":
                raw = c.get_raw_data()
                off = header_len(raw)
                _amin, _amax, item_id, nbonus = struct.unpack_from("<iiii", raw, off)
                off += 16 + nbonus * 12
                (bonus,) = struct.unpack_from("<i", raw, off)
                return item_id, bonus
        if c.type.name == "Transform":
            for ch in c.read().m_Children:
                r = pickup_props(ch.deref().read().m_GameObject.deref().read())
                if r:
                    return r
    return None


def pptr_name(assets_file, fid, pid):
    """Readable name of a same-file PPtr target (GameObject path, or 'Type on path' for components)."""
    if pid == 0:
        return "null"
    if fid != 0:
        return f"<external {fid}:{pid}>"
    try:
        o = assets_file.objects[pid]
        if o.type.name == "GameObject":
            return go_path(o.read())
        c = o.read(check_read=False) if o.type.name == "MonoBehaviour" else o.read()
        kind = script_name(o)[0] if o.type.name == "MonoBehaviour" else o.type.name
        return f"{kind} on {go_path(c.m_GameObject.deref().read())}"
    except Exception as e:  # noqa: BLE001
        return f"<{pid}: {e}>"


def read_pptr_list(raw, off):
    (n,) = struct.unpack_from("<i", raw, off)
    off += 4
    items = [struct.unpack_from("<iq", raw, off + 12 * i) for i in range(n)]
    return items, off + 12 * n


def decode_bonus_listener(name, mb, raw, off):
    af = mb.assets_file
    off += 12  # _itemView
    if name == "ActiveBonusListener":
        off += 12  # _heldItemIdentifier
    gos, off = read_pptr_list(raw, off)
    objs, off = read_pptr_list(raw, off)
    if name == "ActiveBonusListener":
        (bonus,) = struct.unpack_from("<i", raw, off)
        bonuses = [bonus]
        off += 4
    else:
        (n,) = struct.unpack_from("<i", raw, off)
        bonuses = list(struct.unpack_from(f"<{n}i", raw, off + 4))
        off += 4 + 4 * n
    (inverse,) = struct.unpack_from("<B", raw, off)
    gos_s = [pptr_name(af, f, p) for f, p in gos]
    objs_s = [pptr_name(af, f, p) for f, p in objs]
    return f"bonus={bonuses} inverse={inverse} targetGOs={gos_s} targetObjects={objs_s}"


for path in files:
    env = UnityPy.load(path)
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        name, mb = script_name(obj)
        if name not in wanted:
            continue
        go = mb.m_GameObject.deref().read()
        line = f"{os.path.basename(path)}: {name} on '{go_path(go)}'"
        raw = obj.get_raw_data()
        off = header_len(raw)
        if name == "arrowTrajectory":
            fid, pid = struct.unpack_from("<iq", raw, off)
            try:
                pgo = mb.assets_file.objects[pid].read() if fid == 0 else None
                line += f"  _pickup -> {pickup_props(pgo) if pgo else ('external', fid, pid)}"
            except Exception as e:  # noqa: BLE001
                line += f"  _pickup unresolved ({e})"
        elif name == "VRLightFire":
            fid, pid = struct.unpack_from("<iq", raw, off)
            (arrow_fire,) = struct.unpack_from("<B", raw, off + 12)
            target = "?"
            try:
                target = go_path(mb.assets_file.objects[pid].read()) if fid == 0 and pid else "null"
            except Exception:  # noqa: BLE001
                pass
            line += f"  targetFire='{target}' ArrowFire={arrow_fire}"
        elif name in ("ActiveBonusListener", "ActiveBonusListenerMulti"):
            line += "  " + decode_bonus_listener(name, mb, raw, off)
        print(line, flush=True)
