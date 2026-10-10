"""Inspect editable Blender ship attachment gaps and render review views, without saving sources.

blender --background --python tools/audit-ship-models.py -- --root <repo> --output <folder> --render
AABB components are candidate gaps, not proof of a watertight/contacting mesh.
"""
import argparse
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector, Quaternion

parser = argparse.ArgumentParser()
parser.add_argument('--root', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--ship', help='Exact hull ID; omitted checks the Earth fleet')
parser.add_argument('--render', action='store_true')
parser.add_argument('--check', action='store_true')
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
root = Path(args.root)
out = Path(args.output)
if not out.is_absolute(): out = root / out
out.mkdir(parents=True, exist_ok=True)

def g(p): return Vector((p[0], -p[2], p[1]))
def godot(p): return [p.x, p.z, -p.y]
def bounds(points):
    return [[min(p[a] for p in points) for a in range(3)], [max(p[a] for p in points) for a in range(3)]]
def near(a,b,epsilon=.002):
    return all(a[1][i]+epsilon >= b[0][i] and b[1][i]+epsilon >= a[0][i] for i in range(3))

for key in ((args.ship,) if args.ship else ('battleship','escort','interceptor')):
    if args.ship and key != args.ship: continue
    bpy.ops.wm.open_mainfile(filepath=str(root/'art/blender'/f'{key}.blend'))
    coll = bpy.data.collections[f'{key} | editable parts']
    bpy.context.view_layer.update()
    objects = [o for o in coll.all_objects if o.type == 'MESH']
    parts = [{'name':o.name, 'parent':o.parent.name if o.parent else None,
              'bounds':bounds([godot(o.matrix_world@v.co) for v in o.data.vertices])} for o in objects]
    parent = list(range(len(parts)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    # Flat text intentionally sits just above armor to avoid depth flicker.
    solid_indices=[i for i,p in enumerate(parts) if not p['name'].startswith('Stencil')]
    ordered = sorted(solid_indices, key=lambda i:parts[i]['bounds'][0][0])
    for oi,i in enumerate(ordered):
        a = parts[i]['bounds']
        for j in ordered[oi+1:]:
            if parts[j]['bounds'][0][0] > a[1][0]+.002: break
            if near(a,parts[j]['bounds']): parent[find(j)] = find(i)
    groups = {}
    for i in solid_indices: groups.setdefault(find(i),[]).append(parts[i])
    groups = sorted(groups.values(),key=len,reverse=True)
    report = {'ship':key,'parts':parts,'component_exclusions':'planar Stencil meshes with deliberate depth offset','components':[{'count':len(group),
        'bounds':bounds([v for p in group for v in p['bounds']]),'names':[p['name'] for p in group]} for group in groups]}
    (out/f'{key}.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(key, len(parts), 'parts;', len(groups),'AABB components',flush=True)
    if args.check:
        assert len(groups)==1,f'{key}: detached candidate components remain'
        if key.endswith('interceptor'):
            fins=sorted([o for o in objects if o.name.startswith('Vertical fin' if key.startswith('mars_') else 'Canted dorsal fin')],key=lambda o:o.matrix_world.translation.x)
            def quantized(o,mirror):
                return sorted(tuple(round(c,5) for c in ((-p.x if mirror else p.x),p.y,p.z)) for p in [o.matrix_world@v.co for v in o.data.vertices])
            assert quantized(fins[0],True)==quantized(fins[1],False),'Fins are not mirrored'
        else:
            definition=json.loads((root/'data/ships'/f'{key}.json').read_text(encoding='utf-8-sig'))
            for mount in definition['railgun']['mounts']:
                prefix='Rail battery '+mount['moduleId']+' '
                housing=next(p for p in parts if p['name']==prefix+('armored cheek' if key.startswith('mars_') else 'armored mantlet'))['bounds']
                for barrel in [p for p in parts if p['name'].startswith(prefix+'rail shroud')]:
                    b=barrel['bounds'];y=(b[0][1]+b[1][1])*.5
                    assert housing[0][1]<y<housing[1][1],f'{key}: barrel axis is outside housing'
                for side in (() if key.startswith('mars_') else (-1,1)):
                    breech=coll.all_objects[prefix+'breech rail '+str(side)]
                    local=[breech.parent.matrix_world.inverted() @ breech.matrix_world @ v.co for v in breech.data.vertices]
                    assert min(v.y for v in local)<=0<=max(v.y for v in local),'Breech does not reach elevation axle'
        print('PASS attachment topology, barrel housing / pivot reach, or fin symmetry:',key,flush=True)
    if not args.render: continue
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.resolution_x=1280;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    camera=scene.camera
    # Position and view size in game axes/meters; source camera/lighting stay on disk unchanged.
    shots = {
        'battleship': [('hero',(1032,780,-1236),(0,0,-42),1524),
                      ('fore-gun',(190,215,-545),(0,94,-337),270),
                      ('ventral',(670,-570,-680),(0,-35,-40),1070)],
        'escort': [('hero',(258,195,-309),(0,0,-10),390),
                   ('ventral',(135,-125,-170),(0,-12,-15),250)],
        'interceptor': [('hero',(25.8,19.5,-30.9),(0,0,-1),38),
                        ('tail',(12,9,19),(0,1,6),17),
                        ('underside',(20,-16,-23),(0,-.4,0),35)]}.get(key)
    if shots is None:
        definition=json.loads((root/'data/ships'/f'{key}.json').read_text(encoding='utf-8-sig'))
        L=definition['flight']['length']
        shots=[('hero',(L*.86,L*.65,-L*1.03),(0,0,-L*.035),L*1.27),
               ('aft',(L*.7,L*.38,L*.95),(0,0,L*.12),L*1.05),
               ('ventral',(L*.63,-L*.55,-L*.68),(0,0,0),L*1.13)]
        if definition['railgun'].get('mounts'):
            pivot=definition['railgun']['mounts'][0]['pivot']
            shots.append(('fore-gun',(pivot[0]+L*.15,pivot[1]+L*.17,pivot[2]-L*.23),tuple(pivot),L*.32))
    for name,pos,target,size in shots:
        camera.location=g(pos);camera.rotation_euler=(g(target)-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.type='ORTHO'; camera.data.ortho_scale=size
        # Illuminate the underside as well as the authored dorsal studio view.
        fill_data=bpy.data.lights.new('Review fill','AREA');fill_data.energy=size*size*5;fill_data.size=size*.7
        fill=bpy.data.objects.new('Review fill',fill_data);scene.collection.objects.link(fill)
        fill.location=g(pos);fill.rotation_euler=camera.rotation_euler
        scene.render.filepath=str(out/f'{key}-{name}.png')
        bpy.ops.render.render(write_still=True)
        if name=='fore-gun' or (key.endswith('escort') and name=='ventral'):
            posed=[]
            for obj in coll.all_objects:
                if obj.name.startswith('elevation_'):
                    posed.append((obj,obj.rotation_euler.copy()));obj.rotation_euler.x=math.radians(55)
            bpy.context.view_layer.update()
            scene.render.filepath=str(out/f'{key}-{name}-elevated.png')
            bpy.ops.render.render(write_still=True)
            for obj,rotation in posed:obj.rotation_euler=rotation
        bpy.data.objects.remove(fill,do_unlink=True);bpy.data.lights.remove(fill_data)
