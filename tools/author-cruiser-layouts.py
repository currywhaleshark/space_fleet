"""T3 geometry only. Existing base ships and all higher tiers are left intact.
The JSON layouts are the source of truth; --init authors missing layouts, then applies them.
"""
import copy
import hashlib
import json
from pathlib import Path
import sys
from branch_model_layout import apply_layout, resolve

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/validation/cruiser_models_20261017'
KEYS=('earth_gun_cruiser','earth_air_defense_cruiser','mars_gun_cruiser','mars_missile_cruiser')
OUT.mkdir(parents=True,exist_ok=True)

def write(path,data):path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def diff(base,value):
    result={k:None for k in base if k not in value}
    for k,v in value.items():
        if k not in base:result[k]=v
        elif isinstance(v,dict) and isinstance(base[k],dict):
            part=diff(base[k],v)
            if part:result[k]=part
        elif v!=base[k]:result[k]=v
    return result

baseline=OUT/'baseline.json'
if not baseline.exists():
    files=[p for folder in ('data/ships','data/variants','art/blender','assets/ships','assets/ui/ships')
           for p in (ROOT/folder).glob('*') if p.is_file() and p.suffix not in ('.import',)]
    write(baseline,{'files':{str(p.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in files},
                    'definitions':{p.stem:resolve(ROOT,p.stem) for p in (ROOT/'data/variants').glob('*.json')}})

for key in KEYS:
    d=resolve(ROOT,key);path=ROOT/f'data/model_layouts/{key}.json'
    if not path.exists():
        if '--init' not in sys.argv:raise SystemExit('Missing authored layout: '+key)
        layout={'id':key,'kind':'cruiser','modules':{},'bays':{},'pdMounts':d['pointDefense']['mounts'],
                'inheritedGeometry':{'modelId':d['modelId'],'hullSections':d['hullSections'],
                    'modules':{m['id']:{p:m[p] for p in ('center','halfSize')} for m in d['modules']},
                    'bays':{b['id']:{p:b[p] for p in ('center','halfSize')} for b in d['bays']},
                    'pdMounts':d['pointDefense']['mounts']}}
        sections=[dict(id=s['id'],name=s.get('name',s['id']),center=s['center'],halfSize=s['halfSize'],armorFrom=s['id']) for s in d['hullSections']]
        byid={s['id']:s for s in sections}
        def section(id,center,half,source='hull'):
            sections.append(dict(id=id,name=id,center=center,halfSize=half,armorFrom=source))
        if key=='earth_gun_cruiser':
            byid['bow-1'].update(center=[0,-1,-135],halfSize=[26,17,25])
            byid['bow-2'].update(center=[0,-2,-172.5],halfSize=[18,13,12.5])
            for side in (-1,1):section('gun-shoulder-'+str(side),[side*28,0,-45],[7,17,56])
        elif key=='earth_air_defense_cruiser':
            for side in (-1,1):
                section('pd-deck-forward-'+str(side),[side*36,0,-70],[17,11,43])
                section('pd-deck-aft-'+str(side),[side*36,0,34],[17,11,38])
            # Original PD normals and rate/arc limits remain unchanged.
            layout['pdMounts']=[[39,12,35],[-39,12,35],[0,-20,40],[0,24,-40],[39,12,-70],[-39,12,-70]]
        elif key=='mars_gun_cruiser':
            byid['bow-2'].update(halfSize=[7,7.8,10.5])
            # Sensor module/head remain on the tall central mast; needle body below.
        else:
            for side in (-1,1):
                for i,z in enumerate((-80,-45,-10,25)):
                    section(f'missile-cassette-{side}-{i}',[side*27,0,z],[12,13,17])
            # The launcher is fed by both casette banks through the existing central magazine.
        if key.startswith('earth'):
            byid['bridge'].update(center=[0,36,40],halfSize=[16,18,18])
            for mount in d['railgun']['mounts']:
                y=mount['pivot'][1];sign=1 if y>0 else -1
                section('turret-base-'+mount['moduleId'],[0,sign*22.5,mount['pivot'][2]],[10,3.5,10])
        layout['sections']=sections
        write(path,layout)
    authored=apply_layout(ROOT,d)
    variant=json.loads((ROOT/f'data/variants/{key}.json').read_text(encoding='utf-8'))
    original=resolve(ROOT,variant['baseHullId'])
    variant['patch']=diff(original,authored)
    write(ROOT/f'data/variants/{key}.json',variant)
    print('Applied geometry',key)
