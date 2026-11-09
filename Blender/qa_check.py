"""QA pass over the DELIVERED files: opens every Models\\*.blend, checks
transforms, topology and bounding box, then restores the gallery scene."""

import bpy
import bpy.ops
import os
import sys
import importlib
import bmesh
from mathutils import Vector

HERE = r"F:\mini age\Blender"
MODELS = os.path.join(HERE, "Models")
GALLERY = os.path.join(HERE, "MiniAge_Models.blend")
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import build_models
importlib.reload(build_models)

ORDER = ["HomeSite", "Barracks", "Market", "Farm", "GoldMine", "LumberMill",
         "Tower", "Wall", "Tree", "Deer", "Horse", "Sword", "Knife", "Axe",
         "Shovel"]


def check(parts, name):
    issues = []
    degenerate = loose = dup = no_mat = nonident = 0
    vol = 0.0
    mn = Vector((1e9,) * 3)
    mx = Vector((-1e9,) * 3)
    for o in parts:
        if o.type != 'MESH':
            issues.append("non-mesh %s" % o.name)
            continue
        me = o.data
        if len(o.material_slots) == 0 or me.materials[0] is None:
            no_mat += 1
        if (tuple(round(v, 5) for v in o.scale) != (1.0, 1.0, 1.0) or
                tuple(round(v, 5) for v in o.location) != (0.0, 0.0, 0.0)):
            nonident += 1
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                mn[i] = min(mn[i], w[i])
                mx[i] = max(mx[i], w[i])
        bm = bmesh.new()
        bm.from_mesh(me)
        seen = set()
        for f in bm.faces:
            if f.calc_area() < 1e-10:
                degenerate += 1
            key = tuple(sorted(v.index for v in f.verts))
            if key in seen:
                dup += 1
            seen.add(key)
        loose += sum(1 for v in bm.verts if not v.link_faces)
        vol += bm.calc_volume(signed=True)
        bm.free()

    if degenerate:
        issues.append("%d degenerate" % degenerate)
    if loose:
        issues.append("%d loose verts" % loose)
    if dup:
        issues.append("%d dup faces" % dup)
    if no_mat:
        issues.append("%d no material" % no_mat)
    if nonident:
        issues.append("%d dirty transforms" % nonident)
    if vol < 0:
        issues.append("INVERTED NORMALS")
    if not issues:
        issues.append("OK")

    target = build_models.TARGET_SIZES[name]
    size = [mx[i] - mn[i] for i in range(3)]
    err = max(abs(size[i] - target[i]) / max(target[i], 1e-9) * 100 for i in range(3))
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in parts)
    return issues, size, err, tris


rows = []
for name in ORDER:
    path = os.path.join(MODELS, name + ".blend")
    if not os.path.exists(path):
        rows.append((name, "FILE MISSING", None, None, None))
        continue
    bpy.ops.wm.open_mainfile(filepath=path)
    parts = [o for o in bpy.data.objects if o.type == 'MESH']
    issues, size, err, tris = check(parts, name)
    rows.append((name, "; ".join(issues), size, err, tris))

print("=== QA ON DELIVERED .blend FILES ===")
bad = 0
for name, iss, size, err, tris in rows:
    if iss != "OK":
        bad += 1
    if size is None:
        print("%-11s %s" % (name, iss))
    else:
        print("%-11s %-6s size=%-24s bbox_err=%4.2f%% tris=%d"
              % (name, iss, size, err, tris))
print("files with issues:", bad, "/", len(rows))

bpy.ops.wm.open_mainfile(filepath=GALLERY)
print("restored gallery:", bpy.data.filepath)
