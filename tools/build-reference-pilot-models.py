"""Reference-matched rebuild of pilot hulls (T5 Amazon cruiser, T5 Haedongcheong interceptor).
Blender --background --factory-startup --python-exit-code 1 --python this.py -- --root REPO [--render] [--ship ID]
Geometry follows data/model_layouts/<id>.json (hit sections are the bounding contract); silhouettes and paint
follow art/reference/20261017_t5_fleet. After manual Blender edits use export-ship-models.py instead.
"""
import importlib.util, json, sys
from pathlib import Path
import bpy

ROOT = Path(sys.argv[sys.argv.index('--root') + 1]); sys.path.insert(0, str(ROOT / 'tools'))
from fleet_model_export import rig_turrets, rig_point_defense

def load(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / file)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module

t5 = load('t5_builder', 'build-t5-models.py')
small = t5.small
OUT = ROOT / 'docs/validation/reference_pilot_20261017'; OUT.mkdir(parents=True, exist_ok=True)
small.OUT = OUT
KEYS = ('earth_gun_cruiser_t5', 'interceptor_t5')


def paint(ship, colors):
    for key, rgb in colors.items():
        mat = ship.m[key]; mat.diffuse_color = (*rgb, 1)
        node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        node.inputs['Base Color'].default_value = (*rgb, 1)
        # Reference hulls read as painted armour, not bare metal: keep diffuse light on the paint.
        if key in ('armor', 'plate'): node.inputs['Metallic'].default_value = .2


