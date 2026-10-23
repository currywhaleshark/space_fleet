"""Bake immutable external aiming surfaces from shipped GLBs; never modify a model.
Run DefSnap --external-defs shots/external-defs first. Whitelisted sockets/outer
sections define ownership, not ModuleKind. Buried sensors have no target entry.
Moving barrels are omitted from occlusion; fixed housings and the hull remain.
"""
import gzip, json, struct, hashlib
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'data/targeting'; OUT.mkdir(exist_ok=True)

def load_glb(path):
    raw=path.read_bytes(); n=struct.unpack_from('<I',raw,12)[0]
    doc=json.loads(raw[20:20+n]); binary=raw[28+n:]
    def accessor(i):
        a=doc['accessors'][i]; v=doc['bufferViews'][a['bufferView']]
        dtype={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]
        width={'VEC3':3,'SCALAR':1}[a['type']]; offset=v.get('byteOffset',0)+a.get('byteOffset',0)
        return np.ndarray((a['count'],width),dtype=dtype,buffer=binary,offset=offset,
            strides=(v.get('byteStride',np.dtype(dtype).itemsize*width),np.dtype(dtype).itemsize))
    nodes={}; triangles=[]
    def walk(i,parent,names):
        node=doc['nodes'][i]; name=node.get('name',''); m=np.eye(4)
        if 'matrix' in node:m=np.array(node['matrix']).reshape(4,4).T
        else:
            x,y,z,w=node.get('rotation',[0,0,0,1])
            m[:3,:3]=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],
                [2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],
                [2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])@np.diag(node.get('scale',[1,1,1]))
            m[:3,3]=node.get('translation',[0,0,0])
        m=parent@m;nodes[name]=m[:3,3]; names=names+[name]
        if 'mesh' in node and not any(s.startswith(('recoil_','pd_pitch_')) for s in names):
            for p in doc['meshes'][node['mesh']]['primitives']:
                vertices=accessor(p['attributes']['POSITION']);vertices=vertices@m[:3,:3].T+m[:3,3]
                indices=accessor(p['indices']).ravel() if 'indices' in p else np.arange(len(vertices))
                triangles.append((names,vertices[indices].reshape(-1,3,3)))
        for c in node.get('children',[]):walk(c,m,names)
    for node in doc['scenes'][doc.get('scene',0)]['nodes']:walk(node,np.eye(4),[])
    return nodes,triangles,hashlib.sha256(raw).hexdigest()

catalog={}
for path in sorted((ROOT/'shots/external-defs').glob('*.json')):
    d=json.loads(path.read_text(encoding='utf-8'));model=d['ModelId']
    if model in catalog:continue
    nodes,meshes,digest=load_glb(ROOT/f'assets/ships/{model}.glb')
    mods={m['Id']:m for m in d['Modules']};parts=[]
    def add(id,label,kind,module,node,center=None,half=None,normal=None):
        parts.append(dict(id=id,label=label,kind=kind,moduleId=module,node=node,
            center=center,halfSize=half,normal=normal or [0,0,0]))
    for id in mods:
        node='turret_'+id.replace('-','_')
        if node in nodes:add(id,'주포 '+str(1+sum(p['kind']=='Guns' for p in parts)),'Guns',id,node)
    # Fixed nose gun: only the authored muzzle aperture, not the protected gun centre.
    if not any(p['kind']=='Guns' for p in parts) and d.get('Railgun') and 'socket_railgun_muzzle' in nodes:
        at=nodes['socket_railgun_muzzle'];r=d['Flight']['Length']*.018
        add('nose-gun','전방포','Guns',d['Railgun']['ModuleId'],'socket_railgun_muzzle',at.tolist(),[r,r,r*1.5],[0,0,-1])
    for name in sorted(nodes):
        if name.startswith('pd_yaw_'):
            i=int(name.rsplit('_',1)[1]);add('pd:'+str(i),'방공포 '+str(i+1),'Guns',None,name)
    for id,m in mods.items():
        if not id.startswith('engine-'):continue
        node='fx_engine_'+str(m.get('VisualEngineIndex',-1))
        if node in nodes:
            r=max(m['HalfSize'][:2])*1.5
            add(id,'엔진 '+str(m['VisualEngineIndex']+1),'Engines',id,node,nodes[node].tolist(),[r,r,max(.1,r*.6)],[0,0,1])
    for s in d['HullSections']:
        id=s['Id']
        if id.startswith('radiator-') and id in mods:
            add(id,'좌현 방열판' if 'port' in id else '우현 방열판','Radiators',id,'FleetHull',s['Center'],s['HalfSize'])
        if id=='sensor-head' and 'sensor' in mods:
            add('sensor','센서 마스트','Sensors','sensor','FleetHull',s['Center'],s['HalfSize'])
    # Original Earth tower models predate explicit sensor-head collision sections.
    if model in ('battleship','escort'):
        m=mods['sensor'];half=np.array(m['HalfSize'])*1.6
        add('sensor','센서 마스트','Sensors','sensor','FleetHull',m['Center'],half.tolist())
    all_tri=np.concatenate([v for _,v in meshes]);owners=np.full(len(all_tri),-1,dtype='<i2')
    offset=0
    for names,tri in meshes:
        center=tri.mean(axis=1)
        for i,p in enumerate(parts):
            if p['node'] in names and p['node']!='FleetHull' and p['center'] is None:
                mask=np.ones(len(tri),dtype=bool)
            elif 'FleetHull' in names and p['center'] is not None:
                mask=np.all(np.abs(center-p['center'])<=np.array(p['halfSize'])*1.08,axis=1)
            else:continue
            owners[offset:offset+len(tri)][mask]=i
        offset+=len(tri)
    kept=[]
    for i,p in enumerate(parts):
        own=all_tri[owners==i]
        if not len(own):continue
        lo=own.min(axis=(0,1));hi=own.max(axis=(0,1));p['center']=((lo+hi)*.5).tolist();p['halfSize']=np.maximum((hi-lo)*.5,.03).tolist()
        centers=own.mean(axis=1);samples=[]
        for axis in range(3):
            for sign in (-1,1):
                index=np.argmax(centers[:,axis]*sign);samples.append(centers[index].tolist())
        p['samples']=samples;kept.append(p)
    # Ownership is metadata only: full hull geometry still occludes every surface sample.
    with gzip.GzipFile(filename=str(OUT/(model+'.mesh.gz')),mode='wb',mtime=0) as f:
        f.write(struct.pack('<i',len(all_tri)));f.write(all_tri.astype('<f4').tobytes())
    catalog[model]=dict(sha256=digest,parts=kept)
(OUT/'external_parts.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
print(f'{len(catalog)} models, {sum(len(v["parts"]) for v in catalog.values())} external targets')
