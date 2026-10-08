"""Rig saved PD parts in place, preserving hand edits and source geometry."""
import argparse
import json
import sys
from pathlib import Path
import bpy

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
root = Path(args.root)
sys.path.insert(0, str(root / 'tools'))
from fleet_model_export import rig_point_defense

for key in ('battleship', 'escort', 'interceptor'):
    source = root / 'art/blender' / f'{key}.blend'
    bpy.ops.wm.open_mainfile(filepath=str(source))
    definition = json.loads((root / 'data/ships' / f'{key}.json').read_text(encoding='utf-8-sig'))
    rig_point_defense(bpy.data.collections[f'{key} | editable parts'], definition)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(source), compress=True)
    print('PD RIGGED', key, flush=True)
