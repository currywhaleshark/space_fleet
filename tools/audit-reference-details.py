"""Read-only audit of additive refit sources, contracts and deterministic rebuilds."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import bpy
from mathutils import Vector

parser=argparse.ArgumentParser()
parser.add_argument('--root',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
root=Path(args.root)
sys.path.insert(0,str(root/'tools'))
spec=importlib.util.spec_from_file_location('refit',root/'tools/detail-reference-models.py')
refit=importlib.util.module_from_spec(spec)
spec.loader.exec_module(refit)
from branch_model_layout import resolve
manifest=json.loads((root/'assets/ships/manifest.json').read_text(encoding='utf-8'))
out=root/'docs/validation/model_detail_20261020'
reports=[]

def geometry_hash(objects):
    values=[]
    for obj in sorted(objects,key=lambda o:o.name):
        values.append([obj.name,obj.parent.name if obj.parent else None,
                       [list(v.co) for v in obj.data.vertices],
                       [list(f.vertices) for f in obj.data.polygons]])
    return hashlib.sha256(json.dumps(values).encode()).hexdigest()

for entry in manifest['ships']:
    if not entry.get('surfaceDetail'):
        continue
    key=entry['id']
    bpy.ops.wm.open_mainfile(filepath=str(root/entry['source']))
    coll=bpy.data.collections[key+' | editable parts']
    report=json.loads((out/f'{key}.json').read_text(encoding='utf-8'))
    assert refit.fingerprint(coll.all_objects)==report['originalGeometryHash'],key+': changed original geometry'
    old=[o for o in coll.all_objects if not o.name.startswith(refit.PREFIX)]
    details=[o for o in coll.all_objects if o.name.startswith(refit.PREFIX)]
    assert details and all(o.type=='MESH' for o in details)
    assert all(o.parent is None or o.parent in old for o in details),key+': altered rig parent'
    # Detail must remain a shallow skin, not a new hull with unmatched hit volumes.
    def bounds(objects):
        points=[o.matrix_world@v.co for o in objects if o.type=='MESH' for v in o.data.vertices]
        return [min(p[a] for p in points) for a in range(3)],[max(p[a] for p in points) for a in range(3)]
    lo,hi=bounds(old)
    dlo,dhi=bounds(details)
    expansion=max([max(0,lo[i]-dlo[i],dhi[i]-hi[i]) for i in range(3)])
    definition=resolve(root,key)
    assert expansion<definition['flight']['length']*.02,(key,expansion)
    before=geometry_hash(details)
    for obj in details:
        mesh=obj.data
        bpy.data.objects.remove(obj,do_unlink=True)
        if mesh.users==0:
            bpy.data.meshes.remove(mesh)
    generated=refit.Refit(key,definition,coll).build()
    after=geometry_hash([o for o in coll.all_objects if o.name.startswith(refit.PREFIX)])
    assert before==after,key+': non-deterministic or stale generated detail'
    assert generated['addedTriangles']==report['addedTriangles']
    assert refit.fingerprint(coll.all_objects)==report['originalGeometryHash']
    backup=root/'shots/detail-pass/source-backup'/Path(entry['source']).name
    baseline_verified=False
    if backup.exists():
        bpy.ops.wm.open_mainfile(filepath=str(backup))
        original=bpy.data.collections[key+' | editable parts']
        assert refit.fingerprint(original.all_objects)==report['originalGeometryHash'],key+': differs from pre-refit source'
        baseline_verified=True
    reports.append(dict(id=key,originalGeometryPreserved=True,localBaselineVerified=baseline_verified,
                        deterministicRebuild=True,maximumEnvelopeExpansion=round(expansion,6),
                        detailTriangles=generated['addedTriangles']))
    print('AUDITED',key,flush=True)
(out/'detail-audit.json').write_text(json.dumps(reports,indent=2)+'\n',encoding='utf-8')
print(f'PASS: {len(reports)} additive detail audits; no files saved or exported',flush=True)