def profile_at(rings, z):
    """Linear (half-width, half-height, y-centre) of a loft profile at z."""
    for a, b in zip(rings, rings[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            return tuple(a[i] + (b[i] - a[i]) * t for i in (1, 2, 3))
    raise ValueError(z)


class Amazon(t5.CapitalT5):
    """Broad wedge ram, stepped gun citadel, long radiator wings, bulky aft engineering shoulders."""
    def __init__(self, key, render):
        super().__init__(key, render); self.out = OUT
        self.layout = json.loads((ROOT / f'data/model_layouts/{key}.json').read_text(encoding='utf-8'))
        self.reference = self.layout['blockout']['reference']
        # Reference: weathered ivory-gray hull, navy armour bands, navy radiator panels.
        paint(self, {'armor': (.72, .73, .7), 'plate': (.07, .1, .19), 'edge': (.22, .24, .27),
                     'dark': (.03, .035, .045), 'radiator': (.08, .13, .26)})

    def radiator(self, module):
        # Reference wings: a gray frame around navy panels, carried by struts from the shoulder.
        x, y, z = module['center']; w, h, d = module['halfSize']; side = 1 if x > 0 else -1
        self.box('Radiator frame ' + module['id'], (x, y, z), (w * 2, h * 2, d * 2), 'edge', min(w, h) * .2)
        for i in range(4):
            self.box('Radiator panel', (x, y + h * .75, z - d * .72 + i * d * .48), (w * 1.84, h * .5, d * .44), 'radiator', 0)
            self.box('Radiator panel underside', (x, y - h * .75, z - d * .72 + i * d * .48), (w * 1.84, h * .5, d * .44), 'radiator', 0)
        for dz in (-.7, .7): self.rod('Radiator root', (side * 26, y, z + dz * d), (x + side * w * .85, y, z + dz * d), .9, 'edge')

    def band(self, name, rings, z1, z2, grow=.2):
        zs = [z1] + [r[0] for r in rings if z1 < r[0] < z2] + [z2]
        out = []
        for z in zs:
            w, h, y = profile_at(rings, z); out.append((z, w + grow, h + grow, y))
        return self.loft(name, out, 'plate')

    def build(self):
        P = [tuple(r) for r in self.layout['blockout']['profile']]
        top = lambda z: profile_at(P, z)[2] + profile_at(P, z)[1]
        self.loft('Wedge ram hull', P, 'armor')
        for z1, z2 in ((-150, -140), (-104, -92), (-58, -50), (-8, 4)):
            self.band(f'Navy armour band {z1}', P, z1, z2)
        # Blunt ram face: a dark inset in a raised frame, as in the reference prow.
        z0, (w0, h0, y0) = P[0][0], P[0][1:]
        self.box('Ram face frame', (0, y0, z0 - .3), (w0 * 1.5, h0 * 1.55, .8), 'edge', .3)
        self.box('Ram face inset', (0, y0, z0 - .65), (w0 * 1.15, h0 * 1.1, .4), 'dark', .1)
        # Raised dorsal ridge runs from the ram to the forward battery.
        ridge = [(-165, 4, .9, top(-165) + .5)] + [(z, wd, .9, top(z) + .5) for z, wd in ((-158, 5), (-112, 8), (-80, 10))]
        self.loft('Bow dorsal ridge', ridge + [(-72, 10, .9, top(-72) + .5)], 'edge')
        for side in (-1, 1):
            self.box('Team identification panel', (side * 9, top(-135) + .25, -135), (4, .3, 6), 'iff', .04)
            # Side armour seams carry the long wedge read at combat range.
            for z in (-128, -82, -30, 20):
                w, h, y = profile_at(P, z)
                self.box('Hull side belt', (side * (w + .15), y, z), (.6, h * 1.25, 3), 'edge', .1)
        # Stepped gun citadel and command tower.
        self.section_loft('citadel-lower', (.85, 1, 1, .95), 'armor')
        self.section_loft('citadel-upper', (.8, 1, 1, .9), 'armor')
        for side in (-1, 1):
            self.box('Citadel navy panel', (side * 17.15, 23, 8), (.5, 8, 22), 'plate', .1)
            self.box('Citadel upper navy panel', (side * 12.1, 30.5, 44), (.4, 5, 16), 'plate', .1)
        sensor = next(m for m in self.data['modules'] if m['id'] == 'sensor'); sx, sy, sz = sensor['center']; sw, sh, sd = sensor['halfSize']
        self.box('Sensor protected head', (sx, sy, sz), (sw * 2 + .4, sh * 2 + .4, sd * 2 + .4), 'armor', .5)
        self.box('Bridge roof', (sx, sy + sh + .5, sz), (sw * 2 + 3, .8, sd * 2 + 3), 'plate', .2)
        for side in (-1, 1):
            self.box('Bridge side glazing', (sx + side * (sw + .25), sy + sh * .45, sz), (.3, sh * .35, sd * 1.4), 'glass', .05)
        self.box('Bridge front glazing', (sx, sy + sh * .45, sz - sd - .25), (sw * 1.6, sh * .35, .3), 'glass', .05)
        for xx, height in ((0, 14), (-4, 8), (4, 10)):
            self.rod('Attached antenna', (xx, sy + sh + .9, sz), (xx, sy + sh + .9 + height, sz), .25, 'edge')
        for name in ('turret-base-gun-1', 'turret-base-gun-2'): self.section_box(name, 'armor', .5)
        # Aft engineering: a core housing flanked by taller rectangular shoulders.
        self.loft('Aft engineering core', [(48, 27, 17.5, 0), (60, 25, 18, 0), (138, 22, 16, 0)], 'armor')
        for side in (-1, 1):
            self.box('Engineering shoulder', (side * 25, 0, 97), (14, 42, 78), 'armor', 1.5)
            self.box('Engineering shoulder navy band', (side * 32.15, 0, 78), (.5, 38, 14), 'plate', .1)
            self.box('Engineering shoulder top band', (side * 25, 21.15, 78), (13, .5, 14), 'plate', .1)
            self.box('Engineering service hatch', (side * 32.15, 8, 110), (.4, 8, 10), 'edge', .1)
        for mount in self.data['railgun']['mounts']: self.gun(mount)
        for i, p in enumerate(self.data['pointDefense']['mounts']):
            sign = 1 if p[1] >= 0 else -1
            skin = 21 if p[2] >= 50 and abs(p[0]) >= 18 else 18
            height = max(.5, abs(p[1]) - skin + .4)
            self.box('PD foundation ' + str(i), (p[0], sign * (skin + height / 2 - .2), p[2]), (5, height, 5), 'armor', .25)
            self.point_defense(p, 2.25, i)
        for side in (-1, 1):
            for col in range(4):
                for row in (-1, 1):
                    self.box('Missile magazine hatch', (side * 28.05, row * 5, -66 + col * 6), (.6, 3.5, 4), 'dark', .2)
        lx, ly, lz = self.data['missiles']['launchPoint']
        skin = top(lz) - 1.5
        self.box('Missile transfer trunk', (lx, (skin + ly - 1) / 2, lz), (5, ly - 1 - skin, 8), 'armor', .3)
        self.tube('Active missile launch mouth', (lx, ly, lz), 1.6, 2, 'edge', axis=(0, 1, 0), wall=.2)
        for bay in self.data['bays']:
            bx, by, bz = bay['center']; side = 1 if bx > 0 else -1
            self.box('Utility access ' + bay['id'], (side * 28.1, by, bz), (.3, 4, 6), 'edge', .1)
        for module in self.data['modules']:
            if module['kind'] == 'ShieldEmitter':
                x, y, z = module['center']
                self.box('Shield projector', (x, 27.65, z), (3, .5, 6), 'edge', .1)
                self.box('Shield lens', (x, 27.95, z), (2, .15, 4), 'shield', .05)
        for side in (-1, 1):
            self.rcs((side * 24.9, 0, -100), (side, 0, 0), 1.5, 12)
            self.rcs((side * 15, 15.5, -100), (0, 1, 0), 1.5, 12)
            self.rcs((side * 15, -16.7, -100), (0, -1, 0), 1.5, 12)
            self.rcs((side * 25.6, 0, -100), (0, 0, -1), 1.5, 12)
            self.rcs((side * 32, 0, 120), (side, 0, 0), 1.5, 12)
            for v in (-1, 1): self.rcs((side * 25, v * 21, 120), (0, v, 0), 1.5, 12)
        self.modules_and_markers(); rig_turrets(self.coll, self.data); rig_point_defense(self.coll, self.data)


class Haedongcheong(t5.SmallT5):
    """Needle nose, low canopy, armoured shoulders, cranked delta, canards, close twin nacelles and fins."""
    def __init__(self, key):
        super().__init__(key)
        self.reference = self.form['reference']
        paint(self, {'armor': (.74, .76, .76), 'plate': (.07, .1, .19), 'edge': (.2, .22, .25)})

    def build(self):
        f = self.form; e = True
        self.loft('Main pressure hull', f['core'], 'armor'); self.loft('Tapered prow', f['nose'], 'armor')
        nose = f['nose']
        self.loft('Prow navy band', [(z, w + .03, h + .03, y) for z, w, h, y in (profile_and(nose, -9.6), profile_and(nose, -8.6))], 'plate')
        sx = f['shoulderX']
        for side, suffix in ((-1, 'port'), (1, 'starboard')):
            o = self.loft('Armored shoulder ' + suffix, f['shoulder'], 'armor'); o.location.x += side * sx
            band = self.loft('Shoulder navy band ' + suffix, [(z, w + .03, h + .03, y) for z, w, h, y in (profile_and(f['shoulder'], .4), profile_and(f['shoulder'], 2.2))], 'plate')
            band.location.x += side * sx
        cz = f['cockpitZ']; cw = .85
        self.loft('Cockpit frame', [(cz - 2, .35, .13, 1), (cz, cw, .55, 1.5), (cz + 2.3, cw, .45, 1.5), (cz + 2.8, cw * .8, .1, 1.3)], 'edge')
        self.loft('Cockpit glazing', [(cz - 1.7, .22, .08, 1.2), (cz, cw * .84, .39, 1.7), (cz + 2, cw * .84, .3, 1.7)], 'glass')
        self.rod('Canopy spine', (0, 1.35, cz - 1.6), (0, 2.05, cz + 2), .05, 'edge')
        sensor = next(m for m in self.data['modules'] if m['id'] == 'sensor')
        self.box('Sensor dorsal window', (0, 1.0, sensor['center'][2]), (.42, .08, .8), 'glass', .02)
        mx, my, mz = self.data['railgun']['muzzle']; gun = next(m for m in self.data['modules'] if m['id'] == 'gun'); gx, gy, gz = gun['center']
        self.box('Fixed gun breech', (gx, gy, gz), (.8, .65, 2 * gun['halfSize'][2]), 'dark', .06)
        for side in (-1, 1): self.box('Fixed accelerator rail', (side * .27, my, (mz + gz) / 2), (.21, .3, gz - mz), 'edge', .025)
        self.tube('Fixed muzzle', (mx, my, mz + .12), .24, .24, 'plate', wall=.22)
        ex, _, ez = f['engine']; pw, ph, pd = f['engineHalf']; cant, top = f['fin']; t = f['wingThickness']
        for side, suffix, index in [(-1, 'port', 0), (1, 'starboard', 1)]:
            self.prism(f'Attached wing {suffix} 0', [(side * x, 0, z) for x, z in f['canard']], t * .8, 'armor')
            self.prism(f'Attached wing {suffix} 1', [(side * x, 0, z) for x, z in f['wing']], t, 'armor')
            # Navy outer wing panel and root strake, slightly proud of the ivory wing.
            self.prism(f'Wing navy panel {suffix}', [(side * x, 0, z) for x, z in ((5.0, 1.0), (8.6, 5.6), (8.5, 7.2), (5.2, 7.7))], t + .06, 'plate')
            self.prism(f'Wing root strake {suffix}', [(side * x, 0, z) for x, z in ((1.9, -5.8), (3.0, -2.6), (3.0, -1.2), (1.9, -1.2))], t + .06, 'plate')
            for a, b in ((f['wing'][0], f['wing'][1]), (f['wing'][1], f['wing'][2])):
                self.rod('Wing leading spar', (side * a[0], t / 2, a[1]), (side * b[0], t / 2, b[1]), .07, 'edge')
            pod = self.loft('Engine nacelle ' + suffix, [(ez - pd, pw * .75, ph * .75, 0), (ez - pd + .8, pw, ph, 0), (ez + pd - .7, pw, ph, 0), (ez + pd - .25, pw * .86, ph * .86, 0)])
            pod.location.x += side * ex
            self.box('Nacelle armor band ' + suffix, (side * ex, 0, ez + 1.2), (pw * 2 + .04, ph * 2 + .04, .6), 'plate', .08)
            self.box('IFF band ' + suffix, (side * ex, ph + .04, ez + 1.2), (pw * 1.2, .04, .4), 'iff', .005)
            nozzle = ez + pd + .08; radius = pw * .86
            self.tube('Engine rim ' + str(index), (side * ex, 0, nozzle), radius, .9, 'edge', wall=.2)
            self.cylinder('Engine throat ' + str(index), (side * ex, 0, nozzle - .3), radius * .78, .06, 'engine')
            position = [side * ex, 0, nozzle + .45]
            self.fx['engines'].append(dict(position=position, radius=radius * .78, plume=8, index=index)); self.socket('fx_engine_' + str(index), position)
            fin = [(side * ex, ph * .65, ez - 1.5), (side * (ex + cant), top, ez + .2), (side * (ex + cant), top, ez + 1.7), (side * ex, ph * .65, ez + 2.4)]
            vs = [(x + side * w, y, z) for w in (-.12, .12) for x, y, z in fin]
            self.mesh('Attached fin ' + suffix, vs, [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], 'plate')
            cooling = next(m for m in self.data['modules'] if m['id'] == 'cooling-' + suffix)
            self.box('Cooling cassette ' + suffix, (side * ex, ph - .08, cooling['center'][2]), (pw * 1.4, .35, 1.7), 'radiator', .04)
            for dz in (-.5, 0, .5): self.box('Cooling manifold', (side * ex, ph + .11, cooling['center'][2] + dz), (pw * 1.3, .08, .1), 'edge', .01)
            lx, ly, lz = self.data['missiles']['launchPoint']; lx = side * abs(lx)
            self.box('Missile rack support ' + suffix, (lx - side * .24, ly, lz + 1.2), (.65, .8, 2.5), 'dark', .05)
            self.cylinder('Missile cassette ' + suffix, (lx, ly, lz + 1.25), .38, 2.5, 'plate')
            self.tube('Missile mouth ' + suffix, (lx, ly, lz), .38, .2, 'edge', wall=.2)
            self.box('Utility access ' + suffix, (side * sx, .97, -1.5), (.7, .06, .9), 'edge', .02)
        probes = self.data.get('reconProbes')
        for i, (x, y, z) in enumerate(probes['launchPoints'] if probes else []):
            side = 1 if x > 0 else -1
            self.box('Probe mount ' + str(i), (x - side * .3, y, z), (.65, .5, 1), 'edge', .04)
            self.cylinder('Probe cartridge ' + str(i), (x - side * .25, y, z), .23, .65, 'plate', axis=(side, 0, 0))
            self.cylinder('Probe lens ' + str(i), (x + side * .08, y, z), .20, .05, 'glass', axis=(side, 0, 0))
            self.socket('socket_probe_' + str(i), [x, y, z])
        keel = self.sections['am-keel']; x, y, z = keel['center']; w, h, d = keel['halfSize']
        self.loft('Containment keel', [(z - d, w * .8, h, y), (z - d + .3, w, h, y), (z + d, w, h, y)], 'armor')
        self.box('AM jettison hatch', (0, y - h - .03, z), (w * 1.7, .1, d * 1.7), 'plate', .03)
        rounds = self.data['antimatter']['rounds']
        for i in range(rounds):
            self.box('Containment segment', (0, y - h - .09, z - d * .65 + (i + .5) * 1.3 * d / rounds), (w * 1.45, .05, .1), 'edge', .01)
        self.socket('socket_am_jettison', [0, y - h - .1, z]); self.socket('fx_am_containment', [0, y - h - .1, z])
        self.tube('AM launch mouth', self.data['antimatter']['flight']['launchPoint'], .32, .4, 'edge', wall=.2)
        self.socket('socket_antimatter_launch', self.data['antimatter']['flight']['launchPoint'])
        for i, p in enumerate(self.data['pointDefense']['mounts']):
            sign = 1 if p[1] > 0 else -1
            self.box('PD foundation ' + str(i), (p[0], p[1] - sign * .22, p[2]), (1, .75, 1.3), 'armor', .05)
            self.point_defense(p, .5, i)
        h = max(r[2] for r in f['core'])
        shield = next(m for m in self.data['modules'] if m['id'] == 'shield'); shield_z = shield['center'][2]
        self.box('Shield projector', (0, h - .02, shield_z), (.65, .25, .9), 'edge', .04)
        self.box('Shield lens', (0, h + .12, shield_z), (.4, .04, .55), 'shield', .005)
        bow, bw, bh, by = nose[1]
        for side in (-1, 1):
            self.rcs((side * bw, by, bow), (side, 0, 0), .26, 4)
            for v in (-1, 1): self.rcs((side * bw * .6, by + v * bh, bow), (0, v, 0), .26, 4)
            self.rcs((side * (bw + .13), by, bow), (0, 0, -1), .26, 4)
            self.rcs((side * (ex + pw), 0, ez), (side, 0, 0), .3, 4)
            for v in (-1, 1): self.rcs((side * ex, v * ph, ez), (0, v, 0), .3, 4)
        self.modules(); rig_point_defense(self.coll, self.data)
        for bay in self.data['bays']: self.socket('bay_' + bay['id'].replace('-', '_'), bay['center'], half_size=bay['halfSize'])


def profile_and(rings, z):
    w, h, y = profile_at([tuple(r) for r in rings], z); return (z, w, h, y)


def main():
    args = sys.argv[sys.argv.index('--') + 1:]; render = '--render' in args
    only = args[args.index('--ship') + 1] if '--ship' in args else None
    path = ROOT / 'assets/ships/manifest.json'; manifest = json.loads(path.read_text(encoding='utf-8'))
    for key in KEYS:
        if only and key != only: continue
        bpy.ops.wm.read_factory_settings(use_empty=True); initial = bpy.context.scene
        ship = Haedongcheong(key) if key == 'interceptor_t5' else Amazon(key, render)
        bpy.data.scenes.remove(initial); ship.build(); entry = ship.save()
        # In-game paint pattern (accent blocks, edge wear, streaks) for shaders/hull_panels.gdshader.
        if 'surface' in ship.layout['blockout']: entry['surface'] = ship.layout['blockout']['surface']
        manifest['ships'] = [v for v in manifest['ships'] if v['id'] != key] + [entry]
        path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8'); print('BUILT', key, entry['triangles'], 'triangles', flush=True)


if __name__ == '__main__': main()
