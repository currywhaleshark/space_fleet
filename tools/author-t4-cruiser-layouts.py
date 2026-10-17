"""Author T4 cruiser primary forms without modifying lower or upper tier balance."""
import copy,hashlib,json
from pathlib import Path
from branch_model_layout import apply_layout,resolve
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'docs/validation/t4_cruiser_models_20261017'
KEYS=('earth_gun_cruiser_t4','earth_air_defense_cruiser_t4','mars_gun_cruiser_t4','mars_missile_cruiser_t4')
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
    write(baseline,{'files':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files},'definitions':{p.stem:resolve(ROOT,p.stem) for p in (ROOT/'data/variants').glob('*.json')}})
for key in KEYS:
    d=resolve(ROOT,key);path=ROOT/f'data/model_layouts/{key}.json'
    if not path.exists():
        sections=[dict(id=s['id'],name=s.get('name',s['id']),center=s['center'],halfSize=s['halfSize'],armorFrom=s['id']) for s in d['hullSections']]
        byid={s['id']:s for s in sections};modules={};pd=copy.deepcopy(d['pointDefense']['mounts'])
        def sec(id,c,h,source='hull'):sections.append(dict(id=id,name=id,center=c,halfSize=h,armorFrom=source))
        if key.startswith('earth'):
            for m in d['railgun']['mounts']:
                sec('turret-base-'+m['moduleId'],[0,22.5 if m['pivot'][1]>0 else -22.5,m['pivot'][2]],[10,3.5,10])
        if key=='earth_gun_cruiser_t4':
            byid['bow-1'].update(halfSize=[28,16,25]);byid['bow-2'].update(center=[0,-8,-172.5],halfSize=[7,4,12.5])
            byid['bridge'].update(center=[0,31,70],halfSize=[14,16,20]);modules['sensor']={'center':[0,35,70]}
            for side in (-1,1):
                sec('cheek-'+str(side),[side*24,-1,-115],[12,15,32])
                sec('prow-jaw-'+str(side),[side*16,-3,-171],[8,12.5,14],'bow-2')
        elif key=='earth_air_defense_cruiser_t4':
            byid['bow-2'].update(halfSize=[20,13,12.5])
            byid['bridge'].update(center=[0,28,30],halfSize=[22,11,16]);modules['sensor']={'center':[0,38,30]}
            sec('sensor-head',[0,38,30],[8.5,10.5,9.5],'bridge')
            for side in (-1,1):
                sec('defense-forward-'+str(side),[side*48,0,-75],[12,10,45])
                sec('defense-aft-'+str(side),[side*48,0,27],[12,10,25])
                for j,z in enumerate((-95,-55,15,39)):sec(f'defense-root-{side}-{j}',[side*31,0,z],[7,4,6])
            pd=[[48,12,27],[-48,12,27],[0,-21,45],[0,23,-38],[48,12,-75],[-48,12,-75]]
        else:
            gun=key=='mars_gun_cruiser_t4'
            if gun:
                byid['bow-2'].update(halfSize=[4.4,7.8,10.5]);byid['bow-1'].update(halfSize=[13,11.7,21])
                byid['bridge'].update(center=[0,27,8],halfSize=[9,13,12]);modules['sensor']={'center':[0,58,8]}
                byid['sensor-head'].update(center=[0,58,8])
                sec('mast-stem',[0,45,8],[3,6,4],'bridge')
                for side in (-1,1):sec('forward-rail-'+str(side),[side*23,-9,-61],[3.5,3,40])
            else:
                byid['bridge'].update(center=[0,27,62],halfSize=[12,13,16]);modules['sensor']={'center':[0,48,62]}
                byid['sensor-head'].update(center=[0,48,62])
                sec('mast-stem',[0,40,62],[3,2,4],'bridge')
                for side in (-1,1):
                    for i,z in enumerate((-64,10)):
                        sec(f'missile-pontoon-{side}-{i}',[side*42,0,z],[12,12,30])
                        for j,dz in enumerate((-20,20)):sec(f'pontoon-root-{side}-{i}-{j}',[side*25,0,z+dz],[6,4,5])
            for side,suffix in [(-1,'port'),(1,'starboard')]:
                center=[side*(33 if gun else 31),-6 if gun else -4,68 if gun else 77]
                byid['radiator-'+suffix].update(center=center);modules['radiator-'+suffix]={'center':center}
        write(path,dict(id=key,sections=sections,modules=modules,bays={},pdMounts=pd))
    v=json.loads((ROOT/f'data/variants/{key}.json').read_text(encoding='utf-8'));base=resolve(ROOT,v['baseHullId'])
    previous={s['id']:s for s in d['hullSections']}
    for s in base['hullSections']:
        if s['id'] not in previous:d['hullSections'].append(copy.deepcopy(s))
    result=apply_layout(ROOT,d)
    if d['modelId']==key:
        for s in result['hullSections']:
            for f in ('armor','faces'):
                if f in previous.get(s['id'],{}):s[f]=copy.deepcopy(previous[s['id']][f])
    v['patch']=diff(base,result);write(ROOT/f'data/variants/{key}.json',v)
    print('Applied geometry',key)
