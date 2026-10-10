"""Blender 5.x: add concept-sheet visual detail to the saved ship sources (non-destructive, re-runnable).

blender --background --python-exit-code 1 --python tools/detail-ship-models.py -- --root <repo> [--ship mars_battleship]

Adds only static visual parts named 'DETAIL | ...' to '<id> | editable parts' (removing the previous detail
pass first): deck equipment rows, layered side armour, radiator banks, Mars sensor spires / glowing radiator
grids / bow emblem, interceptor canted fins and wing pods. Collision hulls, modules, weapons, sockets and
JSON data are untouched; export afterwards with tools/export-ship-models.py.
"""
import argparse
import json
import math
import random
import sys
import zlib
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
parser.add_argument('--ship')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
root = Path(args.root)
SHIPS = ['battleship', 'escort', 'interceptor', 'mars_battleship', 'mars_escort', 'mars_interceptor']


def g(p):
    """Godot right/up/aft -> Blender right/forward/up (glTF converts back)."""
    return Vector((p[0], -p[2], p[1]))


class Detailer:
    def __init__(self, key):
        self.key = key
        self.data = json.loads((root / f'data/ships/{key}.json').read_text(encoding='utf-8-sig'))
        self.L = self.data['flight']['length']
        self.mars = key.startswith('mars')
        self.rng = random.Random(zlib.crc32(key.encode()))
        self.coll = bpy.data.collections[f'{key} | editable parts']
        for obj in [o for o in self.coll.objects if o.name.startswith('DETAIL |')]:
            bpy.data.objects.remove(obj, do_unlink=True)
        self.m = {m.name: m for m in bpy.data.materials}
        self.mat_glow = self.m.get('Thermal glow') or self.make_glow()
        self.bm = {}
        self.sections = {s['id']: s for s in self.data['hullSections']}
        keep = [m['pivot'] for m in self.data['railgun'].get('mounts', [])]
        self.turrets = [(p, self.L * (0.075 if self.L > 600 else 0.07)) for p in keep]
        pd = self.data.get('pointDefense', {}).get('mounts', [])
        self.clear = [(p, self.L * 0.022) for p in pd]
        self.clear.append((self.data['missiles']['launchPoint'], self.L * 0.03))

    def make_glow(self):
        # Copy an existing emissive material (node setup differs between Blender versions).
        m = next(self.m[n] for n in ('Service lighting', 'EngineGlow', 'ShieldGlow') if n in self.m).copy()
        m.name = 'Thermal glow'
        bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        bs.inputs['Base Color'].default_value = (1, .32, .06, 1)
        bs.inputs['Emission Color'].default_value = (1, .32, .06, 1)
        bs.inputs['Emission Strength'].default_value = 2.2
        m.diffuse_color = (1, .32, .06, 1)
        return m

    # ── geometry ───────────────────────────────────────────
    def mesh_for(self, mat):
        if mat not in self.bm:
            self.bm[mat] = bmesh.new()
        return self.bm[mat]

    def box(self, c, size, mat='Armor - graphite titanium', rot=None):
        bm = self.mesh_for(mat)
        geom = bmesh.ops.create_cube(bm, size=1.0)['verts']
        m = Matrix.Diagonal((size[0], size[2], size[1], 1))   # Godot x,y,z sizes -> Blender x, y(=z), z(=y)
        if rot is not None:
            m = rot @ m
        bmesh.ops.transform(bm, matrix=Matrix.Translation(g(c)) @ m, verts=geom)

    def prism(self, points, depth_axis, depth, mat):
        """Flat polygon (Godot coords) extruded along a Godot axis vector."""
        bm = self.mesh_for(mat)
        vs = [bm.verts.new(g(p)) for p in points]
        face = bm.faces.new(vs)
        ext = bmesh.ops.extrude_face_region(bm, geom=[face])
        moved = [v for v in ext['geom'] if isinstance(v, bmesh.types.BMVert)]
        bmesh.ops.translate(bm, vec=g(depth_axis) * depth, verts=moved)

    def blocked(self, p, r=0.0):
        for c, rad in self.turrets + self.clear:
            if math.hypot(p[0] - c[0], p[2] - c[2]) < rad + r and abs(p[1] - c[1]) < self.L * 0.12:
                return True
        return False

    def finish(self):
        for mat, bm in self.bm.items():
            me = bpy.data.meshes.new(f'DETAIL | {mat}')
            bm.normal_update()
            bm.to_mesh(me)
            bm.free()
            me.materials.append(self.m.get(mat) or self.mat_glow)
            for poly in me.polygons:
                poly.use_smooth = False
            obj = bpy.data.objects.new(f'DETAIL | {mat}', me)
            self.coll.objects.link(obj)

    # ── capital ships ──────────────────────────────────────
    def top_section(self):
        return self.sections.get('deck') or self.sections['hull']

    def deck_equipment(self):
        """Rows of missile-cell hatches, vents and service boxes along the top deck (clear of turrets)."""
        s = self.top_section()
        cx, cy, cz = s['center']; hx, hy, hz = s['halfSize']
        top = cy + hy
        unit = self.L * 0.012
        bridge = self.sections.get('bridge')
        z = cz - hz + unit * 2
        while z < cz + hz - unit * 2:
            kind = self.rng.random()
            length = unit * self.rng.uniform(1.5, 4.5)
            zc = z + length / 2
            if bridge and abs(zc - bridge['center'][2]) < bridge['halfSize'][2] + unit * 2:
                z += length + unit; continue
            for side in (-1, 1):
                x = side * hx * self.rng.uniform(0.45, 0.72)
                if self.blocked((x, top, zc), length * 0.5):
                    continue
                if kind < 0.4:      # missile cell block: raised plate + grid of hatches
                    w = hx * 0.36
                    self.box((x, top + unit * 0.15, zc), (w, unit * 0.3, length), 'Edge - brushed alloy')
                    n = max(2, int(length / (unit * 0.9)))
                    for i in range(n):
                        hz_ = zc - length / 2 + (i + 0.5) * length / n
                        for j in (-1, 1):
                            self.box((x + j * w * 0.24, top + unit * 0.33, hz_), (w * 0.38, unit * 0.06, length / n * 0.72), 'Recess - carbon')
                elif kind < 0.7:    # stepped service box
                    h = unit * self.rng.uniform(0.6, 1.6)
                    self.box((x, top + h / 2, zc), (hx * 0.28, h, length * 0.8), 'Armor - raised ceramic')
                    self.box((x, top + h + unit * 0.12, zc), (hx * 0.18, unit * 0.24, length * 0.5), 'Edge - brushed alloy')
                else:               # vent grille
                    self.box((x, top + unit * 0.12, zc), (hx * 0.3, unit * 0.24, length * 0.7), 'Recess - carbon')
                    for i in range(5):
                        self.box((x - hx * 0.12 + i * hx * 0.06, top + unit * 0.3, zc), (hx * 0.018, unit * 0.18, length * 0.62), 'Edge - brushed alloy')
            # centre-line conduit
            if not self.blocked((0, top, zc), unit):
                self.box((0, top + unit * 0.2, zc), (unit * 0.5, unit * 0.4, length), 'Thermal - copper manifold' if self.rng.random() < 0.25 else 'Edge - brushed alloy')
            z += length + unit * self.rng.uniform(0.4, 1.2)

    def side_armour(self):
        """Layered side armour slabs with gaps (stacked-block silhouette of the concept sheets)."""
        s = self.sections['hull']
        cx, cy, cz = s['center']; hx, hy, hz = s['halfSize']
        unit = self.L * 0.012
        rad = [self.sections.get('radiator-port')]
        z = cz - hz + unit
        while z < cz + hz - unit * 3:
            length = unit * self.rng.uniform(4, 9)
            zc = z + length / 2
            for side in (-1, 1):
                for band, (y0, y1) in enumerate(((-0.8, -0.15), (0.05, 0.75))):
                    if self.rng.random() < 0.18:
                        continue
                    yc = cy + hy * (y0 + y1) / 2
                    t = unit * self.rng.uniform(0.35, 0.9)
                    self.box((side * (hx + t / 2), yc, zc), (t, hy * (y1 - y0) * self.rng.uniform(0.8, 1.0), length * 0.94),
                             'Armor - raised ceramic' if (band + int(zc / unit)) % 3 == 0 else 'Armor - graphite titanium')
                    if self.rng.random() < 0.35:   # recessed hatch on the slab
                        self.box((side * (hx + t + unit * 0.03), yc, zc), (unit * 0.06, hy * 0.18, length * 0.3), 'Recess - carbon')
            z += length + unit * 0.35

    def earth_radiator_bank(self):
        """Vertical slatted radiator panels on the aft hull sides (concept 'RADIATOR PANELS')."""
        s = self.sections['hull']
        hx, hy = s['halfSize'][0], s['halfSize'][1]
        cz, hz = s['center'][2], s['halfSize'][2]
        unit = self.L * 0.012
        z0, z1 = cz + hz * 0.05, cz + hz * 0.55
        for side in (-1, 1):
            x = side * (hx + unit * 1.4)
            self.box((x, 0, (z0 + z1) / 2), (unit * 0.5, hy * 1.15, z1 - z0), 'Recess - carbon')
            n = int((z1 - z0) / (unit * 0.9))
            for i in range(n):
                z = z0 + (i + 0.5) * (z1 - z0) / n
                self.box((x + side * unit * 0.35, 0, z), (unit * 0.25, hy * 1.1, unit * 0.35), 'Thermal - graphite fins')
            for y in (-hy * 0.58, hy * 0.58):
                self.box((x + side * unit * 0.4, y, (z0 + z1) / 2), (unit * 0.3, unit * 0.35, z1 - z0), 'Thermal - copper manifold')

    def mars_radiator_glow(self):
        """Orange glowing grids on the vertical Mars radiator panels."""
        for name in ('radiator-port', 'radiator-starboard'):
            s = self.sections.get(name)
            if not s:
                continue
            (x, y, z), (hx, hy, hz) = s['center'], s['halfSize']
            side = 1 if x > 0 else -1
            face = x + side * (hx + self.L * 0.0015)
            cols = 6; rows = 9
            for i in range(cols):
                zc = z - hz + (i + 0.5) * 2 * hz / cols
                self.box((face, y, zc), (self.L * 0.002, hy * 1.8, hz * 2 / cols * 0.08), 'Edge - brushed alloy')
                for j in range(rows):
                    yc = y - hy + (j + 0.5) * 2 * hy / rows
                    self.box((face, yc, zc), (self.L * 0.0014, hy * 2 / rows * 0.55, hz * 2 / cols * 0.7), 'Thermal glow')

    def mars_spires(self):
        """Cluster of thin sensor spires with lit tips on the sensor head (Ares/Deimos silhouette)."""
        s = self.sections.get('sensor-head') or self.sections['bridge']
        (x, y, z), (hx, hy, hz) = s['center'], s['halfSize']
        top = y + hy
        spots = [(0, 0, 1.0), (-0.75, -0.55, 0.78), (0.75, -0.45, 0.82), (-0.6, 0.65, 0.62), (0.62, 0.7, 0.66), (0, -0.95, 0.5), (0, 0.95, 0.55), (-0.95, 0.1, 0.42), (0.95, 0.05, 0.45)]
        for dx, dz, k in spots:
            h = self.L * 0.13 * k
            w = self.L * 0.004 * (1.4 if k > 0.9 else 1)
            px, pz = x + dx * hx * 0.8, z + dz * hz * 0.8
            self.box((px, top + h * 0.18, pz), (w * 3, h * 0.36, w * 3), 'Armor - graphite titanium')
            self.box((px, top + h * 0.6, pz), (w * 1.6, h * 0.5, w * 1.6), 'Edge - brushed alloy')
            self.box((px, top + h * 0.95, pz), (w * 0.7, h * 0.3, w * 0.7), 'Edge - brushed alloy')
            self.box((px, top + h * 1.11, pz), (w * 1.1, w * 1.1, w * 1.1), 'Service lighting')
            for f in (0.35, 0.7):
                self.box((px, top + h * f, pz), (w * 2.6, w * 0.6, w * 0.6), 'Edge - brushed alloy')

    def mars_bow_emblem(self):
        """Rust accent band and white triangle emblem on both bow flanks."""
        s = self.sections['bow-1']
        (x, y, z), (hx, hy, hz) = s['center'], s['halfSize']
        t = self.L * 0.0015
        for side in (-1, 1):
            fx = side * (hx + t * 2)
            self.box((fx, y + hy * 0.1, z), (t * 2, hy * 1.2, hz * 1.5), 'Armor - raised ceramic')
            # emblem: downward-pointing triangle outline
            c = Vector((fx + side * t * 2, y + hy * 0.15, z + hz * 0.45))
            r = hy * 0.32
            pts = [(c.x, c.y + r * 0.6, c.z - r * 0.7), (c.x, c.y + r * 0.6, c.z + r * 0.7), (c.x, c.y - r * 0.75, c.z)]
            inner = [(c.x, c.y + r * 0.38, c.z - r * 0.42), (c.x, c.y + r * 0.38, c.z + r * 0.42), (c.x, c.y - r * 0.38, c.z)]
            for a, b, ia, ib in zip(pts, pts[1:] + pts[:1], inner, inner[1:] + inner[:1]):
                self.prism([a, b, ib, ia], (side, 0, 0), t, 'Markings - ivory')

    # ── interceptors ───────────────────────────────────────
    def interceptor_fins(self):
        """Canted ventral fins (X silhouette from the rear) and wingtip pods with engine glow."""
        wing = self.sections['wing-back']; hull = self.sections['hull']
        (wx, wy, wz), (whx, why, whz) = wing['center'], wing['halfSize']
        span = whx
        # Thin solid fins: build as boxes rotated by the cant angle.
        for side in (-1, 1):
            cant = math.radians(35)
            c = (side * span * 0.22, -hull['halfSize'][1] * 1.45, wz + whz * 0.45)
            rot = Matrix.Rotation(side * -cant, 4, 'Y')   # Blender Y = Godot -Z (roll about the long axis)
            self.box(c, (self.L * 0.006, hull['halfSize'][1] * 1.9, whz * 1.5), 'Armor - graphite titanium', rot)
            self.box((c[0], c[1], c[2] + whz * 0.55), (self.L * 0.008, hull['halfSize'][1] * 1.7, whz * 0.25), 'Armor - raised ceramic', rot)
        # Wingtip engine pods
        for side in (-1, 1):
            px = side * (span * (0.92 if not self.mars else 0.88))
            pz = wz + whz * 0.35
            r = self.L * 0.017
            self.box((px, wy, pz), (r * 2, r * 2, whz * 1.6), 'Armor - graphite titanium')
            self.box((px, wy, pz - whz * 0.9), (r * 1.3, r * 1.3, whz * 0.3), 'Edge - brushed alloy')
            self.box((px, wy, pz + whz * 0.82), (r * 1.4, r * 1.4, self.L * 0.003), 'EngineGlow')
        if self.mars:   # canards near the nose
            nose = self.sections['nose']
            (nx, ny, nz), (nhx, nhy, nhz) = nose['center'], nose['halfSize']
            for side in (-1, 1):
                self.prism([(side * nhx, ny, nz + nhz * 0.1), (side * nhx * 3.2, ny - nhy * 0.2, nz + nhz * 0.45),
                            (side * nhx * 3.0, ny - nhy * 0.2, nz + nhz * 0.6), (side * nhx, ny, nz + nhz * 0.55)],
                           (0, 1, 0), self.L * 0.004, 'Armor - raised ceramic')

    def run(self):
        if self.L < 100:
            self.interceptor_fins()
        else:
            self.deck_equipment()
            if self.mars:
                # Mars sides are the open truss frame from build-mars-models.py; spires belong to its compact mast.
                self.mars_radiator_glow()
                self.mars_bow_emblem()
            else:
                self.side_armour()
                self.earth_radiator_bank()
        self.finish()


for key in SHIPS:
    if args.ship and args.ship != key:
        continue
    path = root / 'art' / 'blender' / f'{key}.blend'
    bpy.ops.wm.open_mainfile(filepath=str(path))
    d = Detailer(key)
    d.run()
    tris = 0
    for o in d.coll.objects:
        if o.name.startswith('DETAIL |'):
            o.data.calc_loop_triangles(); tris += len(o.data.loop_triangles)
    bpy.ops.wm.save_mainfile(filepath=str(path))
    print(f'detail {key}: +{tris} triangles')
