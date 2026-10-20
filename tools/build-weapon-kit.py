"""Weapon look kit (modular weapons §10, §22): unit-sized barrel, mantlet, collar, fin, laser head, missile cell hatch
and fixed axial receiver pieces that the game scales and combines per part.

blender --background --python tools/build-weapon-kit.py -- --root <repo>

Writes the editable source art/blender/weapon_kit.blend and assets/weapons/weapon_kit.glb. Every piece is one unit
wide, its back at the origin and its length running along Blender +Y (game forward, -Z). Materials are named
kit_armour / kit_dark / kit_glow / kit_lens; the game replaces them with the ship palette.
"""
import argparse
import math
import sys
from pathlib import Path
import bpy
import bmesh

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
root = Path(args.root)

bpy.ops.wm.read_factory_settings(use_empty=True)
collection = bpy.data.collections.new('weapon kit')
bpy.context.scene.collection.children.link(collection)

def material(name, color, emission=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    if emission:
        bsdf.inputs['Emission Color'].default_value = (*emission, 1)
        bsdf.inputs['Emission Strength'].default_value = 2.0
    return m

ARMOUR = material('kit_armour', (.42, .45, .5))
DARK = material('kit_dark', (.12, .13, .15))
GLOW = material('kit_glow', (.3, .06, .03), (.95, .24, .07))
LENS = material('kit_lens', (.04, .22, .3), (.12, .65, .85))

def piece(name, build):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    bm = bmesh.new()
    build(bm, obj)
    bm.to_mesh(mesh)
    bm.free()
    return obj

def mat_index(obj, m):
    if m.name not in [s.name for s in obj.data.materials]:
        obj.data.materials.append(m)
    return [s.name for s in obj.data.materials].index(m.name)

def box(bm, obj, m, x0, x1, y0, y1, z0, z1, bevel=0.0):
    """Axis box from (x0,y0,z0) to (x1,y1,z1); Blender +Y is the weapon's forward."""
    geom = bmesh.ops.create_cube(bm, size=1)['verts']
    for v in geom:
        v.co.x = x0 if v.co.x < 0 else x1
        v.co.y = y0 if v.co.y < 0 else y1
        v.co.z = z0 if v.co.z < 0 else z1
    faces = list({f for v in geom for f in v.link_faces})
    for f in faces:
        f.material_index = mat_index(obj, m)
    if bevel > 0:
        edges = list({e for v in geom for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=2, affect='EDGES', profile=.5)

def cylinder(bm, obj, m, radius, y0, y1, segments=16, x=0.0, z=0.0):
    ring = bmesh.ops.create_circle(bm, cap_ends=True, radius=radius, segments=segments)['verts']
    for v in ring:
        cx, cz = v.co.x, v.co.y
        v.co.x, v.co.y, v.co.z = cx + x, y0, cz + z
    faces = list({f for v in ring for f in v.link_faces})
    ext = bmesh.ops.extrude_face_region(bm, geom=faces)
    moved = [g for g in ext['geom'] if isinstance(g, bmesh.types.BMVert)]
    for v in moved:
        v.co.y = y1
    for f in {f for v in ring + moved for f in v.link_faces}:
        f.material_index = mat_index(obj, m)

# Earth rail: two heavy rails in a closed shroud over a dark slot.
piece('kit_rail_earth', lambda bm, o: (
    box(bm, o, ARMOUR, -.5, -.12, 0, 1, -.42, .42, .03),
    box(bm, o, ARMOUR, .12, .5, 0, 1, -.42, .42, .03),
    box(bm, o, DARK, -.12, .12, 0, .985, -.3, .3),
    box(bm, o, ARMOUR, -.5, .5, 0, .2, .38, .5, .02)))
# Mars rail: thin open rails, frame rods underneath.
piece('kit_rail_mars', lambda bm, o: (
    box(bm, o, ARMOUR, -.42, -.18, 0, 1, -.3, .3, .02),
    box(bm, o, ARMOUR, .18, .42, 0, 1, -.3, .3, .02),
    box(bm, o, DARK, -.62, -.52, 0, .7, -.45, -.3),
    box(bm, o, DARK, .52, .62, 0, .7, -.45, -.3)))
# Round jacket and insulator collar.
piece('kit_jacket', lambda bm, o: cylinder(bm, o, ARMOUR, .5, 0, 1, 20))
piece('kit_collar', lambda bm, o: cylinder(bm, o, DARK, .55, 0, 1, 20))
# Earth mantlet: chamfered armour face across the mount.
piece('kit_mantlet_earth', lambda bm, o: (
    box(bm, o, ARMOUR, -.5, .5, 0, 1, -.5, .5, .08),
    box(bm, o, DARK, -.46, .46, .95, 1.0, -.44, .44)))
# Mars heat sink: thin glowing plate on a dark stem.
piece('kit_fin_mars', lambda bm, o: (
    box(bm, o, GLOW, -.06, .06, 0, 1, .2, 1, .01),
    box(bm, o, DARK, -.08, .08, .1, .9, 0, .2)))
# Laser head: short body, cooling fins and a lens at the muzzle.
def laser(bm, o):
    cylinder(bm, o, ARMOUR, .5, 0, .8, 20)
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        x, z = math.cos(ang) * .5, math.sin(ang) * .5
        box(bm, o, DARK, x - .05, x + .05, .1, .7, z - .05, z + .05)
    cylinder(bm, o, LENS, .36, .8, 1, 20)
piece('kit_laser_head', laser)
# Missile cell hatch: framed square lid with a hinge bar (top at +Z).
piece('kit_cell_hatch', lambda bm, o: (
    box(bm, o, DARK, -.5, .5, -.5, .5, 0, .06),
    box(bm, o, ARMOUR, -.42, .42, -.42, .42, .04, .1, .02),
    box(bm, o, DARK, -.4, .4, .42, .48, .06, .12)))
# Fixed axial receiver: tapered armoured housing with rail guides.
piece('kit_axial_receiver', lambda bm, o: (
    box(bm, o, ARMOUR, -.5, .5, 0, .7, -.45, .45, .06),
    box(bm, o, ARMOUR, -.35, .35, .7, 1, -.32, .32, .04),
    box(bm, o, DARK, -.12, .12, .2, 1.0, .3, .5)))

for obj in collection.objects:
    obj.data.update()
(root / 'art/blender').mkdir(parents=True, exist_ok=True)
(root / 'assets/weapons').mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(root / 'art/blender/weapon_kit.blend'))
bpy.ops.object.select_all(action='DESELECT')
for obj in collection.objects:
    obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(root / 'assets/weapons/weapon_kit.glb'), export_format='GLB', use_selection=True,
                          export_apply=True, export_yup=True)
print('weapon kit:', ', '.join(sorted(o.name for o in collection.objects)))
