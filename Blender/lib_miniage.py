"""Shared helpers for building the MiniAge low-poly model set in Blender.

Every builder returns a list of (object, material_name) parts sitting on Z=0
with +Z up and +Y forward, exactly like the original MiniAge FBX exports.
"""

import bpy
from mathutils import Vector

# ----------------------------------------------------------------- materials

PALETTE = {
    "WoodDark":   ((0.24, 0.14, 0.07), 0.0, 0.80),
    "Wood":       ((0.45, 0.28, 0.13), 0.0, 0.75),
    "WoodLight":  ((0.63, 0.44, 0.22), 0.0, 0.70),
    "Leather":    ((0.30, 0.17, 0.10), 0.0, 0.70),
    "Plaster":    ((0.82, 0.78, 0.68), 0.0, 0.85),
    "PlasterWarm":((0.74, 0.62, 0.44), 0.0, 0.85),
    "Stone":      ((0.52, 0.52, 0.50), 0.0, 0.90),
    "StoneDark":  ((0.36, 0.36, 0.35), 0.0, 0.90),
    "RoofRed":    ((0.52, 0.16, 0.11), 0.0, 0.75),
    "RoofGreen":  ((0.20, 0.34, 0.18), 0.0, 0.75),
    "RoofBlue":   ((0.17, 0.26, 0.42), 0.0, 0.75),
    "Thatch":     ((0.72, 0.58, 0.26), 0.0, 0.85),
    "Leaf":       ((0.19, 0.42, 0.15), 0.0, 0.80),
    "LeafLight":  ((0.30, 0.55, 0.20), 0.0, 0.80),
    "Soil":       ((0.30, 0.21, 0.13), 0.0, 0.95),
    "Crop":       ((0.72, 0.62, 0.18), 0.0, 0.80),
    "Gold":       ((0.83, 0.62, 0.16), 1.0, 0.30),
    "Steel":      ((0.66, 0.69, 0.74), 1.0, 0.25),
    "Iron":       ((0.28, 0.29, 0.32), 1.0, 0.45),
    "Fur":        ((0.55, 0.36, 0.20), 0.0, 0.85),
    "FurDark":    ((0.34, 0.22, 0.12), 0.0, 0.85),
    "Hide":       ((0.78, 0.70, 0.60), 0.0, 0.80),
    "Water":      ((0.18, 0.40, 0.60), 0.0, 0.25),
}


