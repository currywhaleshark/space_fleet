"""Add yaw/elevation/recoil rigs to existing editable ships; preserve authored geometry."""
import argparse
import json
from pathlib import Path
import sys
import bpy

parser=argparse.ArgumentParser()
parser.add_argument('--root', required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
root=Path(args.root)
sys.path.insert(0,str(root/'tools'))
from fleet_model_export import rig_turrets

for key in ('battleship','escort'):
    path=root/'art/blender'/f'{key}.blend'
    bpy.ops.wm.open_mainfile(filepath=str(path))
    collection=bpy.data.collections[f'{key} | editable parts']
    definition=json.loads((root/'data/ships'/f'{key}.json').read_text(encoding='utf-8-sig'))
    for mount in definition['railgun']['mounts']:
        if collection.all_objects.get('turret_'+mount['moduleId'].replace('-','_')): continue
        # Bring the previously authored open tube lip to the exact logical muzzle plane.
        for obj in collection.all_objects:
            if obj.name.startswith('Rail battery '+mount['moduleId']+' open muzzle'):
                obj.location.y -= (30 if key=='battleship' else 9)*.06
    rig_turrets(collection,definition)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(path),compress=True)
    print('RIGGED',key,flush=True)
