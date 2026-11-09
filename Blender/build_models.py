"""Low-poly model set for MiniAge.

One builder per model found in F:\mini age\Assets. Each builder returns a list
of parts. TARGET_SIZES mirrors the bounding box of the original MiniAge FBX so
these can replace the originals without touching prefab scale.

Originals (size = X,Y,Z in Unity units, minZ = 0 except Market):
    Barracks   2.86 x 1.86 x 1.80      HomeSite   2.80 x 1.80 x 2.20
    Farm       1.70 x 1.87 x 1.25      LumberMill 2.20 x 1.60 x 1.10
    GoldMine   2.00 x 1.60 x 0.91      Market     2.20 x 1.80 x 1.15 (minZ .25)
    Tower      1.02 x 1.02 x 3.10      Wall       2.00 x 0.51 x 1.40
    Tree       4.31 x 4.18 x 5.67      Horse      1.41 x 5.68 x 4.82
    Sword      .012 x .002 x .044      Knife      3.352 x 0.464 x 0.10
    Axe        23.4 x 15.16 x 1.24     Shovel     2.83 x 0.63 x 14.42
"""

import bpy
from lib_miniage import (box, cone, cylinder, gable_roof, plate, prism,
                         pyramid_roof)

# name -> (target x, y, z, min_z)
#
# Buildings + Tree reproduce the ORIGINAL MiniAge bbox exactly, so they drop in
# 1:1 with no prefab scale change.
#
# Deer, Horse, Sword, Knife, Axe and Shovel do NOT: their original exports have
# broken unapplied transforms (deer 12.3x7.4x14.5, horse 4.8m tall, axe 23.4
# units wide, sword 44mm long). Those are given sane game-world sizes that line
# up with the 2.8m-wide HomeSite instead of being faithfully reproduced broken.
TARGET_SIZES = {
    # --- original bbox, kept ---
    "Barracks":  (2.86, 1.86, 1.80, 0.0),
    "Farm":      (1.70, 1.87, 1.25, 0.0),
    "GoldMine":  (2.00, 1.60, 0.91, 0.0),
    "HomeSite":  (2.80, 1.80, 2.20, 0.0),
    "LumberMill":(2.20, 1.60, 1.10, 0.0),
    "Market":    (2.20, 1.80, 1.15, 0.25),
    "Tower":     (1.02, 1.02, 3.10, 0.0),
    "Wall":      (2.00, 0.51, 1.40, 0.0),
    "Tree":      (4.31, 4.18, 5.67, 0.0),
    # --- sane game scale (original was broken) ---
    "Deer":      (1.30, 2.40, 1.70, 0.0),
    "Horse":     (1.05, 2.60, 2.25, 0.0),
    "Sword":     (0.055, 0.014, 1.05, 0.0),
    "Knife":     (0.42, 0.055, 0.11, 0.0),
    "Axe":       (0.12, 0.34, 0.78, 0.0),
    "Shovel":    (0.26, 0.09, 1.18, 0.0),
}


# ============================================================ BUILDINGS

def build_homesite(c):
    """Town hall / player home: two storeys, plaster + timber, red gable roof."""
    p = []
    p.append(box("HomeSite_Foundation", (0, 0, 0.09), (3.1, 2.1, 0.18), "StoneDark", c))
    p.append(box("HomeSite_LowerWall", (0, 0, 0.60), (2.7, 1.75, 0.72), "Plaster", c))
    # corner + cross timbers
    for sx in (-1.28, 1.28):
        for sy in (-0.80, 0.80):
            p.append(box("HomeSite_Post", (sx, sy, 0.60), (0.16, 0.16, 0.74), "WoodDark", c))
    p.append(box("HomeSite_Beam", (0, 0, 0.94), (2.74, 1.78, 0.10), "WoodDark", c))
    p.append(box("HomeSite_Floor2", (0, 0, 1.02), (2.7, 1.75, 0.10), "Wood", c))
    p.append(box("HomeSite_UpperWall", (0, 0, 1.42), (2.5, 1.55, 0.72), "PlasterWarm", c))
    # windows front + back
    for sx in (-0.7, 0.7):
        p.append(box("HomeSite_Window", (sx, -0.79, 1.44), (0.5, 0.06, 0.44), "WoodDark", c))
    p.append(box("HomeSite_Door", (0, -0.80, 0.42), (0.5, 0.10, 0.78), "WoodDark", c))
    # roof + chimney
    p.append(gable_roof("HomeSite_Roof", (0, 0, 1.80), (2.7, 1.75, 0.62), "RoofRed", c, overhang=0.22))
    p.append(box("HomeSite_Chimney", (0.95, 0.35, 1.95), (0.24, 0.24, 0.75), "StoneDark", c))
    return p


