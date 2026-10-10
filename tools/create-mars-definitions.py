"""Create the initial Mars hulls; never modify the Earth definitions.

The output JSONs are authoritative after authoring. Re-running intentionally resets Mars tuning.
Geometry and gameplay use these same meter-space sections, modules and sockets.
"""
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def build(kind, name, scale, length):
    d = copy.deepcopy(json.loads((ROOT / f'data/ships/{kind}.json').read_text(encoding='utf-8-sig')))
    d['id'] = 'mars_' + kind
    d['design'] = 'Mars'
    def v(p): return [round(p[i] * scale[i], 5) for i in range(3)]
    for s in d['hullSections']:
        s['center'], s['halfSize'] = v(s['center']), v(s['halfSize'])
        for plate in [s['armor'], *s.get('faces', {}).values()]: plate['thicknessMm'] = round(plate['thicknessMm'] * .70, 2)
    for m in d['modules']:
        m['center'], m['halfSize'] = v(m['center']), v(m['halfSize'])
        m['hitPoints'] = round(m['hitPoints'] * .82, 1)
        m['resistanceMm'] = round(m['resistanceMm'] * .8, 2)
    f = d['flight']; f['displayName'] = name; f['length'] = length
    f['massKg'] *= .68
    for k in ['forwardAccel', 'strafeAccel', 'brakeAccel']: f[k] = round(f[k] * 1.35, 2)
    f['maxAccelG'] *= 1.2
    for k in ['pitchYawRateDeg', 'pitchYawAccelDeg', 'rollRateDeg', 'rollAccelDeg']: f[k] *= 1.3
    f['maxSpeed'] *= 1.22
    f['cameraDistance'] *= scale[2]; f['cameraHeight'] *= scale[1]
    d['shield']['capacity'] *= .68
    d['shield']['rechargePerSecond'] *= .70
    d['sensors']['signature'] *= .82
    d['sensors']['strength'] *= 1.5 if kind != 'escort' else 1.85
    d['sensors']['jammer'] *= 1.15 if kind != 'escort' else 1.55
    gun = d['railgun']; gun['muzzle'] = v(gun['muzzle'])
    for key in ['sensorErrorMeters', 'sensorErrorPerKm', 'velocityError']: gun[key] *= .55
    for m in gun.get('mounts', []):
        for key in ['pivot', 'trunnion', 'housingCenter', 'housingHalfSize']: m[key] = v(m[key])
        m['muzzles'] = [v(p) for p in m['muzzles']]
    d['missiles']['launchPoint'] = v(d['missiles']['launchPoint'])
    pd = d['pointDefense']; pd['mounts'] = [v(p) for p in pd['mounts']]
    if kind == 'battleship':
        # Two precision batteries; a single reactor and generator make redundancy a real tradeoff.
        gun['mounts'] = [m for m in gun['mounts'] if m['moduleId'] != 'gun-2']
        d['modules'] = [m for m in d['modules'] if m['id'] not in ('gun-2', 'reactor-backup', 'generator-starboard')]
        gun.update(muzzleSpeed=18000, reloadSeconds=5.2, energy=2300, penetrationMm=2650, moduleDamage=410,
                   rounds=480, maxRange=310000, shotHeatMj=470)
        for m in gun['mounts']: m['rounds'] = 240
        keep = [0, 1, 6, 7]
        pd['mounts'] = [pd['mounts'][i] for i in keep]; pd['normals'] = [pd['normals'][i] for i in keep]
        d['defenseDrones']['count'] = 4
        d['missiles']['rounds'] = 16
    elif kind == 'escort':
        gun.update(muzzleSpeed=10500, reloadSeconds=2, energy=660, penetrationMm=1000, moduleDamage=195, shotHeatMj=70)
        keep = [2, 3]
        pd['mounts'] = [pd['mounts'][i] for i in keep]; pd['normals'] = [pd['normals'][i] for i in keep]
        d['modules'] = [m for m in d['modules'] if m['id'] != 'generator-starboard']
    else:
        gun.update(reloadSeconds=.7, energy=200, moduleDamage=105)
        pd['mounts'] = pd['mounts'][1:]; pd['normals'] = pd['normals'][1:]
        pd['shotsPerSecond'] = 4; pd['rangeMeters'] = 1000
        d['missiles']['rounds'] = 4
        # The same two-round AM mechanism and direct containment-hit risk as Earth.
        d['antimatter']['flight']['launchPoint'] = v(d['antimatter']['flight']['launchPoint'])
        next(m for m in d['modules'] if m['kind']=='AntimatterContainment')['hitPoints'] = 40
    if kind != 'interceptor':
        # Long precision rails and a shared trunnion/housing center keep the barrels clear of the deck.
        for i, m in enumerate(gun['mounts']):
            size = 22 if kind == 'battleship' else 7
            m['trunnion'] = [0, size*.55, 0]
            m['housingCenter'] = [0, size*.55, size*.12]
            m['housingHalfSize'] = [size, size*.38, size*.96]
            for muzzle in m['muzzles']: muzzle[2] = (-230 if i==0 else -160) if kind=='battleship' else -61
        first = gun['mounts'][0]
        gun['muzzle'] = [first['pivot'][0], first['pivot'][1]+first['trunnion'][1], first['pivot'][2]+first['muzzles'][0][2]]
        sensor = next(m for m in d['modules'] if m['id']=='sensor')
        bridge = next(s for s in d['hullSections'] if s['id']=='bridge')
        sensor['center'][1] = bridge['center'][1]+bridge['halfSize'][1]+sensor['halfSize'][1]+(5 if kind=='battleship' else 1.5)
        d['hullSections'].append(dict(id='sensor-head',name='외부 센서 헤드',center=sensor['center'][:],
            halfSize=[v*1.1 for v in sensor['halfSize']],armor={'thicknessMm':12 if kind=='battleship' else 6}))
        # Exposed vertical radiators beside the central truss. Exact visible and hit-volume coordinates.
        x, y, z, h, depth = (68, 0, 190, 39, 112) if kind == 'battleship' else (22.5, 0, 59, 13.5, 23)
        for side, sign in [('port', -1), ('starboard', 1)]:
            sec = next(s for s in d['hullSections'] if s['id'] == 'radiator-' + side)
            sec['center'] = [sign*x, y, z]; sec['halfSize'] = [1.2 if kind == 'battleship' else .45, h, depth]
            mod = next(m for m in d['modules'] if m['id'] == sec['id'])
            mod['center'] = sec['center']; mod['halfSize'] = [a*.95 for a in sec['halfSize']]
            mod['hitPoints'] *= .72
        # Enclose gun machinery beneath each actual mount, including the escort ventral pedestal.
        for m in gun['mounts']:
            pivot = m['pivot']; ventral = m['ventral']; sign = -1 if ventral else 1
            size = 22 if kind == 'battleship' else 7
            sec = dict(id='turret-base-' + m['moduleId'], name='주포 바베트',
                       center=[pivot[0], pivot[1]-sign*size*.5, pivot[2]], halfSize=[size, size*.55, size],
                       armor={'thicknessMm': 100 if kind == 'battleship' else 38})
            d['hullSections'].append(sec)
    else:
        gun['muzzle'][2] = -17.6
    (ROOT / f"data/ships/{d['id']}.json").write_text(json.dumps(d, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


if __name__ == '__main__':
    build('battleship', '아레스급 전함', (.70, .80, .85), 1080)
    build('escort', '데이모스급 호위함', (.73, .78, .84), 280)
    build('interceptor', '포보스급 요격함', (.70, .80, .85), 26)
