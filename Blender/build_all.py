"""Driver: builds every MiniAge model, saves one .blend per model plus one
gallery .blend containing all of them laid out on a grid, and exports FBX
for Unity. Run from Blender:

    exec(compile(open(r"F:\mini age\Blender\\build_all.py").read(),
                 "build_all.py", "exec"))
"""

import bpy
import os
import sys
import importlib
from mathutils import Vector

HERE = r"F:\mini age\Blender"
OUT = os.path.join(HERE, "Models")
FBX_OUT = os.path.join(HERE, "FBX")
os.makedirs(OUT, exist_ok=True)
os.makedirs(FBX_OUT, exist_ok=True)

if HERE not in sys.path:
    sys.path.insert(0, HERE)
import lib_miniage
import build_models
importlib.reload(lib_miniage)
importlib.reload(build_models)
from lib_miniage import bake_xform, bounds, drop_to_ground, fit_to

ORDER = ["HomeSite", "Barracks", "Market", "Farm", "GoldMine", "LumberMill",
         "Tower", "Wall", "Tree", "Deer", "Horse", "Sword", "Knife", "Axe",
         "Shovel"]


# --------------------------------------------------------------- scene utils

def wipe_objects():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def wipe_collections():
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)


def new_collection(name):
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    return c


def build(name, coll):
    """Build one model into `coll`, fitted to its original bbox."""
    parts = build_models.BUILDERS[name](coll)
    tx, ty, tz, tminz = build_models.TARGET_SIZES[name]
    fit_to(parts, (tx, ty, tz))
    mn, mx = bounds(parts)
    # re-centre on X/Y, land exactly on the original min Z
    for o in parts:
        o.location.x -= (mn.x + mx.x) / 2.0
        o.location.y -= (mn.y + mx.y) / 2.0
    bpy.context.view_layer.update()
    mn, mx = bounds(parts)
    d = tminz - mn.z
    for o in parts:
        o.location.z += d
    bpy.context.view_layer.update()
    bake_xform(parts)
    return parts


def stats(parts, target):
    mn, mx = bounds(parts)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in parts)
    verts = sum(len(o.data.vertices) for o in parts)
    size = [mx[i] - mn[i] for i in range(3)]
    err = max(abs(size[i] - target[i]) / max(target[i], 1e-9) * 100 for i in range(3))
    return {"size": [round(s, 4) for s in size],
            "target": list(target),
            "size_err_pct": round(err, 3),
            "minZ": round(mn.z, 4),
            "tris": tris, "verts": verts, "parts": len(parts)}


def export_fbx(parts, path):
    for o in bpy.data.objects:
        o.select_set(False)
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True,
                             apply_unit_scale=True, bake_space_transform=False)


# --------------------------------------------------- 1. individual .blend files

results = {}
for name in ORDER:
    wipe_objects()
    wipe_collections()
    coll = new_collection(name)
    parts = build(name, coll)
    target = build_models.TARGET_SIZES[name][:3]
    results[name] = stats(parts, target)
    results[name]["blend"] = os.path.join(OUT, name + ".blend")
    bpy.ops.wm.save_as_mainfile(filepath=results[name]["blend"])
    export_fbx(parts, os.path.join(FBX_OUT, name + ".fbx"))

print("=== INDIVIDUAL MODELS ===")
for n in ORDER:
    r = results[n]
    print("%-11s size=%-22s err=%5.2f%%  tris=%5d  parts=%2d  %s"
          % (n, r["size"], r["size_err_pct"], r["tris"], r["parts"],
             os.path.basename(r["blend"])))

# ------------------------------------------------------ 2. gallery .blend file

wipe_objects()
wipe_collections()
scene_coll = new_collection("MiniAge_Model_Set")

# ground
gc = new_collection("_Stage")
me = bpy.data.meshes.new("Ground")
me.from_pydata([(-30, -30, 0), (30, -30, 0), (30, 30, 0), (-30, 30, 0)],
               [], [(0, 1, 2, 3)])
me.update()
ground = bpy.data.objects.new("Ground", me)
gc.objects.link(ground)
lib_miniage.assign(ground, "Soil")
ground.scale = (1, 1, 1)

# camera + key light
cam_data = bpy.data.cameras.new("Camera")
cam_data.lens = 55
cam = bpy.data.objects.new("Camera", cam_data)
gc.objects.link(cam)
cam.location = (0, -22, 15)
cam.rotation_euler = (1.10, 0.0, 0.0)
bpy.context.scene.camera = cam

light_data = bpy.data.lights.new("KeyLight", type='SUN')
light_data.energy = 4.0
light = bpy.data.objects.new("KeyLight", light_data)
gc.objects.link(light)
light.rotation_euler = (0.9, 0.2, 0.4)

# grid layout: 5 columns, buildings / nature / tools in rows
COLS = 5
GAP = 4.5
placed = []
for i, name in enumerate(ORDER):
    row = i // COLS
    col = i % COLS
    coll = new_collection(name)
    parts = build(name, coll)
    ox = (col - (COLS - 1) / 2.0) * GAP
    oy = -row * GAP
    for o in parts:
        o.location.x += ox
        o.location.y += oy
    placed.append((name, ox, oy))

bpy.context.view_layer.update()
gallery = os.path.join(HERE, "MiniAge_Models.blend")
bpy.ops.wm.save_as_mainfile(filepath=gallery)
print("=== GALLERY ===")
print(gallery, len(placed), "models")
for n, ox, oy in placed:
    print("  %-11s at (%.1f, %.1f)" % (n, ox, oy))
