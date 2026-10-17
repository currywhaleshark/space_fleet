"""Preserve the scaled T1 originals, then author independent starter geometry only."""
import copy,hashlib,json,shutil
from pathlib import Path
from branch_model_layout import resolve,apply_layout
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/validation/t1_starter_models_20261017'
KEYS=('earth_starter_escort','mars_starter_escort')
OUT.mkdir(parents=True,exist_ok=True)
def write(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
baseline=OUT/'baseline.json'
if not baseline.exists():
    files=[p for folder in ('data/ships','data/variants','art/blender','assets/ships','assets/ui/ships','data/tree')
           for p in (ROOT/folder).glob('*') if p.is_file() and p.suffix!='.import']
    write(baseline,dict(files={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files},
        definitions={key:resolve(ROOT,key) for key in KEYS}))
    archive=ROOT/'art/archive/20261017_t1_scaled_originals'
    for key in KEYS:
        for rel in (f'art/blender/{key}.blend',f'assets/ships/{key}.glb',f'assets/ui/ships/{key}.png',f'data/variants/{key}.json'):
            target=archive/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/rel,target)
    shutil.copy2(ROOT/'assets/ships/manifest.json',archive/'manifest.json')
before=json.loads(baseline.read_text(encoding='utf-8'))
for key in KEYS:
    original=copy.deepcopy(before['definitions'][key]);e=key.startswith('earth')
    sections=[]
    def section(id,center,half,source='hull'):
        sections.append(dict(id=id,name=id,center=center,halfSize=half,armorFrom=source))
    if e:
        section('hull',[0,0,8],[21.6,15.2,102.4])
        section('bow-1',[0,-.8,-109],[19,12.5,20.5],'bow-1')
        section('bow-2',[0,-1.5,-138],[14,9.5,10],'bow-2')
        section('bridge',[0,24,41],[10,9,16],'bridge')
        section('sensor-head',[0,28,41],[6.9,8.5,7.7],'bridge')
        section('turret-base-gun-1',[0,17.5,-48],[8,3.3,8])
        sensor=[0,28,41]
        for side,suffix in ((-1,'port'),(1,'starboard')):
            section('radiator-'+suffix,[side*35.2,0,64],[14.4,.8,18.4],'radiator-'+suffix)
        pd=[[16.8,17.6,65],[-16.8,17.6,65],[0,-16,32],[0,19.2,-32]]
    else:
        section('hull',[0,0,-21.32],[15.768,11.856,59.32])
        section('bow-1',[0,-.624,-90.72],[12.848,9.36,16.8],'bow-1')
        section('bow-2',[0,-1.872,-115.92],[7.008,6.24,8.4],'bow-2')
        section('bridge',[0,20,20],[8,8.5,12],'bridge')
        section('sensor-head',[0,40,20],[5.1392,6.864,6.6528],'sensor-head')
        section('mast-stem',[0,31,20],[2.8,5,3.8],'bridge')
        section('turret-base-gun-1',[0,13.424,-40.32],[5.6,3.08,5.6],'turret-base-gun-1')
        section('aft-block',[0,0,85],[15.768,11.856,7.736])
        section('engineering-keel',[0,-7,57.7],[5,4,20.2])
        for m in original['modules']:
            if m['id'].startswith(('generator-','cooling-')):
                section('service-'+m['id'],m['center'],[v+.3 for v in m['halfSize']])
        section('starboard-feed',[11.68,0,41.8],[2.92,3,4])
        for side,suffix in ((-1,'port'),(1,'starboard')):
            for up in (-1,1):section(f'truss-chord-{suffix}-{up}',[side*15.4,up*10,57.7],[1,1,20.2])
            section('radiator-'+suffix,[side*20,0,58],[.36,10.8,18.4],'radiator-'+suffix)
        sensor=[0,40,20];pd=copy.deepcopy(original['pointDefense']['mounts'])
    modules={'sensor':dict(center=sensor)}
    for s in sections:
        if s['id'].startswith('radiator-'):modules[s['id']]=dict(center=s['center'])
    layout=dict(id=key,sections=sections,modules=modules,bays={},pdMounts=pd,
        stage='Independent T1 role blockout; surface finish pending')
    write(ROOT/f'data/model_layouts/{key}.json',layout)
    d=apply_layout(ROOT,original)
    variant=json.loads((ROOT/f'data/variants/{key}.json').read_text(encoding='utf-8'))
    variant['patch']=d;write(ROOT/f'data/variants/{key}.json',variant)
    print('Authored T1 geometry',key)