def build_barracks(c):
    """Wide low hall - sleeping quarters with a fenced yard."""
    p = []
    p.append(box("Barracks_Base", (0, 0, 0.08), (2.95, 1.95, 0.16), "StoneDark", c))
    p.append(box("Barracks_Hall", (0, 0, 0.52), (2.7, 1.7, 0.72), "Wood", c))
    for sx in (-1.3, 0.0, 1.3):
        p.append(box("Barracks_Post", (sx, -0.84, 0.52), (0.14, 0.14, 0.74), "WoodDark", c))
    p.append(box("Barracks_Plate", (0, 0, 0.92), (2.76, 1.76, 0.10), "WoodDark", c))
    p.append(gable_roof("Barracks_Roof", (0, 0, 0.96), (2.7, 1.7, 0.55), "RoofGreen", c, overhang=0.20))
    # fence in front
    for i in range(5):
        p.append(box("Barracks_FencePost", (-1.1 + i * 0.55, -1.15, 0.28),
                     (0.10, 0.10, 0.56), "WoodDark", c))
    p.append(box("Barracks_FenceRail", (0, -1.15, 0.44), (2.5, 0.06, 0.08), "Wood", c))
    # weapon rack
    p.append(box("Barracks_Rack", (1.0, -0.7, 1.05), (0.5, 0.14, 0.14), "WoodDark", c))
    for i in range(3):
        p.append(cylinder("Barracks_Spear", (0.82 + i * 0.18, -0.7, 1.12),
                          0.02, 0.7, "Iron", c, sides=4))
    return p


def build_farm(c):
    """Field plot: tilled soil rows with crop tufts and a small tool shed."""
    p = []
    p.append(box("Farm_Ground", (0, 0, 0.05), (1.85, 2.0, 0.10), "Soil", c))
    for i in range(4):
        y = -0.72 + i * 0.48
        p.append(box("Farm_Row", (0, y, 0.13), (1.65, 0.30, 0.08), "Soil", c))
        for k in range(5):
            p.append(cone("Farm_Crop", (-0.66 + k * 0.33, y, 0.17),
                          0.07, 0.22, "Crop", c, sides=4))
    # scarecrow
    p.append(cylinder("Farm_ScarecrowPole", (0.72, 0.82, 0.10), 0.03, 1.05, "WoodDark", c, sides=5))
    p.append(box("Farm_ScarecrowArms", (0.72, 0.82, 0.92), (0.55, 0.05, 0.05), "WoodDark", c))
    p.append(box("Farm_ScarecrowHead", (0.72, 0.82, 1.06), (0.20, 0.16, 0.20), "Thatch", c))
    return p


def build_goldmine(c):
    """Rocky mound with a mine entrance, timber frame and gold vein."""
    p = []
    p.append(cone("GoldMine_Rock", (0, 0, 0.0), 1.05, 1.35, "Stone", c, sides=7))
    p.append(cone("GoldMine_Rock2", (-0.55, 0.45, 0.0), 0.6, 0.9, "StoneDark", c, sides=6))
    p.append(cone("GoldMine_Rock3", (0.6, -0.4, 0.0), 0.55, 0.8, "StoneDark", c, sides=6))
    # tunnel mouth
    p.append(box("GoldMine_FrameL", (-0.34, -0.92, 0.34), (0.14, 0.16, 0.68), "WoodDark", c))
    p.append(box("GoldMine_FrameR", (0.34, -0.92, 0.34), (0.14, 0.16, 0.68), "WoodDark", c))
    p.append(box("GoldMine_Lintel", (0, -0.92, 0.72), (0.9, 0.18, 0.14), "WoodDark", c))
    p.append(box("GoldMine_Dark", (0, -0.88, 0.30), (0.62, 0.10, 0.60), "StoneDark", c))
    # cart + gold
    p.append(box("GoldMine_Cart", (0.95, -0.35, 0.20), (0.46, 0.34, 0.26), "Wood", c))
    p.append(cone("GoldMine_Gold", (0.95, -0.35, 0.33), 0.20, 0.16, "Gold", c, sides=6))
    p.append(box("GoldMine_Shaft", (0.0, 0.1, 1.42), (0.10, 0.10, 0.5), "Iron", c))
    return p


