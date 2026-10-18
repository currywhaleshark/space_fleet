"""Reference-matched hulls from data/model_layouts/<id>.json blockout.pieces (see reference_layout.py).
Blender --background --factory-startup --python-exit-code 1 --python this.py -- --root REPO [--render] --ship ID[,ID...]
The visible primary forms are authored as pieces; turrets, PD, engines, radiators, launchers, sensors, shields,
bays and RCS are placed from the ship data, seated on the hull by ray casts. After manual Blender edits use
export-ship-models.py instead of rebuilding.
"""
import importlib.util, json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(sys.argv[sys.argv.index('--root') + 1]); sys.path.insert(0, str(ROOT / 'tools'))
from fleet_model_primitives import g
from fleet_model_export import rig_turrets, rig_point_defense
import reference_layout


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / file)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module

t5 = load('t5_builder', 'build-t5-models.py')
small = t5.small
OUT = ROOT / 'docs/validation/reference_models_20261018'; OUT.mkdir(parents=True, exist_ok=True)
small.OUT = OUT

PALETTES = {
    # Earth reference: weathered ivory armour, navy bands and radiator panels.
    'earth': dict(armor=(.72, .73, .7), plate=(.07, .1, .19), edge=(.22, .24, .27), dark=(.03, .035, .045), radiator=(.08, .13, .26)),
    # Mars references: light gray (small craft, Marineris) or charcoal (Hellas, Nergal) with rust-red bands.
    'mars-light': dict(armor=(.6, .58, .55), plate=(.34, .085, .045), edge=(.2, .2, .21), dark=(.03, .03, .035), radiator=(.07, .07, .08)),
    'mars-dark': dict(armor=(.15, .155, .16), plate=(.34, .085, .045), edge=(.11, .11, .12), dark=(.025, .025, .03), radiator=(.07, .07, .08)),
}


def paint(ship, palette):
    colors = dict(PALETTES[palette['base']] if isinstance(palette, dict) else PALETTES[palette])
    if isinstance(palette, dict): colors.update({k: tuple(v) for k, v in palette.items() if k != 'base'})
    for key, rgb in colors.items():
        mat = ship.m[key]; mat.diffuse_color = (*rgb, 1)
        node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        node.inputs['Base Color'].default_value = (*rgb, 1)
        # Reference hulls read as painted armour, not bare metal (dark space reflections turn metal black).
        if key in ('armor', 'plate'): node.inputs['Metallic'].default_value = .2


def interp(rings, z):
    rings = sorted(rings)
    if z <= rings[0][0]: return tuple(rings[0][1:])
    for a, b in zip(rings, rings[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0]); return tuple(a[i] + (b[i] - a[i]) * t for i in (1, 2, 3))
    return tuple(rings[-1][1:])


