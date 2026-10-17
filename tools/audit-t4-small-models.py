"""Read-only validation of T4 small hull sources and physical socket placement."""
import json,sys
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from branch_model_layout import resolve
from fleet_model_primitives import g
OUT=ROOT/'docs/validation/t4_small_models_20261017';reports=[]
for key in ('earth_recon_t4','interceptor_t4','mars_assault_t4','mars_interceptor_t4'):
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/f'art/blender/{key}.blend'))
    d=resolve(ROOT,key);objects={o.name:o for o in bpy.data.collections[key+' | editable parts'].all_objects}
    bpy.context.view_layer.update()
    markers={'module_'+m['id'].replace('-','_'):m['center'] for m in d['modules']}
    markers.update(socket_railgun_muzzle=d['railgun']['muzzle'],socket_missile_launch=d['missiles']['launchPoint'],socket_antimatter_launch=d['antimatter']['flight']['launchPoint'])
    markers.update({f'pd_yaw_{i}':p for i,p in enumerate(d['pointDefense']['mounts'])})
    if d.get('reconProbes'):markers.update({f'socket_probe_{i}':p for i,p in enumerate(d['reconProbes']['launchPoints'])})
    errors={name:(objects[name].matrix_world.translation-g(p)).length for name,p in markers.items()}
    assert max(errors.values())<.001,errors
    meshes=[o for o in objects.values() if o.type=='MESH'];boxes=[]
    for o in meshes:
        points=[o.matrix_world@v.co for v in o.data.vertices]
        boxes.append([[min(p[a] for p in points) for a in range(3)],[max(p[a] for p in points) for a in range(3)]])
    parents=list(range(len(meshes)))
    def find(i):
        while parents[i]!=i:i=parents[i]
        return i
    for i,a in enumerate(boxes):
        for j,b in enumerate(boxes[:i]):
            if all(a[1][k]+.015>=b[0][k] and b[1][k]+.015>=a[0][k] for k in range(3)):parents[find(i)]=find(j)
    groups={}
    for i,o in enumerate(meshes):groups.setdefault(find(i),[]).append(o.name)
    def sig(o,mirror=False):
        return sorted(tuple(round(v,4) for v in ((-p.x if mirror else p.x),p.y,p.z)) for p in [o.matrix_world@v.co for v in o.data.vertices])
    assert sig(objects['Attached fin port'],True)==sig(objects['Attached fin starboard']),'Fin symmetry'
    shield=next(m for m in d['modules'] if m['id']=='shield')
    assert abs(objects['Shield projector'].matrix_world.translation.y+shield['center'][2])<.001,'Shield projector and module fore/aft position'
    assert len([n for n in objects if n.startswith('fx_engine_')])==2
    visible_probes=0
    for i,p in enumerate((d.get('reconProbes') or {}).get('launchPoints',[])):
        head=objects['Probe lens '+str(i)]
        outward=(0,0,-1) if key=='earth_recon_t4' else (1 if p[0]>0 else -1,0,0)
        direction=g(outward)
        hit=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),head.matrix_world.translation+direction*50,-direction,distance=55)
        assert hit[0] and hit[4].name==head.name,(key,'Probe mouth occluded',hit[4])
        visible_probes+=1
    for i in range(2):
        throat=objects['Engine throat '+str(i)]
        hit=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),throat.matrix_world.translation+g((0,0,50)),g((0,0,-1)),distance=55)
        assert hit[0] and hit[4].name==throat.name,(key,'Occluded nozzle',hit[4])
    reports.append(dict(id=key,markers=len(markers),maxMarkerError=max(errors.values()),meshParts=len(meshes),attachmentComponentCount=len(groups),
        detachedCandidates=[a for a in groups.values() if len(a)<max(map(len,groups.values()))],symmetricFins=True,visibleEngineThroats=2,visibleProbeHeads=visible_probes,
        note='AABB adjacency finds candidate gaps; visual review still required.'))
(OUT/'geometry-audit.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8')
for r in reports:print(r,flush=True)
assert all(r['attachmentComponentCount']==1 for r in reports),'Detached candidate geometry'
print('PASS: four T4 small hull source geometry audits')