def build_lumbermill(c):
    """Sawmill: open timber shed, big saw blade, log pile."""
    p = []
    p.append(box("LumberMill_Base", (0, 0, 0.07), (2.3, 1.7, 0.14), "StoneDark", c))
    p.append(box("LumberMill_Deck", (0, 0, 0.18), (2.2, 1.6, 0.14), "Wood", c))
    # four corner posts + open sides
    for sx in (-1.0, 1.0):
        for sy in (-0.7, 0.7):
            p.append(box("LumberMill_Post", (sx, sy, 0.62), (0.14, 0.14, 0.88), "WoodDark", c))
    p.append(box("LumberMill_BeamF", (0, -0.72, 1.06), (2.2, 0.12, 0.12), "WoodDark", c))
    p.append(box("LumberMill_BeamB", (0, 0.72, 1.06), (2.2, 0.12, 0.12), "WoodDark", c))
    p.append(gable_roof("LumberMill_Roof", (0, 0, 1.12), (2.2, 1.6, 0.42), "WoodDark", c, overhang=0.24))
    # saw blade on a post
    p.append(box("LumberMill_SawPost", (0.45, -0.2, 0.52), (0.12, 0.12, 0.7), "WoodDark", c))
    p.append(cylinder("LumberMill_Saw", (0.45, -0.2, 0.86), 0.36, 0.05, "Steel", c, sides=10))
    # log pile
    for i in range(3):
        p.append(cylinder("LumberMill_Log", (-0.72, -0.45 + i * 0.34, 0.34),
                          0.15, 0.8, "WoodLight", c, sides=6))
    return p


def build_market(c):
    """Market stall: counter, striped awning, crates and produce. Sits on a
    low platform because the original model has minZ = 0.25."""
    p = []
    p.append(box("Market_Platform", (0, 0, -0.10), (2.3, 1.9, 0.2), "StoneDark", c))
    p.append(box("Market_Board", (0, 0, 0.05), (2.2, 1.8, 0.1), "Wood", c))
    p.append(box("Market_Counter", (0, -0.35, 0.30), (1.9, 0.55, 0.4), "Wood", c))
    p.append(box("Market_CounterTop", (0, -0.35, 0.52), (2.0, 0.62, 0.06), "WoodLight", c))
    for sx in (-0.9, 0.9):
        p.append(box("Market_Post", (sx, 0.6, 0.48), (0.12, 0.12, 0.86), "WoodDark", c))
    # awning: two sloped panels
    p.append(box("Market_AwningF", (0, -0.05, 0.80), (2.1, 1.0, 0.05), "RoofRed", c))
    p.append(box("Market_AwningB", (0, 0.75, 0.72), (2.1, 0.7, 0.05), "RoofRed", c))
    # goods
    for i, (x, m) in enumerate([(-0.6, "Crop"), (0.0, "Leaf"), (0.6, "Gold")]):
        p.append(cone("Market_Goods%d" % i, (x, -0.35, 0.55), 0.16, 0.16, m, c, sides=6))
    p.append(box("Market_Crate", (0.85, 0.5, 0.28), (0.32, 0.32, 0.36), "WoodLight", c))
    p.append(box("Market_Crate2", (-0.85, 0.55, 0.24), (0.28, 0.28, 0.28), "WoodLight", c))
    return p


def build_tower(c):
    """Defence tower: stone base, wooden upper, conical roof, banner."""
    p = []
    p.append(box("Tower_Base", (0, 0, 0.22), (1.06, 1.06, 0.44), "StoneDark", c))
    p.append(prism("Tower_Shaft", (0, 0, 1.15), (0.86, 0.86, 1.45), "Stone", c, taper=0.88))
    # arrow slits
    for sy in (-0.44, 0.44):
        p.append(box("Tower_Slit", (0, sy, 1.35), (0.14, 0.06, 0.5), "StoneDark", c))
    p.append(box("Tower_Battlement", (0, 0, 1.94), (1.10, 1.10, 0.12), "StoneDark", c))
    for sx in (-0.48, 0.48):
        for sy in (-0.48, 0.48):
            p.append(box("Tower_Merlon", (sx, sy, 2.10), (0.20, 0.20, 0.22), "Stone", c))
    p.append(cone("Tower_Roof", (0, 0, 2.20), 0.52, 0.72, "RoofBlue", c, sides=8))
    p.append(cylinder("Tower_Pole", (0, 0, 2.88), 0.03, 0.32, "WoodDark", c, sides=5))
    p.append(box("Tower_Banner", (0.18, 0, 2.98), (0.04, 0.34, 0.26), "RoofRed", c))
    return p


