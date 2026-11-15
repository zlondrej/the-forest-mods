"""Render the arrow models with their cloth wraps, to check the generated wraps (mod/ArrowFixes/ClothWrapSpecs.cs)
without starting the game.

Rows: wooden arrow with the game's own cloth (reference), bone and modern arrows with the generated wraps.
Columns: whole arrow, tip close-up from the side, tip close-up from above at an angle.
Models get the cloth-arrow tint, as in game.

Usage:
  dotnet run --project scripts/ClothPreview -- <dir>          # writes the generated wraps as OBJ
  python -I scripts/render_cloth_preview.py <dir>             # reads them, writes <dir>/cloth_preview.png
Needs GAME_DIR, UnityPy, numpy and Pillow.
"""
import math
import os
import sys

import numpy as np
import UnityPy
from PIL import Image, ImageDraw
from UnityPy.helpers.MeshHelper import MeshHandler

GAME_DIR = os.environ.get("GAME_DIR") or sys.exit("Set GAME_DIR to The Forest install folder")
OUT = sys.argv[1] if len(sys.argv) > 1 else sys.exit("Pass the directory with the generated OBJ files")
DATA = sys.argv[2] if len(sys.argv) > 2 else "TheForest_Data"
INV_PATH = "INVENTORY/Weapons/Arrows/Arrows_Inv"
FIRE_TINT = np.array([1.0, 0.544, 0.544])  # BowFireMat _Color, applied to coated bone/modern models by the mod
SS = 2  # supersampling


# ---------------------------------------------------------------- game assets
def comps(go):
    for c in go.m_Components:
        yield (c.component if hasattr(c, "component") else c).deref()


def path_of(tr):
    names = []
    while True:
        names.append(tr.m_GameObject.deref().read().m_Name)
        if tr.m_Father.path_id == 0:
            return "/".join(reversed(names))
        tr = tr.m_Father.deref().read()


def material_info(mat_ptr):
    mat = mat_ptr.deref().read()
    tex = None
    for name, env in mat.m_SavedProperties.m_TexEnvs:
        if name == "_MainTex" and env.m_Texture.path_id:
            tex = np.asarray(env.m_Texture.deref().read().image.convert("RGB"), dtype=np.float32) / 255.0
    color = np.ones(3, dtype=np.float32)
    for name, c in mat.m_SavedProperties.m_Colors:
        if name == "_Color":
            color = np.array([c.r, c.g, c.b], dtype=np.float32)
    return tex, color


def mesh_arrays(mesh_ptr):
    h = MeshHandler(mesh_ptr.deref().read())
    h.process()
    v = np.array(h.m_Vertices, dtype=np.float32).reshape(-1, 3)
    n = np.array(h.m_Normals, dtype=np.float32).reshape(-1, 3)
    uv = np.array(h.m_UV0, dtype=np.float32).reshape(-1, 2)
    tris = np.array(h.get_triangles()[0], dtype=np.int64).reshape(-1, 3)
    return v, n, uv, tris


def load_inventory_arrow():
    """{child name: (vertices, normals, uvs, triangles, texture, color)} for the inventory arrow and its children."""
    env = UnityPy.load(os.path.join(GAME_DIR, DATA, "globalgamemanagers.assets"))
    for obj in env.objects:
        if obj.type.name != "Transform":
            continue
        tr = obj.read()
        if not path_of(tr).endswith(INV_PATH):
            continue
        parts = {}
        for t in [tr] + [c.deref().read() for c in tr.m_Children]:
            go = t.m_GameObject.deref().read()
            mesh = mat = None
            for c in comps(go):
                if c.type.name == "MeshFilter":
                    mesh = c.read().m_Mesh
                elif c.type.name == "MeshRenderer":
                    mat = c.read().m_Materials[0]
            if mesh and mesh.path_id and mat:
                parts["Arrow" if t is tr else go.m_Name] = mesh_arrays(mesh) + material_info(mat)
        return parts
    sys.exit(f"{INV_PATH} not found in {DATA}")


def load_obj(path):
    v, vt, vn, f = [], [], [], []
    for line in open(path):
        p = line.split()
        if not p:
            continue
        if p[0] == "v":
            v.append([float(x) for x in p[1:4]])
        elif p[0] == "vt":
            vt.append([float(x) for x in p[1:3]])
        elif p[0] == "vn":
            vn.append([float(x) for x in p[1:4]])
        elif p[0] == "f":
            f.append([int(x.split("/")[0]) - 1 for x in p[1:4]])
    return (np.array(v, np.float32), np.array(vn, np.float32), np.array(vt, np.float32), np.array(f, np.int64))


# ---------------------------------------------------------------- rendering
def rot(yaw, pitch):
    """Camera rotation: yaw around the arrow (Y), then pitch around X. Rows are camera right, up, forward."""
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]], np.float32)
    rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]], np.float32)
    return rx @ ry


