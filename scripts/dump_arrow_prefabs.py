"""Compare the fired-arrow prefabs (Arrow / ArrowBone / ArrowModern): hierarchy, local transforms, colliders and
ArrowDamage.damage. Used to check that a coated bone/modern arrow behaves like the vanilla wooden one apart from damage.

Usage: python -I scripts/dump_arrow_prefabs.py [TheForest_Data|TheForestVR_Data]
"""
import os
import struct
import sys

import UnityPy

GAME_DIR = os.environ.get("GAME_DIR") or sys.exit("Set GAME_DIR to The Forest install folder")
DATA = sys.argv[1] if len(sys.argv) > 1 else "TheForest_Data"
ROOTS = {"Arrow", "ArrowBone", "ArrowModern"}

env = UnityPy.load(os.path.join(GAME_DIR, DATA, "globalgamemanagers.assets"))


def comps(go):
    for c in go.m_Components:
        yield (c.component if hasattr(c, "component") else c).deref()


def header_len(raw):
    off = 12 + 4 + 12
    (nlen,) = struct.unpack_from("<i", raw, off)
    return (off + 4 + nlen + 3) & ~3


def v3(v):
    return f"({v.x:+.3f},{v.y:+.3f},{v.z:+.3f})"


def describe(c):
    t = c.type.name
    if t == "MonoBehaviour":
        mb = c.read(check_read=False)
        name = mb.m_Script.deref().read().m_ClassName
        if name == "ArrowDamage":
            raw = c.get_raw_data()
            off = header_len(raw)
            # layers(4) MyPickUp(12) parent(12) PhysicBody(12) spearType flintLockAmmoType crossbowBoltType (aligned bools) damage
            off += 4 + 12 * 3 + 4 * 3
            (damage,) = struct.unpack_from("<i", raw, off)
            return f"ArrowDamage(damage={damage})"
        return name
    if t in ("BoxCollider", "CapsuleCollider", "SphereCollider"):
        col = c.read()
        trig = " trigger" if col.m_IsTrigger else ""
        if t == "BoxCollider":
            return f"BoxCollider{trig} center={v3(col.m_Center)} size={v3(col.m_Size)}"
        if t == "CapsuleCollider":
            return f"CapsuleCollider{trig} center={v3(col.m_Center)} r={col.m_Radius:.3f} h={col.m_Height:.3f} dir={col.m_Direction}"
        return f"SphereCollider{trig} center={v3(col.m_Center)} r={col.m_Radius:.3f}"
    if t == "Rigidbody":
        rb = c.read()
        return f"Rigidbody(mass={rb.m_Mass})"
    return None


def dump(go, depth):
    tr = next(c for c in comps(go) if c.type.name == "Transform").read()
    parts = [d for d in (describe(c) for c in comps(go) if c.type.name != "Transform") if d]
    print(f"{'  ' * depth}{go.m_Name} pos={v3(tr.m_LocalPosition)} scale={v3(tr.m_LocalScale)}  {', '.join(parts)}")
    for ch in tr.m_Children:
        dump(ch.deref().read().m_GameObject.deref().read(), depth + 1)


for obj in env.objects:
    if obj.type.name != "GameObject":
        continue
    go = obj.read()
    if go.m_Name not in ROOTS:
        continue
    tr = next(c for c in comps(go) if c.type.name == "Transform").read()
    if tr.m_Father.path_id != 0:
        continue
    if not any(c.type.name == "MonoBehaviour" and describe(c) == "arrowTrajectory" for c in comps(go)):
        continue
    print("=" * 20)
    dump(go, 0)