def build_wall(c):
    """Palisade section: stone footing with log wall and a walkway."""
    p = []
    p.append(box("Wall_Footing", (0, 0, 0.12), (2.05, 0.56, 0.24), "StoneDark", c))
    for i in range(7):
        x = -0.87 + i * 0.29
        p.append(cylinder("Wall_Log", (x, 0, 0.24), 0.11, 1.05, "Wood", c, sides=6))
    p.append(box("Wall_Rail", (0, -0.16, 1.06), (2.0, 0.10, 0.10), "WoodDark", c))
    p.append(box("Wall_Walk", (0, 0, 1.22), (2.02, 0.5, 0.08), "WoodLight", c))
    # support braces
    for x in (-0.8, 0.8):
        p.append(box("Wall_Brace", (x, 0.2, 0.7), (0.10, 0.10, 0.9), "WoodDark", c))
    return p


# ============================================================ NATURE / UNITS

def build_tree(c):
    """Stylised broadleaf: tapered trunk, three stacked canopy tiers."""
    p = []
    p.append(cylinder("Tree_Trunk", (0, 0, 0), 0.34, 1.9, "WoodDark", c, sides=7, radius_top=0.20))
    p.append(cone("Tree_RootA", (0.25, 0.1, 0), 0.14, 0.5, "WoodDark", c, sides=5))
    p.append(cone("Tree_RootB", (-0.22, -0.12, 0), 0.14, 0.45, "WoodDark", c, sides=5))
    p.append(cone("Tree_BranchA", (0.35, 0, 1.4), 0.09, 0.8, "WoodDark", c, sides=5))
    p.append(cone("Tree_BranchB", (-0.3, 0.2, 1.7), 0.08, 0.7, "WoodDark", c, sides=5))
    p.append(cone("Tree_CanopyLow", (0, 0, 1.5), 1.35, 1.5, "Leaf", c, sides=8))
    p.append(cone("Tree_CanopyMid", (0.15, 0.1, 2.5), 1.05, 1.4, "LeafLight", c, sides=8))
    p.append(cone("Tree_CanopyTop", (-0.1, -0.05, 3.5), 0.72, 1.2, "Leaf", c, sides=7))
    return p


def _quadruped(c, prefix, body, body_len, body_h, leg_h, leg_r, neck, head,
               tail, mane=None, antlers=False):
    """Shared 4-legged body so deer and horse stay consistent."""
    p = []
    p.append(box(prefix + "_Body", (0, 0, leg_h + body_h / 2),
                 (body, body_len, body_h), "Fur", c))
    p.append(prism(prefix + "_Chest", (0, -body_len / 2 + body * 0.2, leg_h + body_h * 0.6),
                   (body * 0.95, body * 0.95, body_h * 0.8), "Fur", c, taper=0.8))
    p.append(prism(prefix + "_Rump", (0, body_len / 2 - body * 0.25, leg_h + body_h * 0.62),
                   (body * 1.02, body * 1.05, body_h * 0.9), "FurDark", c, taper=0.7))
    for sx in (-body / 2 + leg_r, body / 2 - leg_r):
        for sy in (-body_len / 2 + leg_r * 1.4, body_len / 2 - leg_r * 1.6):
            p.append(box(prefix + "_Leg", (sx, sy, leg_h / 2),
                         (leg_r * 1.8, leg_r * 1.8, leg_h), "Hide", c))
            p.append(box(prefix + "_Hoof", (sx, sy, leg_r * 0.6),
                         (leg_r * 1.9, leg_r * 2.1, leg_r * 1.4), "WoodDark", c))
    p.append(box(prefix + "_Neck", (0, -body_len / 2 - neck * 0.2, leg_h + body_h + neck * 0.25),
                 (body * 0.55, neck * 1.1, neck * 1.2), "Fur", c))
    hy = -body_len / 2 - neck * 0.75
    hz = leg_h + body_h + neck * 0.55
    p.append(box(prefix + "_Head", (0, hy, hz), (body * 0.5, neck * 0.9, neck * 0.6), "Fur", c))
    p.append(box(prefix + "_Muzzle", (0, hy - neck * 0.5, hz - neck * 0.14),
                 (body * 0.32, neck * 0.45, neck * 0.34), "Hide", c))
    for sx in (-body * 0.22, body * 0.22):
        p.append(cone(prefix + "_Ear", (sx, hy + neck * 0.2, hz + neck * 0.28),
                      body * 0.10, neck * 0.3, "FurDark", c, sides=4))
    for sx in (-body * 0.22, body * 0.22):
        p.append(box(prefix + "_Eye", (sx, hy - neck * 0.35, hz + neck * 0.12),
                     (body * 0.07, body * 0.05, body * 0.07), "StoneDark", c))
    p.append(cone(prefix + "_Tail", (0, body_len / 2, leg_h + body_h * 0.75),
                  tail, tail * 2.2, "FurDark", c, sides=5))
    if mane:
        p.append(box(prefix + "_Mane", (0, -body_len / 2 + body * 0.15, leg_h + body_h + neck * 0.2),
                     (body * 0.28, neck * 1.0, neck * 0.9), mane, c))
    if antlers:
        for sx in (-body * 0.16, body * 0.16):
            p.append(cone(prefix + "_Antler", (sx, hy + neck * 0.15, hz + neck * 0.45),
                          body * 0.05, neck * 0.85, "WoodLight", c, sides=4))
            p.append(cone(prefix + "_AntlerT", (sx + body * 0.10, hy + neck * 0.3, hz + neck * 0.8),
                          body * 0.04, neck * 0.4, "WoodLight", c, sides=4))
    return p


