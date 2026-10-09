"""Fit an aft upper auxiliary turret to the saved interceptor, preserving existing geometry."""
import argparse
import json
import sys
from pathlib import Path
import bpy

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
root = Path(parser.parse_args(sys.argv[sys.argv.index('--') + 1:]).root)
sys.path.insert(0, str(root / 'tools'))
from fleet_model_export import fit_interceptor_dorsal_pd

source = root / 'art/blender/interceptor.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
definition = json.loads((root / 'data/ships/interceptor.json').read_text(encoding='utf-8-sig'))
fit_interceptor_dorsal_pd(bpy.data.collections['interceptor | editable parts'], definition)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(source), compress=True)
print('FITTED interceptor dorsal auxiliary', flush=True)