def render(meshes, y_range, yaw, pitch, size):
    """meshes: list of (v, n, uv, tris, tex, color). The arrow's Y axis is drawn horizontally (tip to the right)."""
    W, H = size[0] * SS, size[1] * SS
    img = np.zeros((H, W, 3), np.float32)
    img[:] = (0.16, 0.17, 0.19)
    zbuf = np.full((H, W), np.inf, np.float32)
    R = rot(yaw, pitch)
    light = np.array([0.4, 0.7, -0.6], np.float32)
    light /= np.linalg.norm(light)
    y0, y1 = y_range
    scale = W / (y1 - y0)

    for v, n, uv, tris, tex, color in meshes:
        # Rotated arrow: Y (along the arrow) -> screen x, X -> screen y, Z -> depth (camera looks along +Z).
        p = (v - np.array([0, (y0 + y1) / 2, 0], np.float32)) @ R.T
        sx = p[:, 1] * scale + W / 2
        sy = H / 2 - p[:, 0] * scale
        sz = p[:, 2]
        nn = n @ R.T
        th, tw = tex.shape[:2]
        for a, b, c in tris:
            xs, ys = sx[[a, b, c]], sy[[a, b, c]]
            minx, maxx = int(max(0, np.floor(xs.min()))), int(min(W - 1, np.ceil(xs.max())))
            miny, maxy = int(max(0, np.floor(ys.min()))), int(min(H - 1, np.ceil(ys.max())))
            if minx > maxx or miny > maxy:
                continue
            den = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
            if abs(den) < 1e-12:
                continue
            gx, gy = np.meshgrid(np.arange(minx, maxx + 1) + 0.5, np.arange(miny, maxy + 1) + 0.5)
            w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / den
            w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / den
            w2 = 1 - w0 - w1
            inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not inside.any():
                continue
            z = w0 * sz[a] + w1 * sz[b] + w2 * sz[c]
            zb = zbuf[miny:maxy + 1, minx:maxx + 1]
            draw = inside & (z < zb)
            if not draw.any():
                continue
            u = w0 * uv[a, 0] + w1 * uv[b, 0] + w2 * uv[c, 0]
            vv = w0 * uv[a, 1] + w1 * uv[b, 1] + w2 * uv[c, 1]
            tx = (np.mod(u, 1.0) * (tw - 1)).astype(np.int64)
            ty = ((1 - np.mod(vv, 1.0)) * (th - 1)).astype(np.int64)
            albedo = tex[ty, tx] * color
            nrm = w0[..., None] * nn[a] + w1[..., None] * nn[b] + w2[..., None] * nn[c]
            nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True) + 1e-9
            nrm = np.where(nrm[..., 2:3] > 0, -nrm, nrm)  # light the side facing the camera
            shade = 0.38 + 0.8 * np.clip(nrm @ light, 0, 1)[..., None]
            out = img[miny:maxy + 1, minx:maxx + 1]
            out[draw] = np.clip(albedo * shade, 0, 1)[draw]
            zb[draw] = z[draw]
    im = Image.fromarray((img * 255).astype(np.uint8))
    return im.resize(size, Image.LANCZOS)


def main():
    parts = load_inventory_arrow()

    def part(name, tint=None):
        v, n, uv, tris, tex, color = parts[name]
        return v, n, uv, tris, tex, color * (tint if tint is not None else 1)

    cloth_tex, cloth_color = parts["ArrowCloth"][4], parts["ArrowCloth"][5]

    def generated(name):
        v, n, uv, tris = load_obj(os.path.join(OUT, name + ".obj"))
        return v, n, uv, tris, cloth_tex, cloth_color

    rows = [
        ("Wooden + game cloth (reference)", [part("Arrow", FIRE_TINT), part("ArrowCloth")], (0.95, 1.40)),
        ("Bone + generated cloth", [part("BoneAmmo", FIRE_TINT), generated("BoneArrowCloth")], (0.18, 0.66)),
        ("Modern + generated cloth", [part("ModernArrow", FIRE_TINT), generated("ModernArrowCloth")], (0.98, 1.45)),
    ]
    full_w, close_w, h = 760, 420, 210
    label_h = 26
    sheet = Image.new("RGB", (full_w + 2 * close_w, len(rows) * (h + label_h)), (30, 31, 35))
    draw = ImageDraw.Draw(sheet)
    for i, (title, meshes, tip) in enumerate(rows):
        ys = np.concatenate([m[0][:, 1] for m in meshes[:1]])
        full = (float(ys.min()) - 0.02, float(ys.max()) + 0.02)
        top = i * (h + label_h)
        draw.text((8, top + 6), title, fill=(230, 230, 230))
        sheet.paste(render(meshes, full, 0.0, 0.0, (full_w, h)), (0, top + label_h))
        sheet.paste(render(meshes, tip, 0.0, 0.0, (close_w, h)), (full_w, top + label_h))
        sheet.paste(render(meshes, tip, math.radians(55), math.radians(-30), (close_w, h)), (full_w + close_w, top + label_h))
        print("rendered", title, flush=True)
    path = os.path.join(OUT, "cloth_preview.png")
    sheet.save(path)
    print(path)


if __name__ == "__main__":
    main()