def mat(name):
    """Get-or-create a Principled material from PALETTE."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    color, metallic, rough = PALETTE[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*color, 1.0)
    return m


def assign(ob, material_name):
    ob.data.materials.clear()
    ob.data.materials.append(mat(material_name))
    for p in ob.data.polygons:
        p.use_smooth = False
    return ob


# ---------------------------------------------------------------- primitives

def mesh_obj(name, verts, faces, material, collection):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate(verbose=False)
    me.update()
    ob = bpy.data.objects.new(name, me)
    collection.objects.link(ob)
    return assign(ob, material)


def box(name, center, size, material, collection):
    sx, sy, sz = (s / 2.0 for s in size)
    cx, cy, cz = center
    v = [(cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz),
         (cx + sx, cy + sy, cz - sz), (cx - sx, cy + sy, cz - sz),
         (cx - sx, cy - sy, cz + sz), (cx + sx, cy - sy, cz + sz),
         (cx + sx, cy + sy, cz + sz), (cx - sx, cy + sy, cz + sz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
         (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return mesh_obj(name, v, f, material, collection)


def prism(name, center, size, material, collection, taper=0.6):
    """Box whose top face is scaled by `taper` in X/Y - walls, towers, roofs."""
    sx, sy, sz = (s / 2.0 for s in size)
    cx, cy, cz = center
    tx, ty = sx * taper, sy * taper
    v = [(cx - sx, cy - sy, cz - sz), (cx + sx, cy - sy, cz - sz),
         (cx + sx, cy + sy, cz - sz), (cx - sx, cy + sy, cz - sz),
         (cx - tx, cy - ty, cz + sz), (cx + tx, cy - ty, cz + sz),
         (cx + tx, cy + ty, cz + sz), (cx - tx, cy + ty, cz + sz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
         (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return mesh_obj(name, v, f, material, collection)


def cylinder(name, center, radius, height, material, collection, sides=8,
             radius_top=None):
    rt = radius if radius_top is None else radius_top
    cx, cy, cz = center
    v, f = [], []
    for i in range(sides):
        a = 2 * 3.141592653589793 * i / sides
        v.append((cx + radius * _cos(a), cy + radius * _sin(a), cz))
    for i in range(sides):
        a = 2 * 3.141592653589793 * i / sides
        v.append((cx + rt * _cos(a), cy + rt * _sin(a), cz + height))
    for i in range(sides):
        j = (i + 1) % sides
        f.append((i, j, sides + j, sides + i))
    f.append(tuple(range(sides - 1, -1, -1)))
    f.append(tuple(range(sides, sides * 2)))
    return mesh_obj(name, v, f, material, collection)


def cone(name, center, radius, height, material, collection, sides=6):
    """Roof / spike / tree canopy - apex at +Z."""
    cx, cy, cz = center
    v, f = [], []
    for i in range(sides):
        a = 2 * 3.141592653589793 * i / sides
        v.append((cx + radius * _cos(a), cy + radius * _sin(a), cz))
    apex = len(v)
    v.append((cx, cy, cz + height))
    for i in range(sides):
        f.append((i, (i + 1) % sides, apex))
    f.append(tuple(range(sides - 1, -1, -1)))
    return mesh_obj(name, v, f, material, collection)


def gable_roof(name, center, size, material, collection, overhang=0.0):
    """Ridge along X. Rectangular base with a triangular prism on top."""
    sx = size[0] / 2.0 + overhang
    sy = size[1] / 2.0 + overhang
    cx, cy, cz = center
    v = [(cx - sx, cy - sy, cz), (cx + sx, cy - sy, cz),
         (cx + sx, cy + sy, cz), (cx - sx, cy + sy, cz),
         (cx - sx, cy, cz + size[2]), (cx + sx, cy, cz + size[2])]
    f = [(0, 3, 2, 1), (0, 1, 5, 4), (3, 4, 5, 2), (1, 2, 5), (0, 4, 3)]
    return mesh_obj(name, v, f, material, collection)


def pyramid_roof(name, center, size, material, collection, overhang=0.0):
    sx = size[0] / 2.0 + overhang
    sy = size[1] / 2.0 + overhang
    cx, cy, cz = center
    v = [(cx - sx, cy - sy, cz), (cx + sx, cy - sy, cz),
         (cx + sx, cy + sy, cz), (cx - sx, cy + sy, cz),
         (cx, cy, cz + size[2])]
    f = [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]
    return mesh_obj(name, v, f, material, collection)


def plate(name, center, size, material, collection):
    """Flat quad on the ground - fields, water, foundations."""
    sx, sy = size[0] / 2.0, size[1] / 2.0
    cx, cy, cz = center
    v = [(cx - sx, cy - sy, cz), (cx + sx, cy - sy, cz),
         (cx + sx, cy + sy, cz), (cx - sx, cy + sy, cz)]
    return mesh_obj(name, v, [(0, 1, 2, 3)], material, collection)


def _cos(a):
    import math
    return math.cos(a)


def _sin(a):
    import math
    return math.sin(a)


# ------------------------------------------------------------------- fitting

def bounds(objects):
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objects:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                mn[i] = min(mn[i], w[i])
                mx[i] = max(mx[i], w[i])
    return mn, mx


def fit_to(objects, target_size):
    """Scale + translate `objects` so their bounding box matches target_size
    exactly. Scaling is per-axis so a model always fills the same volume as
    the MiniAge original it replaces - prefab scale stays untouched."""
    mn, mx = bounds(objects)
    cur = Vector((mx.x - mn.x, mx.y - mn.y, mx.z - mn.z))
    target = Vector(target_size)
    # guard against a zero-sized source axis collapsing the mesh
    s = [1.0, 1.0, 1.0]
    for i in range(3):
        if cur[i] > 1e-6 and target[i] > 1e-6:
            s[i] = target[i] / cur[i]

    pivot = Vector((mn.x, mn.y, mn.z))
    for o in objects:
        o.location = Vector(((o.location.x - pivot.x) * s[0],
                             (o.location.y - pivot.y) * s[1],
                             (o.location.z - pivot.z) * s[2]))
        o.scale = (o.scale[0] * s[0], o.scale[1] * s[1], o.scale[2] * s[2])
    bpy.context.view_layer.update()
    return s


def drop_to_ground(objects, z=0.0):
    mn, _ = bounds(objects)
    d = z - mn.z
    for o in objects:
        o.location.z += d
    bpy.context.view_layer.update()


def bake_xform(objects):
    """Fold location + scale into the mesh data and reset every object to a
    clean identity transform. Keeps the saved files tidy and makes the exported
    FBX carry no odd pivots. Normals are recalculated afterwards so winding
    survives the non-uniform fit."""
    import bmesh
    for o in objects:
        me = o.data
        sx, sy, sz = o.scale
        lx, ly, lz = o.location
        if (sx, sy, sz) != (1.0, 1.0, 1.0) or (lx, ly, lz) != (0.0, 0.0, 0.0):
            for v in me.vertices:
                v.co = Vector((v.co.x * sx + lx, v.co.y * sy + ly,
                               v.co.z * sz + lz))
            o.location = (0.0, 0.0, 0.0)
            o.scale = (1.0, 1.0, 1.0)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        me.update()
    bpy.context.view_layer.update()