def build_deer(c):
    """Deer: slim quadruped, antlers, white rump patch."""
    p = _quadruped(c, "Deer", body=0.46, body_len=1.30, body_h=0.52,
                   leg_h=0.78, leg_r=0.075, neck=0.46, head=0.3,
                   tail=0.07, mane=None, antlers=True)
    p.append(box("Deer_Rump", (0, 0.52, 0.92), (0.22, 0.12, 0.16), "Hide", c))
    for i in range(5):
        p.append(cone("Deer_Spot%d" % i, (-0.18 + (i % 3) * 0.18, 0.1 + i * 0.13, 1.02),
                      0.05, 0.03, "Hide", c, sides=4))
    return p


def build_horse(c):
    """Horse: heavy quadruped, mane and tail, taller legs."""
    p = _quadruped(c, "Horse", body=0.66, body_len=2.10, body_h=0.86,
                   leg_h=1.35, leg_r=0.11, neck=0.72, head=0.4,
                   tail=0.11, mane="FurDark", antlers=False)
    p.append(box("Horse_Saddle", (0, -0.05, 2.32), (0.56, 0.7, 0.16), "RoofRed", c))
    p.append(box("Horse_SaddlePad", (0, -0.05, 2.24), (0.62, 0.8, 0.08), "Leather", c))
    for sx in (-0.34, 0.34):
        p.append(cone("Horse_EarT", (sx, -1.28, 2.42), 0.07, 0.16, "FurDark", c, sides=4))
    return p


# ============================================================ TOOLS / WEAPONS

def build_sword(c):
    """Bladed weapon along +Z: diamond blade, guard, grip, pommel."""
    p = []
    st = [(0.030, 0.030, 0.010), (0.75, 0.026, 0.009), (0.88, 0.016, 0.006)]
    verts, faces = [], []
    for z, w, t in st:
        verts += [(w, 0, z), (0, t, z), (-w, 0, z), (0, -t, z)]
    for i in range(len(st) - 1):
        a, b = i * 4, (i + 1) * 4
        for k in range(4):
            k2 = (k + 1) % 4
            faces.append((a + k, a + k2, b + k2, b + k))
    last = (len(st) - 1) * 4
    tip = len(verts)
    verts.append((0, 0, 1.0))
    for k in range(4):
        faces.append((last + k, last + (k + 1) % 4, tip))
    faces.append((3, 2, 1, 0))
    from lib_miniage import mesh_obj
    p.append(mesh_obj("Sword_Blade", verts, faces, "Steel", c))
    p.append(box("Sword_Guard", (0, 0, 0.030), (0.16, 0.028, 0.016), "Gold", c))
    p.append(box("Sword_Grip", (0, 0, 0.014), (0.030, 0.026, 0.034), "WoodDark", c))
    p.append(box("Sword_Pommel", (0, 0, -0.002), (0.036, 0.032, 0.014), "Gold", c))
    return p


