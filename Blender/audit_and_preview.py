"""Quality audit + preview renders for the MiniAge model set."""

import bpy
import bpy.ops
import os
import sys
import importlib
from mathutils import Vector

HERE = r"F:\mini age\Blender"
PREV = os.path.join(HERE, "preview")
os.makedirs(PREV, exist_ok=True)
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import build_models
importlib.reload(build_models)
from lib_miniage import bounds

import bmesh

ORDER = ["HomeSite", "Barracks", "Market", "Farm", "GoldMine", "LumberMill",
         "Tower", "Wall", "Tree", "Deer", "Horse", "Sword", "Knife", "Axe",
         "Shovel"]


def audit(parts, name):
    issues = []
    degenerate = loose = dup = no_mat = 0
    total_vol = 0.0
    for o in parts:
        me = o.data
        if len(o.material_slots) == 0 or me.materials[0] is None:
            no_mat += 1
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
        total_vol += bm.calc_volume(signed=True)
        bm.free()
        if tuple(round(s, 5) for s in o.scale) != (1.0, 1.0, 1.0):
            issues.append("dirty scale on %s" % o.name)
        if tuple(round(l, 5) for l in o.location) != (0.0, 0.0, 0.0):
            issues.append("dirty loc on %s" % o.name)
        if o.type != 'MESH':
            issues.append("non-mesh %s" % o.name)

    if degenerate:
        issues.append("%d degenerate faces" % degenerate)
    if loose:
        issues.append("%d loose verts" % loose)
    if dup:
        issues.append("%d duplicate faces" % dup)
    if no_mat:
        issues.append("%d parts without material" % no_mat)
    if total_vol < 0:
        issues.append("inverted normals (signed volume %.4f)" % total_vol)
    if not issues:
        issues.append("OK")
    return issues


def collect(name):
    c = bpy.data.collections.get(name)
    return list(c.objects) if c else []


# ------------------------------------------------------------ render setup
scene = bpy.context.scene
try:
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
except TypeError:
    scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 900
scene.render.resolution_y = 640
scene.render.resolution_percentage = 100
scene.render.film_transparent = False
scene.render.image_settings.file_format = 'PNG'
try:
    scene.eevee.taa_render_samples = 32
except Exception:
    pass
if scene.world is None:
    scene.world = bpy.data.worlds.new("World")
scene.world.use_nodes = True
bg = scene.world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.055, 0.06, 0.075, 1.0)
    bg.inputs[1].default_value = 1.0


def ensure_stage():
    for nm in ("_ShotCam", "_ShotKey", "_ShotFill"):
        if nm in bpy.data.objects:
            return
    cd = bpy.data.cameras.new("ShotCam")
    cd.lens = 50
    cam = bpy.data.objects.new("_ShotCam", cd)
    bpy.context.scene.collection.objects.link(cam)
    ld = bpy.data.lights.new("ShotKey", type='SUN')
    ld.energy = 4.5
    key = bpy.data.objects.new("_ShotKey", ld)
    bpy.context.scene.collection.objects.link(key)
    fd = bpy.data.lights.new("ShotFill", type='AREA')
    fd.energy = 400
    fd.size = 8
    fill = bpy.data.objects.new("_ShotFill", fd)
    bpy.context.scene.collection.objects.link(fill)


def frame_camera(objs, cam, pad=1.5):
    mn, mx = bounds(objs)
    ctr = (mn + mx) / 2.0
    size = max((mx - mn).x, (mx - mn).y, (mx - mn).z) * pad
    cam.location = ctr + Vector((0.0, -size * 1.5, size * 0.55))
    cam.rotation_euler = (1.15, 0.0, 0.0)
    d = (ctr - cam.location).normalized()
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    for l, off in ((bpy.data.objects["_ShotKey"], Vector((3, -4, 7))),
                   (bpy.data.objects["_ShotFill"], Vector((-5, -6, 3)))):
        l.location = ctr + off
        l.rotation_euler = (ctr - l.location).to_track_quat('-Z', 'Y').to_euler()


def render_to(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def shoot(parts, path, pad=1.35):
    """Render ONLY `parts` - every other object in the scene is hidden from
    the render, otherwise neighbouring models on the gallery grid show up in
    the shot and the QA image lies about what the model looks like."""
    keep = set(o.name for o in parts) | {"_ShotCam", "_ShotKey", "_ShotFill"}
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.hide_render = o.name not in keep
    frame_camera(parts, cam, pad=pad)
    render_to(path)


ensure_stage()
cam = bpy.data.objects["_ShotCam"]
scene.camera = cam

print("=== RENDERS ===")
# gallery shot: everything visible again
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.hide_render = False
allparts = []
for name in ORDER:
    allparts += collect(name)
frame_camera(allparts, cam, pad=1.25)
render_to(os.path.join(PREV, "00_gallery.png"))
print("00_gallery.png")

# per-model shots, each isolated to that one model
for i, name in enumerate(ORDER):
    parts = collect(name)
    shoot(parts, os.path.join(PREV, "%02d_%s.png" % (i + 1, name)), pad=1.5)
    print("%02d_%s.png" % (i + 1, name))

# leave the gallery visible again
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.hide_render = False
print("done")

