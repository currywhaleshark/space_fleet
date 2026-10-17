"""Read-only geometry audit and damage-volume preview for the two T3 blockouts."""
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from branch_model_layout import resolve
from fleet_model_primitives import g, material
OUT=ROOT/'docs/validation/blockout_small_branches_20261015'

def pos(obj):
    v=obj.matrix_world.translation
    return Vector((v.x,v.z,-v.y))

def bounds(obj):
    points=[obj.matrix_world@v.co for v in obj.data.vertices]
    return [[min(p[a] for p in points) for a in range(3)],[max(p[a] for p in points) for a in range(3)]]

reports=[]
for key in ('earth_recon_t3','mars_assault_t3'):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/f'art/blender/{key}.blend'))
    d=resolve(ROOT,key);coll=bpy.data.collections[f'{key} | editable parts'];bpy.context.view_layer.update()
    objects={o.name:o for o in coll.all_objects}
    markers={'module_'+m['id'].replace('-','_'):m['center'] for m in d['modules']}
    markers.update(socket_railgun_muzzle=d['railgun']['muzzle'],socket_missile_launch=d['missiles']['launchPoint'],
                   socket_antimatter_launch=d['antimatter']['flight']['launchPoint'])
    markers.update({f'pd_yaw_{i}':p for i,p in enumerate(d['pointDefense']['mounts'])})
    markers.update({f'socket_probe_{i}':p for i,p in enumerate((d.get('reconProbes') or {}).get('launchPoints',[]))})
    errors={name:(pos(objects[name])-Vector(p)).length for name,p in markers.items()}
    assert max(errors.values())<.001,errors
    meshes=[o for o in objects.values() if o.type=='MESH']
    boxes=[bounds(o) for o in meshes];parents=list(range(len(meshes)))
    def find(i):
        while parents[i]!=i:i=parents[i]
        return i
    for i,a in enumerate(boxes):
        for j in range(i):
            b=boxes[j]
            if all(a[1][k]+.015>=b[0][k] and b[1][k]+.015>=a[0][k] for k in range(3)):
                parents[find(i)]=find(j)
    groups={}
    for i,o in enumerate(meshes):groups.setdefault(find(i),[]).append(o.name)
    assert len(groups)==1, 'Detached candidate groups: '+str(list(groups.values()))
    def signature(o,mirror):
        return sorted(tuple(round(v,4) for v in ((-p.x if mirror else p.x),p.y,p.z)) for p in [o.matrix_world@v.co for v in o.data.vertices])
    fins=[o for o in meshes if o.name.startswith('Attached radiator fin')]
    assert len(fins)==2 and signature(fins[0],True)==signature(fins[1],False),'Fin symmetry'
    assert len([n for n in objects if n.startswith('fx_engine_')])==2,'Exactly two main engines'
    reports.append(dict(id=key,markers=len(markers),maxMarkerError=max(errors.values()),
        attachmentComponents=sorted(groups.values(),key=len,reverse=True),symmetricFins=True,
        note='AABB adjacency detects candidate gaps; visual review is also required.'))
    scene=bpy.context.scene;scene.cycles.samples=16;camera=scene.camera
    camera.location=g((0,48,0));camera.rotation_euler=(0,0,math.pi/2)
    camera.data.ortho_scale=d['flight']['length']*1.15
    scene.render.filepath=str(OUT/f'{key}_top.png');bpy.ops.render.render(write_still=True)
    # Review-only top-down cutaway: wire ribs of the real meshes + colored module volumes.
    # This view is not saved back to the editable source or exported to the game.
    for o in meshes:
        o.hide_render=True
    guides=bpy.data.collections[f'{key} | module hit volumes (guides)'];guides.hide_render=False
    colors={'AntimatterContainment':(.9,.32,.04),'Sensor':(.1,.62,.85),'Gun':(.5,.55,.6),
        'Reactor':(.65,.2,.7),'Thruster':(.22,.48,.8),'Cooling':(.1,.65,.55),
        'PowerBus':(.85,.7,.15),'Generator':(.75,.48,.2),'Magazine':(.8,.2,.15)}
    for m in d['modules']:
        o=guides.objects['HIT VOLUME | '+m['id']];o.hide_render=False;o.hide_set(False)
        o.data.materials.clear();o.data.materials.append(material('Review '+m['id'],colors.get(m['kind'],(.3,.6,.35)),.2,.6))
    wire=material('Review hull wire',(.2,.3,.4),.1,.7)
    for o in meshes:
        if o.parent or any(t in o.name for t in ('cartridge','PD ','RCS ','grille','strap','status')):continue
        copy=o.copy();copy.data=o.data.copy();coll.objects.link(copy);copy.hide_render=False
        copy.data.materials.clear();copy.data.materials.append(wire)
        mod=copy.modifiers.new('Review wire','WIREFRAME');mod.thickness=.024
    scene.render.filepath=str(OUT/f'{key}_modules.png');bpy.ops.render.render(write_still=True)
(OUT/'geometry-audit.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8')
for report in reports:
    print(report['id'],report['markers'],'markers;',len(report['attachmentComponents']),'attachment components',flush=True)
