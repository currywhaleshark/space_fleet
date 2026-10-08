"""Export saved, manually edited Blender sources without regenerating their geometry.

blender --background --python tools/export-ship-models.py -- --root <repo> [--ship interceptor]
Module and weapon markers must agree with data/ships before export. Source files
are only read; the temporary merge exists in this Blender process, never on disk.
"""
import argparse
import json
from pathlib import Path
import sys
import bpy
from mathutils import Vector

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
parser.add_argument('--ship', choices=('battleship', 'escort', 'interceptor'))
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
root = Path(args.root)
sys.path.insert(0, str(root / 'tools'))
from fleet_model_export import export_collection
manifest_path = root / 'assets/ships/manifest.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))

def position(obj):
    p = obj.matrix_world.translation
    return [p.x, p.z, -p.y]

def check_socket(objects, name, expected):
    if name not in objects or (Vector(position(objects[name])) - Vector(expected)).length > .001:
        raise ValueError(f'{name}: Blender marker and data/ships disagree. Update both before export.')

for entry in manifest['ships']:
    key = entry['id']
    if args.ship and args.ship != key:
        continue
    bpy.ops.wm.open_mainfile(filepath=str(root / entry['source']))
    collection = bpy.data.collections[f'{key} | editable parts']
    objects = {o.name: o for o in collection.all_objects}
    definition = json.loads((root / f'data/ships/{key}.json').read_text(encoding='utf-8-sig'))
    for module in definition['modules']:
        check_socket(objects, 'module_' + module['id'].replace('-', '_'), module['center'])
    check_socket(objects, 'socket_railgun_muzzle', definition['railgun']['muzzle'])
    check_socket(objects, 'socket_missile_launch', definition['missiles']['launchPoint'])
    if definition.get('antimatter'):
        check_socket(objects,'socket_antimatter_launch',definition['antimatter']['flight']['launchPoint'])
    for i, p in enumerate(definition['pointDefense']['mounts']):
        check_socket(objects, f'socket_point_defense_{i}', p)
    for engine in entry['engines']:
        engine['position'] = position(objects[f"fx_engine_{engine['index']}"])
    for i, jet in enumerate(entry['rcs']):
        socket = objects[f'fx_rcs_{i}']
        jet['position'] = position(socket)
        jet['exhaust'] = list(socket['exhaust'])
        if abs(Vector(jet['exhaust']).length - 1) > .001:
            raise ValueError(f'{key}: RCS {i} exhaust must be a unit direction in Godot axes')
    for mount in definition['railgun'].get('mounts', []):
        key_id = mount['moduleId'].replace('-', '_')
        check_socket(objects, 'turret_' + key_id, mount['pivot'])
        for i, p in enumerate(mount['muzzles']):
            # Markers must be exported at neutral pose, not a saved demonstration aim.
            sign = -1 if mount['ventral'] else 1
            expected = [mount['pivot'][0] + sign*(mount['trunnion'][0]+p[0]),
                mount['pivot'][1] + sign*(mount['trunnion'][1]+p[1]), mount['pivot'][2]+mount['trunnion'][2]+p[2]]
            check_socket(objects, f'muzzle_{key_id}_{i}', expected)
    entry.update(export_collection(collection, root / entry['model']))
    entry['modules'] = len(definition['modules'])
    print(f"EXPORTED {key}: {entry['triangles']} triangles", flush=True)

manifest_path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