class Body:
    """Shared piece drawing and hull ray casts for capital and small craft."""
    def setup(self):
        self.layout = json.loads((ROOT / f'data/model_layouts/{self.key}.json').read_text(encoding='utf-8'))
        self.bo = self.layout['blockout']; self.reference = self.bo.get('reference', '')
        paint(self, self.bo.get('palette', 'earth' if self.earth else 'mars-light'))
        self.body = []; self.core = []; self._bvh = {}

    def prism(self, name, points, thickness, mat='armor'):
        n = len(points); vs = [(x, y + t * thickness / 2, z) for t in (-1, 1) for x, y, z in points]
        fs = [tuple(reversed(range(n))), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
        return self.mesh(name, vs, fs, mat)

    def piece(self, p):
        if p.get('hidden'): return []  # hit section only (e.g. a sensor head drawn from module data)
        k = p['kind']; name = p.get('name', p.get('section', k)); mat = p.get('mat', 'armor')
        if k == 'loft':
            obj = self.loft(name, [tuple(r) for r in p['rings']], mat); obj.location.x += p.get('x', 0); objs = [obj]
        elif k == 'box':
            objs = [self.box(name, p['center'], p['size'], mat, p.get('bevel'))]
        elif k == 'prism':
            objs = [self.prism(name, [(x, p.get('y', 0), z) for x, z in p['points']], p.get('thickness', .5), mat)]
        elif k == 'fin':
            (r0, r1), (t0, t1) = p['root'], p['tip']; w = p.get('thickness', .24) / 2
            quad = [r0, t0, t1, r1]; vs = [(x + s * w, y, z) for s in (-1, 1) for x, y, z in quad]
            objs = [self.mesh(name, vs, [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], mat)]
        elif k == 'truss':
            c = Vector(p['center']); h = Vector(p['size']) / 2; r = p.get('rod', .6); objs = []
            ax = p.get('axis', 'z')  # chords run along the long axis; posts and diagonals close the open frame
            corners = [(sx, sy) for sx in (-1, 1) for sy in (-1, 1)]
            if ax == 'z':
                for sx, sy in corners: objs.append(self.rod(name + ' chord', (c.x + sx * h.x, c.y + sy * h.y, c.z - h.z), (c.x + sx * h.x, c.y + sy * h.y, c.z + h.z), r, mat))
                n = max(1, round(h.z * 2 / max(h.x * 2, h.y * 2, 1)))
                for i in range(n + 1):
                    z = c.z - h.z + 2 * h.z * i / n
                    for sx in (-1, 1): objs.append(self.rod(name + ' post', (c.x + sx * h.x, c.y - h.y, z), (c.x + sx * h.x, c.y + h.y, z), r * .8, mat))
                    for sy in (-1, 1): objs.append(self.rod(name + ' cross member', (c.x - h.x, c.y + sy * h.y, z), (c.x + h.x, c.y + sy * h.y, z), r * .7, mat))
                    if i < n:
                        z2 = c.z - h.z + 2 * h.z * (i + 1) / n
                        for sx in (-1, 1): objs.append(self.rod(name + ' diagonal', (c.x + sx * h.x, c.y - h.y, z), (c.x + sx * h.x, c.y + h.y, z2), r * .7, mat))
            else:
                for sz in (-1, 1):
                    for sy in (-1, 1): objs.append(self.rod(name + ' chord', (c.x - h.x, c.y + sy * h.y, c.z + sz * h.z), (c.x + h.x, c.y + sy * h.y, c.z + sz * h.z), r, mat))
                    objs.append(self.rod(name + ' post', (c.x, c.y - h.y, c.z + sz * h.z), (c.x, c.y + h.y, c.z + sz * h.z), r * .8, mat))
        else:
            raise ValueError(k)
        if not p.get('decor'):
            self.body += objs
            if p.get('core', True): self.core += objs
        return objs

    def draw_pieces(self):
        drawn = {}
        for p in reference_layout.pieces(self.layout): drawn.setdefault(p.get('name'), []).append((p, self.piece(p)))
        grow = self.bo.get('bandGrow', .2 if self.capital else .03)
        for band in self.bo.get('bands', []):
            for p, _ in drawn.get(band['piece'], []):
                z1, z2 = band['z']; rings = sorted(tuple(r) for r in p['rings'])
                zs = [z1] + [r[0] for r in rings if z1 < r[0] < z2] + [z2]
                obj = self.loft(band.get('name', 'Paint band'), [(z, *(lambda w, h, y: (w + grow, h + grow, y))(*interp(rings, z))) for z in zs], band.get('mat', 'plate'))
                obj.location.x += p.get('x', 0)

    def ray(self, origin, direction, core=True):
        if core not in self._bvh:
            graph = bpy.context.evaluated_depsgraph_get(); vs = []; fs = []
            for obj in (self.core if core else self.body):
                mesh = obj.evaluated_get(graph).to_mesh(); offset = len(vs)
                vs += [obj.matrix_world @ v.co for v in mesh.vertices]; fs += [[offset + i for i in poly.vertices] for poly in mesh.polygons]
                obj.evaluated_get(graph).to_mesh_clear()
            self._bvh[core] = BVHTree.FromPolygons(vs, fs)
        hit, _, _, _ = self._bvh[core].ray_cast(g(origin), g(direction).normalized(), 1e5)
        return None if hit is None else Vector((hit.x, hit.z, -hit.y))

    def below(self, p, sign, what):
        """First hull surface under (or above, for ventral fittings) a fitting point, decks included."""
        hit = self.ray(Vector(p), Vector((0, -sign, 0)), core=False)
        if hit is None: raise ValueError(f'{self.key}: nothing under {what}')
        return hit

    def seat(self, origin, direction, what):
        hit = self.ray(Vector(origin) - Vector(direction) * self.data['flight']['length'] * 2, direction)
        if hit is None: raise ValueError(f'{self.key}: no hull surface for {what}')
        return hit

    def auto_rcs(self, stations, size, plume, forward=True):
        """Seven jets per side at the given stations: lateral, dorsal, ventral (+ a forward jet at the first)."""
        L = self.data['flight']['length']
        for index, z in enumerate(stations):
            top = self.seat((0, 0, z), (0, -1, 0), 'rcs top').y; bottom = self.seat((0, 0, z), (0, 1, 0), 'rcs bottom').y; yc = (top + bottom) / 2
            for side in (-1, 1):
                lateral = self.seat((0, yc, z), (-side, 0, 0), 'rcs side'); w = abs(lateral.x)
                self.rcs((side * w, yc, z), (side, 0, 0), size, plume)
                for v in (-1, 1):
                    vx = side * max(w * .6, w - size * 2.5)
                    p = self.ray(Vector((vx, v * L, z)), Vector((0, -v, 0)))
                    if p is None: raise ValueError(f'{self.key}: no hull for vertical RCS at z {z}')
                    self.rcs((vx, p.y, z), (0, v, 0), size, plume)
                if forward and index == 0: self.rcs((side * (w + size * .5), yc, z), (0, 0, -1), size, plume)


class Capital(Body, t5.CapitalT5):
    capital = True
    def __init__(self, key, render):
        t5.CapitalT5.__init__(self, key, render); self.out = OUT; self.setup()

    def radiator(self, module):
        x, y, z = module['center']; w, h, d = module['halfSize']; side = 1 if x > 0 else -1
        self.box('Radiator frame ' + module['id'], (x, y, z), (w * 2, h * 2, d * 2), 'edge', min(w, h) * .2)
        vertical = h > w
        for i in range(4):
            dz = z - d * .72 + i * d * .48
            if vertical:
                for s in (-1, 1): self.box('Radiator panel', (x + s * w * .75, y, dz), (w * .5, h * 1.84, d * .44), 'radiator', 0)
                for k in range(3): self.box('Radiator coolant channel', (x + side * w * 1.02, y - h * .6 + k * h * .6, dz), (w * .2, h * .08, d * .44), 'copper', 0)
            else:
                for s in (-1, 1): self.box('Radiator panel', (x, y + s * h * .75, dz), (w * 1.84, h * .5, d * .44), 'radiator', 0)
        inner = x - side * w
        root = self.seat((inner, y, z), (-side, 0, 0), 'radiator root')
        for dz in (-.7, .7): self.rod('Radiator root', (root.x - side * .5, y, z + dz * d), (inner + side * w * .3, y, z + dz * d), max(.4, min(w, h, d) * .1) if vertical else .9 * self.scale, 'edge')

    def engine(self, module):
        x, y, z = module['center']; w, h, d = module['halfSize']; i = module['visualEngineIndex']
        radius = min(w, h) * .85 * self.bo.get('engineScale', 1)
        aft = self.seat((x, y, z), (0, 0, -1), 'engine aft').z
        mouth = max(z + d, aft) + radius * .65; start = min(z - d, aft - radius); end = mouth - radius * .7
        self.box('Engine cradle ' + str(i), (x, y, (start + end) / 2), (radius * 2.2, radius * 2.2, end - start), 'armor', radius * .12)
        self.tube('Engine nozzle ' + str(i), (x, y, mouth - radius * .3), radius, radius * 1.05, 'edge', wall=.18)
        self.cylinder('Engine throat ' + str(i), (x, y, mouth - radius * .35), radius * .78, radius * .04, 'engine', sides=20)
        for n in range(8):
            a = n * math.tau / 8
            self.rod('Engine cooling rib', (x + radius * math.cos(a), y + radius * math.sin(a), mouth - radius), (x + radius * math.cos(a), y + radius * math.sin(a), mouth), radius * .03, 'copper')
        position = [x, y, mouth + radius * .225]
        self.fx['engines'].append(dict(position=position, radius=radius * .82, plume=self.bo.get('plume', 28 * self.scale), index=i))
        self.socket('fx_engine_' + str(i), position)

    def build(self):
        s = self.scale; self.draw_pieces()
        sensor = next(m for m in self.data['modules'] if m['id'] == 'sensor'); sx, sy, sz = sensor['center']; sw, sh, sd = sensor['halfSize']
        if self.bo.get('sensorHead', True):
            self.box('Sensor protected head', (sx, sy, sz), (sw * 2 + .4 * s, sh * 2 + .4 * s, sd * 2 + .4 * s), 'armor', .5 * s)
            self.box('Sensor roof', (sx, sy + sh + .5 * s, sz), (sw * 2 + 3 * s, .8 * s, sd * 2 + 3 * s), 'plate', .2 * s)
            for side in (-1, 1): self.box('Sensor side glazing', (sx + side * (sw + .25 * s), sy + sh * .45, sz), (.3 * s, sh * .35, sd * 1.4), 'glass', .05 * s)
            self.box('Sensor front glazing', (sx, sy + sh * .45, sz - sd - .25 * s), (sw * 1.6, sh * .35, .3 * s), 'glass', .05 * s)
            for xx, height in ((0, 14), (-4, 8), (4, 10)):
                self.rod('Attached antenna', (xx * s, sy + sh + .9 * s, sz), (xx * s, sy + sh + .9 * s + height * s, sz), .25 * s, 'edge')
        for mount in self.data['railgun']['mounts']: self.gun(mount)
        pd_size = self.bo.get('pdSize', (2.25 if self.earth else 1.8) * s)
        for i, p in enumerate(self.data['pointDefense']['mounts']):
            sign = 1 if p[1] >= 0 else -1; hit = self.below(p, sign, f'PD {i}')
            height = max(.5 * s, abs(p[1] - hit.y) + .4 * s)
            self.box('PD foundation ' + str(i), (p[0], hit.y + sign * (height / 2 - .2 * s), p[2]), (pd_size * 2.2, height, pd_size * 2.2), 'armor', .25 * s)
            self.point_defense(p, pd_size, i)
        lx, ly, lz = self.data['missiles']['launchPoint']; down = self.below((lx, ly - 1.2 * s, lz), 1, 'missile launcher')
        self.box('Missile transfer trunk', (lx, (down.y - .5 * s + ly - 1 * s) / 2, lz), (5 * s, ly - 1 * s - down.y + .5 * s, 8 * s), 'armor', .3 * s)
        self.tube('Active missile launch mouth', (lx, ly, lz), 1.6 * s, 2 * s, 'edge', axis=(0, 1, 0), wall=.2)
        for module in self.data['modules']:
            if module['kind'] == 'ShieldEmitter':
                x, _, z = module['center']; top = self.seat((x, 0, z), (0, -1, 0), 'shield projector').y
                self.box('Shield projector', (x, top + .15 * s, z), (3 * s, .5 * s, 6 * s), 'edge', .1 * s)
                self.box('Shield lens', (x, top + .45 * s, z), (2 * s, .15 * s, 4 * s), 'shield', .05 * s)
        for bay in self.data['bays']:
            bx, by, bz = bay['center']; side = 1 if bx > 0 else -1; hit = self.seat((bx, by, bz), (-side, 0, 0), 'bay cover')
            self.box('Utility access ' + bay['id'], (hit.x + side * .1 * s, by, bz), (.3 * s, 4 * s, 6 * s), 'edge', .1 * s)
        for vls in self.bo.get('vls', []):
            (cx, cy, cz), (rows, cols), (px, pz), (cw, cl) = vls['center'], vls['grid'], vls['pitch'], vls['cell']
            for r in range(rows):
                for c in range(cols):
                    x = cx + (r - (rows - 1) / 2) * px; z = cz + (c - (cols - 1) / 2) * pz
                    self.box('VLS cell coaming', (x, cy, z), (cw, .5 * s, cl), 'edge', .15 * s)
                    self.box('VLS armored lid', (x, cy + .3 * s, z), (cw * .84, .3 * s, cl * .84), 'plate', .1 * s)
        for hatch in self.bo.get('hatches', []):
            (hx, hy, hz), (rows, cols), (py, pz), size = hatch['start'], hatch['grid'], hatch['pitch'], hatch['size']
            for side in (-1, 1):
                for r in range(rows):
                    for c in range(cols):
                        self.box('Missile magazine hatch', (side * hx, hy + r * py, hz + c * pz), size, 'dark', .2 * s)
        rcs = self.bo['rcs']; z = rcs['stations'][0]
        w = abs(self.seat((0, 0, z), (-1, 0, 0), 'IFF panel').x)
        for side in (-1, 1):  # team colour (IFF) panels on the forward deck
            top = self.seat((side * w * .45, 0, z), (0, -1, 0), 'IFF panel').y
            self.box('Team identification panel', (side * w * .45, top + .12 * s, z), (4 * s, .3 * s, 6 * s), 'iff', .04 * s)
        rcs = self.bo['rcs']; self.auto_rcs(rcs['stations'], rcs.get('size', 1.5 * s), rcs.get('plume', 12 * s))
        self.modules_and_markers(); rig_turrets(self.coll, self.data); rig_point_defense(self.coll, self.data)


class Small(Body, t5.SmallT5):
    capital = False
    def __init__(self, key):
        t5.SmallT5.__init__(self, key); self.setup()

    def build(self):
        f = self.bo; recon = 'recon' in self.key; e = self.earth
        self.draw_pieces()
        cockpit = f['cockpit']; cz, cw, cy = cockpit['z'], cockpit['w'], cockpit['y']
        self.loft('Cockpit frame', [(cz - 2, .35, .13, cy - .5), (cz, cw, .55, cy), (cz + 2.3, cw, .45, cy), (cz + 2.8, cw * .8, .1, cy - .2)], 'edge')
        self.loft('Cockpit glazing', [(cz - 1.7, .22, .08, cy - .3), (cz, cw * .84, .39, cy + .2), (cz + 2, cw * .84, .3, cy + .2)], 'glass')
        self.rod('Canopy spine', (0, cy - .15, cz - 1.6), (0, cy + .55, cz + 2), .05, 'edge')
        sensor = next(m for m in self.data['modules'] if m['id'] == 'sensor'); top = self.seat(sensor['center'], (0, -1, 0), 'sensor window').y
        self.box('Sensor dorsal window', (0, top + .02, sensor['center'][2]), (.75 if recon else .42, .08, .8), 'glass', .02)
        mx, my, mz = self.data['railgun']['muzzle']; gun = next(m for m in self.data['modules'] if m['id'] == 'gun'); gx, gy, gz = gun['center']
        self.box('Fixed gun breech', (gx, gy, gz), (.8, .65, 2 * gun['halfSize'][2]), 'dark', .06)
        for side in (-1, 1): self.box('Fixed accelerator rail', (side * .27, my, (mz + gz) / 2), (.21, .3, gz - mz), 'edge', .025)
        self.tube('Fixed muzzle', (mx, my, mz + .12), .24, .24, 'plate', wall=.22)
        for side, suffix in ((-1, 'port'), (1, 'starboard')):
            engine = next(m for m in self.data['modules'] if m['kind'] == 'Thruster' and (m['center'][0] > 0) == (side > 0))
            ex, ey, ez = engine['center']; index = engine['visualEngineIndex']
            radius = f.get('nozzleRadius', 1.05); aft = self.seat(engine['center'], (0, 0, -1), 'nozzle').z; nozzle = aft + .35
            self.tube('Engine rim ' + str(index), (ex, ey, nozzle), radius, .9, 'edge', wall=.2)
            self.cylinder('Engine throat ' + str(index), (ex, ey, nozzle - .3), radius * .78, .06, 'engine')
            position = [ex, ey, nozzle + .45]
            self.fx['engines'].append(dict(position=position, radius=radius * .78, plume=8 if e else 10, index=index)); self.socket('fx_engine_' + str(index), position)
            cooling = next(m for m in self.data['modules'] if m['id'] == 'cooling-' + suffix); cx, _, czz = cooling['center']
            ctop = self.seat((cx, 0, czz), (0, -1, 0), 'cooling').y
            self.box('Cooling cassette ' + suffix, (cx, ctop - .05, czz), (1.6, .35, 1.7), 'radiator', .04)
            for dz in (-.5, 0, .5): self.box('Cooling manifold', (cx, ctop + .14, czz + dz), (1.45, .08, .1), 'edge', .01)
            lx, ly, lz = self.data['missiles']['launchPoint']; lx = side * abs(lx)
            self.box('Missile rack support ' + suffix, (lx - side * .24, ly, lz + 1.2), (.65, .8, 2.5), 'dark', .05)
            self.cylinder('Missile cassette ' + suffix, (lx, ly, lz + 1.25), .38, 2.5, 'plate')
            self.tube('Missile mouth ' + suffix, (lx, ly, lz), .38, .2, 'edge', wall=.2)
        probes = self.data.get('reconProbes')
        for i, (x, y, z) in enumerate(probes['launchPoints'] if probes else []):
            if recon:
                self.cylinder('Probe cartridge ' + str(i), (x, y, z + .8), .27, 1.6, 'plate')
                self.tube('Probe mouth ' + str(i), (x, y, z), .28, .16, 'edge', wall=.2)
                self.cylinder('Probe lens ' + str(i), (x, y, z - .03), .2, .06, 'glass')
            else:
                side = 1 if x > 0 else -1
                self.box('Probe mount ' + str(i), (x - side * .3, y, z), (.65, .5, 1), 'edge', .04)
                self.cylinder('Probe cartridge ' + str(i), (x - side * .25, y, z), .23, .65, 'plate', axis=(side, 0, 0))
                self.cylinder('Probe lens ' + str(i), (x + side * .08, y, z), .20, .05, 'glass', axis=(side, 0, 0))
            self.socket('socket_probe_' + str(i), [x, y, z])
        keel = self.sections['am-keel']; x, y, z = keel['center']; w, h, d = keel['halfSize']
        if not f.get('keelPiece'): self.loft('Containment keel', [(z - d, w * .8, h, y), (z - d + .3, w, h, y), (z + d, w, h, y)], 'armor')
        self.box('AM jettison hatch', (0, y - h - .03, z), (w * 1.7, .1, d * 1.7), 'plate', .03)
        rounds = self.data['antimatter']['rounds']
        for i in range(rounds):
            self.box('Containment segment', (0, y - h - .09, z - d * .65 + (i + .5) * 1.3 * d / rounds), (w * 1.45, .05, .1), 'edge', .01)
        self.socket('socket_am_jettison', [0, y - h - .1, z]); self.socket('fx_am_containment', [0, y - h - .1, z])
        self.tube('AM launch mouth', self.data['antimatter']['flight']['launchPoint'], .32, .4, 'edge', wall=.2)
        self.socket('socket_antimatter_launch', self.data['antimatter']['flight']['launchPoint'])
        for i, p in enumerate(self.data['pointDefense']['mounts']):
            sign = 1 if p[1] > 0 else -1; hit = self.below(p, sign, f'PD {i}')
            height = max(.3, abs(p[1] - hit.y) + .1)
            self.box('PD foundation ' + str(i), (p[0], hit.y + sign * (height / 2 - .08), p[2]), (1, height, 1.3), 'armor', .05)
            self.point_defense(p, .5 if e else .43, i)
        shield = next(m for m in self.data['modules'] if m['id'] == 'shield'); sx, _, sz = shield['center']
        top = self.seat((sx, 0, sz), (0, -1, 0), 'shield projector').y
        self.box('Shield projector', (sx, top + .1, sz), (.65, .25, .9), 'edge', .04)
        self.box('Shield lens', (sx, top + .24, sz), (.4, .04, .55), 'shield', .005)
        for engine in (m for m in self.data['modules'] if m['kind'] == 'Thruster'):  # team colour (IFF) bands
            ex, _, ez = engine['center']; top = self.seat((ex, 0, ez - 1.6), (0, -1, 0), 'IFF band').y
            self.box('IFF band', (ex, top + .02, ez - 1.6), (1.2, .04, .4), 'iff', .005)
        rcs = f['rcs']; self.auto_rcs(rcs['stations'], rcs.get('size', .28), rcs.get('plume', 4))
        self.modules(); rig_point_defense(self.coll, self.data)
        for bay in self.data['bays']: self.socket('bay_' + bay['id'].replace('-', '_'), bay['center'], half_size=bay['halfSize'])


def main():
    args = sys.argv[sys.argv.index('--') + 1:]; render = '--render' in args
    keys = args[args.index('--ship') + 1].split(',')
    path = ROOT / 'assets/ships/manifest.json'; manifest = json.loads(path.read_text(encoding='utf-8'))
    for key in keys:
        bpy.ops.wm.read_factory_settings(use_empty=True); initial = bpy.context.scene
        smallcraft = any(a in key for a in ('interceptor', 'recon', 'assault'))
        ship = Small(key) if smallcraft else Capital(key, render)
        bpy.data.scenes.remove(initial); ship.build(); entry = ship.save()
        if 'surface' in ship.bo: entry['surface'] = ship.bo['surface']
        manifest['ships'] = [v for v in manifest['ships'] if v['id'] != key] + [entry]
        path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8'); print('BUILT', key, entry['triangles'], 'triangles', flush=True)


if __name__ == '__main__': main()
