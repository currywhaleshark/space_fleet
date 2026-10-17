"""Author only the two T3 interceptor geometries, preserving balance and old assets."""
import hashlib,json,copy
from pathlib import Path
from branch_model_layout import apply_layout,resolve
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/validation/interceptor_models_20261017'
KEYS=('interceptor_t3','mars_interceptor_t3')
OUT.mkdir(parents=True,exist_ok=True)
def write(p,d):p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def diff(a,b):
    r={k:None for k in a if k not in b}
    for k,v in b.items():
        if k not in a:r[k]=v
        elif isinstance(v,dict) and isinstance(a[k],dict):
            part=diff(a[k],v)
            if part:r[k]=part
        elif v!=a[k]:r[k]=v
    return r
baseline=OUT/'baseline.json'
if not baseline.exists():
    files=[p for folder in ('data/ships','data/variants','art/blender','assets/ships','assets/ui/ships') for p in (ROOT/folder).glob('*') if p.is_file() and p.suffix!='.import']
    write(baseline,{'files':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files},'definitions':{k:resolve(ROOT,k) for k in KEYS}})
for key in KEYS:
    d=resolve(ROOT,key);e=key=='interceptor_t3';path=ROOT/f'data/model_layouts/{key}.json'
    if not path.exists():
        sections=[]
        def sec(id,c,h,source):sections.append(dict(id=id,name=id,center=c,halfSize=h,armorFrom=source))
        sec('nose',[0,0,-9.5 if e else -9],[1.4 if e else .92,1 if e else .85,4.5 if e else 4],'nose')
        sec('hull',[0,0,1],[2.4 if e else 1.65,1.6 if e else 1.3,9 if e else 7.8],'hull')
        sec('am-keel',[0,-1.45 if e else -1.15,-2 if e else -2.4],[1.05 if e else .85,1.05 if e else .9,1.8 if e else 1.6],'hull')
        modules={'am-containment':{'center':[0,-1.55 if e else -1.25,-2 if e else -2.4]},'gun':{'center':[0,-.6 if e else -.48,-11.5 if e else -10.5]}}
        ex=4.9 if e else 3.65;ez=9 if e else 7.8
        for sign,suffix,index in [(-1,'port',0),(1,'starboard',1)]:
            sec('engine-'+suffix,[sign*ex,0,ez],[1.35 if e else .98,1.35 if e else 1.12,3.7 if e else 3.5],'engine-block')
            sec('wing-'+suffix,[sign*(4.45 if e else 2.6),0,2 if e else 3],[2.75 if e else 1.8,.45,6 if e else 3.2],'wing-back')
            sec('fin-'+suffix,[sign*(ex+.3),2.05 if e else 1.95,ez+.5],[.55,1.25,2],'fins')
            modules['engine-'+str(index)]={'center':[sign*ex,0,ez+1]}
            modules['cooling-'+suffix]={'center':[sign*ex,0,ez-1.9]}
        layout=dict(id=key,sections=sections,modules=modules,bays={},
            gunMuzzle=[0,-.6 if e else -.48,-16.3 if e else -14.3],
            missileLaunch=[-2.55 if e else -1.8,-.65,-4.1 if e else -3.8],
            amLaunch=[0,-1.55 if e else -1.25,-3.9 if e else -4.1],
            pdMounts=[[0,-1.8,6],[0,1.85,8.2]] if e else [[0,1.52,7.5]],
            probeLaunchPoints=[[s*(2.55 if e else 1.8),.15,-1.3] for s in (-1,1)])
        write(path,layout)
    variant=json.loads((ROOT/f'data/variants/{key}.json').read_text(encoding='utf-8'))
    original=resolve(ROOT,variant['baseHullId'])
    # A repeat application already has the authored sections; source-only armor IDs
    # must still be available. Preserve any existing per-section armor tuning.
    previous={s['id']:s for s in d['hullSections']}
    for section in original['hullSections']:
        if section['id'] not in previous:d['hullSections'].append(copy.deepcopy(section))
    result=apply_layout(ROOT,d)
    if d['modelId']==key:
        for section in result['hullSections']:
            for field in ('armor','faces'):
                if field in previous.get(section['id'],{}):section[field]=copy.deepcopy(previous[section['id']][field])
    variant['patch']=diff(original,result)
    write(ROOT/f'data/variants/{key}.json',variant)
    print('Applied geometry',key)
