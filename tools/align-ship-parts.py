"""Repair saved editable sources in place; gameplay sockets and rig transforms stay fixed."""
import argparse
import json
import sys
from pathlib import Path
import bpy

parser=argparse.ArgumentParser();parser.add_argument('--root',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);root=Path(args.root)
sys.path.insert(0,str(root/'tools'))
from fleet_model_alignment import align_ship_parts
for key in ('battleship','escort','interceptor'):
    path=root/'art/blender'/f'{key}.blend';bpy.ops.wm.open_mainfile(filepath=str(path))
    coll=bpy.data.collections[f'{key} | editable parts']
    definition=json.loads((root/'data/ships'/f'{key}.json').read_text(encoding='utf-8-sig'))
    before={o.name:o.matrix_world.copy() for o in coll.all_objects if o.type=='EMPTY'}
    align_ship_parts(coll,definition)
    for name,matrix in before.items():
        assert all(abs(a-b)<1e-6 for row0,row1 in zip(matrix,coll.all_objects[name].matrix_world) for a,b in zip(row0,row1)),name
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(path),compress=True)
    print('ALIGNED',key,'all gameplay/FX marker transforms preserved',flush=True)
