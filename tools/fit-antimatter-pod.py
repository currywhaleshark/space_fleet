"""Add the AM pod to the existing editable interceptor without regenerating other geometry."""
import argparse,json,sys
from pathlib import Path
import bpy
parser=argparse.ArgumentParser(); parser.add_argument('--root',required=True)
root=Path(parser.parse_args(sys.argv[sys.argv.index('--')+1:]).root)
sys.path.insert(0,str(root/'tools'))
from fleet_model_export import fit_antimatter
path=root/'art/blender/interceptor.blend'
bpy.ops.wm.open_mainfile(filepath=str(path))
definition=json.loads((root/'data/ships/interceptor.json').read_text(encoding='utf-8-sig'))
fit_antimatter(bpy.data.collections['interceptor | editable parts'],definition)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(path),compress=True)
