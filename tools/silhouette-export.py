"""Project every hull part of the authored Blender sources to top (z, x) and side (z, -y) triangles.
Blender --background --factory-startup --python this.py -- --root REPO --out DIR --ship ID[,ID...]
Writes DIR/<id>.npz (top, side: float32 [n, 3, 2] in metres, game axes) for silhouette-match.py.
"""
import sys
from pathlib import Path
import bpy
import numpy as np

args = sys.argv[sys.argv.index('--') + 1:]
ROOT = Path(args[args.index('--root') + 1]); OUT = Path(args[args.index('--out') + 1]); OUT.mkdir(parents=True, exist_ok=True)
for key in args[args.index('--ship') + 1].split(','):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / f'art/blender/{key}.blend'))
    graph = bpy.context.evaluated_depsgraph_get(); top = []; side = []
    for obj in bpy.data.collections[key + ' | editable parts'].all_objects:
        if obj.type != 'MESH' or obj.hide_render: continue
        ev = obj.evaluated_get(graph); mesh = ev.to_mesh(); mesh.calc_loop_triangles()
        m = obj.matrix_world
        co = np.array([tuple(m @ v.co) for v in mesh.vertices], dtype=np.float32)  # Blender (x, -z, y)
        tri = np.array([t.vertices[:] for t in mesh.loop_triangles], dtype=np.int64).reshape(-1, 3)
        if len(tri):
            gx, gz, gy = co[:, 0], -co[:, 1], co[:, 2]
            top.append(np.stack([gz, gx], axis=1)[tri]); side.append(np.stack([gz, -gy], axis=1)[tri])
        ev.to_mesh_clear()
    np.savez_compressed(OUT / f'{key}.npz', top=np.concatenate(top), side=np.concatenate(side))
    print('EXPORTED', key, sum(len(t) for t in top), 'triangles', flush=True)
