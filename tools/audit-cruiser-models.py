"""Read-only Blender attachment, rig and module audit for the four T3 cruisers."""
import json
import sys
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from branch_model_layout import resolve
from fleet_model_primitives import g
OUT=ROOT/'docs/validation/cruiser_models_20261017'
KEYS=('earth_gun_cruiser','earth_air_defense_cruiser','mars_gun_cruiser','mars_missile_cruiser')
if '--tier4' in sys.argv:
    KEYS=tuple(k+'_t4' for k in KEYS)
    OUT=ROOT/'docs/validation/t4_cruiser_models_20261017'
if '--starters' in sys.argv:
    KEYS=('earth_starter_escort','mars_starter_escort')
    OUT=ROOT/'docs/validation/t1_starter_models_20261017'
reports=[]
for key in KEYS:
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/f'art/blender/{key}.blend'))
    d=resolve(ROOT,key);coll=bpy.data.collections[f'{key} | editable parts'];bpy.context.view_layer.update()
    objects={o.name:o for o in coll.all_objects}
    markers={'module_'+m['id'].replace('-','_'):m['center'] for m in d['modules']}
    markers.update(socket_railgun_muzzle=d['railgun']['muzzle'],socket_missile_launch=d['missiles']['launchPoint'])
    markers.update({f'pd_yaw_{i}':p for i,p in enumerate(d['pointDefense']['mounts'])})
    if '--starters' in sys.argv:
        markers.update({'bay_'+b['id'].replace('-','_'):b['center'] for b in d['bays']})
        manifest=json.loads((ROOT/'assets/ships/manifest.json').read_text(encoding='utf-8'))
        entry=next(e for e in manifest['ships'] if e['id']==key)
        markers.update({f'fx_engine_{e["index"]}':e['position'] for e in entry['engines']})
        markers.update({f'fx_rcs_{i}':e['position'] for i,e in enumerate(entry['rcs'])})
        for module in d['modules']:
            if module['kind']=='ShieldEmitter':
                projector=objects['Shield projector '+module['id']].matrix_world.translation
                assert abs(projector.x-module['center'][0])<.001 and abs(projector.y+module['center'][2])<.001
        assert len(d['railgun']['mounts'])==1 and not any('gun_2' in n or 'gun-2' in n for n in objects)
        launcher=Vector(d['missiles']['launchPoint'])
        ray=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),g(launcher+Vector((0,200,0))),g((0,-1,0)),distance=220)
        assert ray[0] and ray[1].z<launcher.y-.5,(key,'Launcher throat is blocked above its exit',ray[1])
    for mount in d['railgun']['mounts']:
        mid=mount['moduleId'].replace('-','_');markers['turret_'+mid]=mount['pivot']
        sign=-1 if mount['ventral'] else 1
        for i,p in enumerate(mount['muzzles']):
            markers[f'muzzle_{mid}_{i}']=[mount['pivot'][a]+(sign if a<2 else 1)*(mount['trunnion'][a]+p[a]) for a in range(3)]
    def pos(o):
        p=o.matrix_world.translation
        return Vector((p.x,p.z,-p.y))
    errors={name:(pos(objects[name])-Vector(p)).length for name,p in markers.items()}
    assert max(errors.values())<.001,errors
    meshes=[o for o in objects.values() if o.type=='MESH']
    boxes=[]
    for o in meshes:
        vertices=[o.matrix_world@v.co for v in o.data.vertices]
        boxes.append([[min(p[a] for p in vertices) for a in range(3)],[max(p[a] for p in vertices) for a in range(3)]])
    parents=list(range(len(meshes)))
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
    detached=[v for v in groups.values() if len(v)<max(map(len,groups.values()))]
    print(key,'detached candidates:',detached,flush=True)
    assert len([n for n in objects if n.startswith('fx_engine_')])==2,'Exactly two main engines'
    for i in range(2):
        throat=objects['Engine throat '+str(i)]
        result=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),
            throat.matrix_world.translation+g((0,0,200)),g((0,0,-1)),distance=250)
        assert result[0] and result[4].name==throat.name,(key,'Engine throat occluded',result[4])
    reports.append(dict(id=key,markers=len(markers),maxMarkerError=max(errors.values()),
        meshParts=len(meshes),attachmentComponentCount=len(groups),detachedCandidates=detached,visibleEngineThroats=2,
        note='AABB adjacency detects candidate gaps, not proof of mesh contact; visual review also required.'))
    if '--render-top' in sys.argv:
        scene=bpy.context.scene;camera=scene.camera;length=d['flight']['length']
        camera.location=g((0,2*length,-15));target=g((0,0,-15))
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=length*1.8;scene.cycles.samples=16
        scene.render.filepath=str(OUT/f'{key}_top.png');bpy.ops.render.render(write_still=True)
(OUT/'geometry-audit.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8')
assert all(r['attachmentComponentCount']==1 for r in reports),'Detached parts: inspect geometry-audit.json'
print(f'PASS: {len(KEYS)} ship source geometry audits',flush=True)
