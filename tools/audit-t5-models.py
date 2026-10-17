"""Read-only source audit: all functional transforms, nozzle/port visibility, symmetry, candidate gaps."""
import json,sys
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from branch_model_layout import resolve
from fleet_model_primitives import g
OUT=ROOT/(sys.argv[sys.argv.index('--out')+1] if '--out' in sys.argv else 'docs/validation/t5_models_20261017')
ONLY=sys.argv[sys.argv.index('--only')+1].split(',') if '--only' in sys.argv else None
manifest=json.loads((ROOT/'assets/ships/manifest.json').read_text(encoding='utf-8'));reports=[];issues=[]
for path in sorted((ROOT/'data/variants').glob('*_t5.json')):
    key=path.stem
    if ONLY and key not in ONLY:continue
    d=resolve(ROOT,key);entry=next(v for v in manifest['ships'] if v['id']==key)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/f'art/blender/{key}.blend'));bpy.context.view_layer.update()
    objects={o.name:o for o in bpy.data.collections[key+' | editable parts'].all_objects}
    markers={'module_'+m['id'].replace('-','_'):m['center'] for m in d['modules']}
    markers.update(socket_railgun_muzzle=d['railgun']['muzzle'],socket_missile_launch=d['missiles']['launchPoint'])
    markers.update({'bay_'+b['id'].replace('-','_'):b['center'] for b in d['bays']})
    markers.update({f'pd_yaw_{i}':p for i,p in enumerate(d['pointDefense']['mounts'])})
    markers.update({f'fx_engine_{e["index"]}':e['position'] for e in entry['engines']})
    markers.update({f'fx_rcs_{i}':e['position'] for i,e in enumerate(entry['rcs'])})
    if d.get('antimatter'):markers['socket_antimatter_launch']=d['antimatter']['flight']['launchPoint']
    for i,p in enumerate((d.get('reconProbes') or {}).get('launchPoints',[])):markers['socket_probe_'+str(i)]=p
    for mount in d['railgun'].get('mounts',[]):
        mid=mount['moduleId'].replace('-','_');markers['turret_'+mid]=mount['pivot'];sign=-1 if mount['ventral'] else 1
        for i,p in enumerate(mount['muzzles']):markers[f'muzzle_{mid}_{i}']=[mount['pivot'][a]+(sign if a<2 else 1)*(mount['trunnion'][a]+p[a]) for a in range(3)]
    errors={n:(objects[n].matrix_world.translation-g(p)).length for n,p in markers.items()}
    assert max(errors.values())<.001,(key,errors)
    meshes=[o for o in objects.values() if o.type=='MESH'];boxes=[]
    for o in meshes:
        vs=[o.matrix_world@v.co for v in o.data.vertices];boxes.append([[min(p[a] for p in vs) for a in range(3)],[max(p[a] for p in vs) for a in range(3)]])
    parents=list(range(len(meshes)))
    def find(i):
        while parents[i]!=i:i=parents[i]
        return i
    for i,a in enumerate(boxes):
        for j,b in enumerate(boxes[:i]):
            if all(a[1][k]+.015>=b[0][k] and b[1][k]+.015>=a[0][k] for k in range(3)):parents[find(i)]=find(j)
    groups={}
    for i,o in enumerate(meshes):groups.setdefault(find(i),[]).append(o.name)
    detached=[v for v in groups.values() if len(v)<max(map(len,groups.values()))]
    def visible(name,axis):
        obj=objects[name];direction=g(axis);L=d['flight']['length']*2
        hit=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),obj.matrix_world.translation+direction*L,-direction,distance=L+10)
        if not(hit[0] and hit[4].name==name):issues.append((key,'Occluded',name,str(hit[4])))
    engineCount=len([m for m in d['modules'] if m['kind']=='Thruster'])
    assert len(entry['engines'])==engineCount
    for i in range(engineCount):visible('Engine throat '+str(i),(0,0,1))
    probes=(d.get('reconProbes') or {}).get('launchPoints',[])
    for i,p in enumerate(probes):visible('Probe lens '+str(i),(0,0,-1) if 'recon' in key else (1 if p[0]>0 else -1,0,0))
    if 'Attached fin port' in objects:
        def signature(o,mirror=False):return sorted(tuple(round(v,4) for v in ((-p.x if mirror else p.x),p.y,p.z)) for p in [o.matrix_world@v.co for v in o.data.vertices])
        assert signature(objects['Attached fin port'],True)==signature(objects['Attached fin starboard'])
        projector=objects['Shield projector'].matrix_world.translation;shield=next(m for m in d['modules'] if m['id']=='shield')
        assert abs(projector.x-shield['center'][0])<.001 and abs(projector.y+shield['center'][2])<.001
    else:
        launcher=Vector(d['missiles']['launchPoint']);L=d['flight']['length']*2
        hit=bpy.context.scene.ray_cast(bpy.context.evaluated_depsgraph_get(),g(launcher+Vector((0,L,0))),g((0,-1,0)),distance=L+100)
        if not(hit[0] and hit[1].z<launcher.y-.4):issues.append((key,'Launcher blocked',list(hit[1])))
    reports.append(dict(id=key,markers=len(markers),maxMarkerError=max(errors.values()),meshParts=len(meshes),attachmentComponentCount=len(groups),detachedCandidates=detached,visibleEngineThroats=engineCount,visibleProbeHeads=len(probes),symmetricFins='Attached fin port' in objects))
    print(key,'candidate gaps:',detached,flush=True)
(OUT/'geometry-audit.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8')
print('Visibility issues:',issues,flush=True)
assert not issues,issues
assert all(r['attachmentComponentCount']==1 for r in reports),'Inspect detached candidates'
print(f'PASS: {len(reports)} T5 source audits; AABB adjacency is a candidate check, complemented by rendered visual review.')
