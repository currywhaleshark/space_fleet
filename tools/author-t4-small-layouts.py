"""Independent T4 small hull geometry. All performance fields are preserved."""
import copy,hashlib,json
from pathlib import Path
from branch_model_layout import resolve,apply_layout
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'docs/validation/t4_small_models_20261017'
KEYS=('earth_recon_t4','interceptor_t4','mars_assault_t4','mars_interceptor_t4')
OUT.mkdir(parents=True,exist_ok=True)
def write(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
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
    write(baseline,dict(files={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files},definitions={k:resolve(ROOT,k) for k in KEYS}))
for key in KEYS:
    d=resolve(ROOT,key);e=not key.startswith('mars');recon=key=='earth_recon_t4';assault=key=='mars_assault_t4'
    path=ROOT/f'data/model_layouts/{key}.json'
    if not path.exists():
        if recon:
            core=[[-7,2.3,1.45,0],[-3,2.8,1.5,0],[6,2.8,1.5,0],[10.5,1.8,1.4,0]]
            nose=[[-14.5,1.15,.8,0],[-11,1.9,1.1,0],[-5,1.9,1.1,0]]
            wings=[[[1.9,-3],[8,-2],[8,.4],[2,2.5]],[[1.8,4],[5.3,6],[5.3,9],[1.5,9]]]
            engine=[4.8,0,9];half=[1.25,1.3,3.1];fin=[.45,2.8]
            sensor=[0,.5,-11.6];gun=[0,-.6,-12.2];muzzle=[0,-.6,-16.4]
            am=[0,-1.5,-3.3];amh=[.95,1,1.8];pd=[[0,-1.75,7.5],[0,1.75,9]]
            probes=[[x,2.3,4.0] for x in (-1.4,-.7,0,.7,1.4)]
        elif e:
            core=[[-7,1.8,1.3,0],[-3,2.4,1.6,0],[7,2.4,1.6,0],[10.5,1.8,1.4,0]]
            nose=[[-14.5,.48,.6,0],[-9,1.4,1,0],[-5,1.4,1,0]]
            wings=[[[1.9,-1],[8,-6.5],[8,-3],[5,4],[1.9,7]],[[1.8,5],[5.8,5.5],[5.8,10],[1.7,10]]]
            engine=[5.4,0,9];half=[1.4,1.35,3.5];fin=[.7,4.1]
            sensor=[0,.75,-7];gun=[0,-.6,-12.3];muzzle=[0,-.6,-16.7]
            am=[0,-1.5,-3.1];amh=[1,1,1.8];pd=[[0,-1.85,7],[0,1.85,8.5]]
            probes=[[s*2.7,.4,0] for s in (-1,1)]
        elif assault:
            core=[[-6.5,1.45,1.2,0],[-2,2.1,1.5,0],[5,2.1,1.5,0],[9.8,1.4,1.2,0]]
            nose=[[-13.4,.35,.5,0],[-8.7,1.1,.95,0],[-4,1.4,1.1,0]]
            wings=[[[1.4,-8],[6.3,4],[4.7,8.5],[1.4,7.5]]]
            engine=[4.4,0,7.6];half=[1.1,1.2,3.2];fin=[.55,3.5]
            sensor=[0,.7,-6.8];gun=[0,-.48,-11.2];muzzle=[0,-.48,-15.2]
            am=[0,-1.4,-2.8];amh=[1.1,1,2.1];pd=[[0,1.72,7.4]];probes=[]
        else:
            core=[[-6.5,1.3,1.1,0],[-2,1.7,1.25,0],[6,1.7,1.25,0],[9,1.2,1.1,0]]
            nose=[[-13.5,.24,.45,0],[-9,.9,.85,0],[-4.8,.9,.85,0]]
            wings=[[[1.1,-7],[3.7,-5],[3.7,-3.8],[1.1,-4.3]],[[1.2,1],[4.5,4.3],[4.5,6],[1.2,4]]]
            engine=[3.75,0,7];half=[1,1.1,3.5];fin=[.95,2.8]
            sensor=[0,.65,-6.5];gun=[0,-.48,-11];muzzle=[0,-.48,-15.1]
            am=[0,-1.2,-2.8];amh=[.8,.85,1.6];pd=[[0,1.5,7.5]]
            probes=[[s*1.85,.35,-.2] for s in (-1,1)]
        sections=[]
        def sec(id,c,h,source):sections.append(dict(id=id,name=id,center=c,halfSize=h,armorFrom=source))
        def rings(id,shape,source):
            low=min(r[0] for r in shape);high=max(r[0] for r in shape)
            sec(id,[0,0,(low+high)/2],[max(r[1] for r in shape),max(r[2] for r in shape),(high-low)/2],source)
        rings('hull',core,'hull');rings('nose',nose,'nose');sec('am-keel',am,amh,'hull')
        modules={'am-containment':{'center':am},'gun':{'center':gun},'sensor':{'center':sensor},
            'shield':{'center':[0,.6 if e else .48,3.1]}}
        thickness=1.0 if assault else .7
        for side,suffix,index in [(-1,'port',0),(1,'starboard',1)]:
            sec('engine-'+suffix,[side*engine[0],0,engine[2]],half,'engine-block')
            modules['engine-'+str(index)]={'center':[side*engine[0],0,engine[2]+.5]}
            modules['cooling-'+suffix]={'center':[side*engine[0],0,engine[2]-1.6]}
            for i,shape in enumerate(wings):
                xs=[p[0] for p in shape];zs=[p[1] for p in shape]
                sec(f'wing-{suffix}-{i}',[side*(min(xs)+max(xs))/2,0,(min(zs)+max(zs))/2],[(max(xs)-min(xs))/2,thickness/2,(max(zs)-min(zs))/2],'wing-back')
            sec('fin-'+suffix,[side*(engine[0]+fin[0]/2),(half[1]*.65+fin[1])/2,engine[2]+.4],[fin[0]/2+.14,(fin[1]-half[1]*.65)/2,2.1],'fins')
            if recon:sec('sensor-ear-'+suffix,[side*7.6,0,-.8],[.65,.55,1.2],'nose')
        if recon:sec('probe-carrier',[0,1.75,5.25],[1.9,.37,1.35],'hull')
        layout=dict(id=key,sections=sections,modules=modules,bays={},gunMuzzle=muzzle,
            missileLaunch=[-(2.85 if recon else 2.55 if e else 2.2 if assault else 1.85),-.65,-3.8],
            amLaunch=[0,am[1],am[2]-amh[2]-.18],pdMounts=pd,
            blockout=dict(core=core,nose=nose,wings=wings,wingThickness=thickness,engine=engine,engineHalf=half,fin=fin,role='recon' if recon else 'assault' if assault else 'defense' if e else 'agile'))
        if probes:layout['probeLaunchPoints']=probes
        write(path,layout)
    variant=json.loads((ROOT/f'data/variants/{key}.json').read_text(encoding='utf-8'));original=resolve(ROOT,variant['baseHullId'])
    previous={s['id']:s for s in d['hullSections']}
    for s in original['hullSections']:
        if s['id'] not in previous:d['hullSections'].append(copy.deepcopy(s))
    result=apply_layout(ROOT,d)
    if d['modelId']==key:
        for s in result['hullSections']:
            for f in ('armor','faces'):
                if f in previous.get(s['id'],{}):s[f]=copy.deepcopy(previous[s['id']][f])
    variant['patch']=diff(original,result);write(ROOT/f'data/variants/{key}.json',variant)
    print('Applied geometry',key)