def build_knife(c):
    """Knife lying along X - the original export is blade-horizontal."""
    p = []
    from lib_miniage import mesh_obj
    st = [(-0.42, 0.055, 0.008), (0.16, 0.075, 0.010), (0.34, 0.055, 0.008)]
    verts, faces = [], []
    for x, w, t in st:
        verts += [(x, 0, w), (x, t, 0), (x, 0, -w), (x, -t, 0)]
    for i in range(len(st) - 1):
        a, b = i * 4, (i + 1) * 4
        for k in range(4):
            k2 = (k + 1) % 4
            faces.append((a + k, a + k2, b + k2, b + k))
    last = (len(st) - 1) * 4
    tip = len(verts)
    verts.append((0.46, 0, 0))
    for k in range(4):
        faces.append((last + k, last + (k + 1) % 4, tip))
    faces.append((3, 2, 1, 0))
    p.append(mesh_obj("Knife_Blade", verts, faces, "Steel", c))
    p.append(box("Knife_Guard", (0.34, 0, 0), (0.02, 0.13, 0.13), "WoodDark", c))
    p.append(box("Knife_Grip", (0.42, 0, 0), (0.16, 0.05, 0.05), "WoodDark", c))
    return p


def build_axe(c):
    """Axe. The original export is 23.4 x 15.2 units (a broken/unapplied
    transform) so the shape is built sane and scaled to the original box."""
    p = []
    p.append(cylinder("Axe_Handle", (0, 0, -0.5), 0.075, 2.0, "WoodLight", c, sides=6))
    from lib_miniage import mesh_obj
    # wedge-shaped blade
    verts = [(-0.05, -0.10, 1.30), (-0.05, 0.10, 1.30),
             (-0.05, -0.16, 1.62), (-0.05, 0.16, 1.62),
             (-0.02, -0.62, 1.50), (-0.02, 0.62, 1.50),
             (-0.02, -0.30, 1.78), (-0.02, 0.30, 1.78)]
    faces = [(0, 1, 3, 2), (2, 3, 7, 6), (6, 7, 5, 4), (4, 5, 1, 0),
             (2, 4, 0), (3, 1, 5), (6, 4, 2), (7, 3, 5), (0, 4, 6, 2), (1, 3, 7, 5)]
    p.append(mesh_obj("Axe_Blade", verts, faces, "Steel", c))
    p.append(box("Axe_Ferrule", (0, 0, 1.34), (0.12, 0.16, 0.16), "Iron", c))
    p.append(box("Axe_Grip", (0, 0, 1.06), (0.14, 0.14, 0.42), "WoodDark", c))
    return p


def build_shovel(c):
    """Shovel: D-handle shaft with a spade blade. Original export is rotated
    (14.4 tall on Z) so it is built standing and scaled to the original box."""
    p = []
    p.append(box("Shovel_Shaft", (0, 0, 1.0), (0.09, 0.09, 2.0), "WoodLight", c))
    # D handle
    p.append(box("Shovel_HandleL", (-0.14, 0, 2.10), (0.06, 0.09, 0.34), "Iron", c))
    p.append(box("Shovel_HandleR", (0.14, 0, 2.10), (0.06, 0.09, 0.34), "Iron", c))
    p.append(box("Shovel_HandleTop", (0, 0, 2.28), (0.34, 0.09, 0.06), "Iron", c))
    # spade
    from lib_miniage import mesh_obj
    verts = [(-0.22, -0.03, 0.10), (0.22, -0.03, 0.10),
             (-0.22, 0.03, 0.10), (0.22, 0.03, 0.10),
             (0.0, -0.03, -0.28), (0.0, 0.03, -0.28)]
    faces = [(0, 1, 3, 2), (0, 2, 5, 4), (1, 4, 5, 3), (0, 4, 1), (2, 3, 5)]
    p.append(mesh_obj("Shovel_Blade", verts, faces, "Iron", c))
    p.append(box("Shovel_Socket", (0, 0, 0.16), (0.16, 0.14, 0.2), "Iron", c))
    return p


BUILDERS = {
    "HomeSite": build_homesite,
    "Barracks": build_barracks,
    "Farm": build_farm,
    "GoldMine": build_goldmine,
    "LumberMill": build_lumbermill,
    "Market": build_market,
    "Tower": build_tower,
    "Wall": build_wall,
    "Tree": build_tree,
    "Deer": build_deer,
    "Horse": build_horse,
    "Sword": build_sword,
    "Knife": build_knife,
    "Axe": build_axe,
    "Shovel": build_shovel,
}
