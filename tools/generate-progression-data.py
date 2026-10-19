"""Refresh research data and optionally regenerate starter variants; never rewrite base ships."""
import copy
import json
import argparse
from pathlib import Path
from branch_model_layout import apply_layout

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--research-only', action='store_true', help='Refresh research without regenerating starter hulls')
args = parser.parse_args()
def read(path):
    return json.loads((ROOT/path).read_text(encoding='utf-8-sig'))
def write(path, data):
    dest=ROOT/path; dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def merge(base, patch):
    result=copy.deepcopy(base)
    for key,value in patch.items():
        if value is None: result.pop(key,None)
        elif isinstance(value,dict) and isinstance(result.get(key),dict): result[key]=merge(result[key],value)
        else: result[key]=copy.deepcopy(value)
    return result

starters = [] if args.research_only else [('earth','escort','제피르급 호위함'),('mars','mars_escort','루나에급 호위함')]
for design,base,name in starters:
    d=read(f'data/ships/{base}.json'); s=.8
    d['id']=d['modelId']=f'{design}_starter_escort'; d['flight']['displayName']=name
    for key in ['length','cameraDistance','cameraHeight']: d['flight'][key]*=s
    d['flight']['massKg']*=.75
    d['structureHitPoints']*=.85; d['shield']['capacity']*=.85; d['shield']['rechargePerSecond']*=.85
    d['railgun']['mounts']=d['railgun']['mounts'][:1]
    # A single twin mount with a shorter cycle keeps introductory damage throughput near T2.
    d['railgun']['reloadSeconds']/=1.7
    d['railgun']['rounds']=480; d['railgun']['mounts'][0]['rounds']=480
    d['missiles']['rounds']=12
    d['bays']=d['bays'][:1]
    d['bays'][0]['size']='Small'; d['bays'][0]['accepts']=['Sensor','Ecm']
    d['bays'][0]['halfSize']=[1,1,2]
    d['modules']=[m for m in d['modules'] if m['id']!='gun-2']
    # Keep the underlying medium-class weight: the matcher applies the tier factor once.
    # Scale all local geometry, including modules, bays, collision boxes and weapon pivots.
    vector_keys={'center','halfSize','pivot','trunnion','housingCenter','housingHalfSize','muzzle','launchPoint','socket','position'}
    vector_array_keys={'muzzles','mounts','launchPoints'}
    def geometry(obj):
        if isinstance(obj,dict):
            for key,value in obj.items():
                if key in vector_keys and isinstance(value,list) and len(value)==3 and all(isinstance(x,(float,int)) for x in value):
                    obj[key]=[x*s for x in value]
                elif key in vector_array_keys and isinstance(value,list) and value and isinstance(value[0],list):
                    obj[key]=[[x*s for x in p] for p in value]
                else: geometry(value)
        elif isinstance(obj,list):
            for value in obj: geometry(value)
    geometry(d)
    # Once authored independently, T1 statistics belong to that hull. A later T2
    # sensor/PIP rebalance must not silently replace existing T1 tuning during art regeneration.
    existing=ROOT/f'data/variants/{d["id"]}.json'
    if (ROOT/f'data/model_layouts/{d["id"]}.json').exists() and existing.exists():
        saved=read(str(existing.relative_to(ROOT)))
        d=merge(read(f'data/ships/{saved["baseHullId"]}.json'),saved['patch'])
    d=apply_layout(ROOT,d)
    write(f'data/variants/{d["id"]}.json',dict(id=d['id'],baseHullId=base,tier=1,patch=d))

catalog={p.stem:read(str(p.relative_to(ROOT))) for p in (ROOT/'data/ships').glob('*.json')}
for p in (ROOT/'data/variants').glob('*.json'):
    v=read(str(p.relative_to(ROOT))); catalog[v['id']]=merge(catalog[v['baseHullId']],v['patch'])

SUPPLY=1.08
def first_socket(d):
    if 'mainBattery' in d: return d['mainBattery']['hardpoints'][0]['id']
    gun=d['railgun']
    return gun['mounts'][0]['moduleId'] if gun.get('mounts') else gun['moduleId']

def upgrades(hull,tier):
    d=catalog[hull]; cost=60*tier
    values=[('hull','선체 보강','구조 내구도 +8%',{'structureHitPoints':d['structureHitPoints']*1.08}),
      # Modular weapons §7: the old reload/output research became container upgrades (same IDs, so saves carry over).
      ('fire-control','포좌 확장','첫 주포 소켓 한 단계 확대 · 무장 용적 +2',{'weaponBudget':{'volumeBonus':2,'enlargedSockets':[first_socket(d)]}}),
      ('reactor','주포 전원 개량',f'주포 공급 +{round((SUPPLY-1)*100)}% · 냉각 +15%',{'power':{'coolingMw':d['power']['coolingMw']*1.15},'weaponBudget':{'supplyScale':SUPPLY}}),
      ('drive','추진계 개량','병진 가속 +5%',{'flight':{k:d['flight'][k]*1.05 for k in ['forwardAccel','strafeAccel','brakeAccel']}}),
      ('repair','복구계 개량','응급복구 시간 -8%',{'damageControl':{'repairSeconds':d['damageControl']['repairSeconds']*.92}})]
    return [dict(id=key,name=name,description=desc,cost=cost,matchBonus=.025,patch=patch) for key,name,desc,patch in values]

for family in ['earth','mars']:
    earth=family=='earth'; prefix='' if earth else 'mars_'
    escort=prefix+'escort'; interceptor=prefix+'interceptor'; bb=prefix+'battleship'; starter=family+'_starter_escort'
    gun=family+'_gun_cruiser'; other='earth_air_defense_cruiser' if earth else 'mars_missile_cruiser'
    otherbranch='AirDefenseCruiser' if earth else 'MissileCruiser'; smallbranch='Recon' if earth else 'Assault'
    nodes=[]
    def node(id,name,tier,branch,parent,lane,hull=None):
        nodes.append(dict(id=id,name=name,tier=tier,branch=branch,parentId=parent,lane=lane,hullId=hull,
                          cost=0 if tier==1 else 200*2**(tier-2),upgrades=upgrades(hull,tier) if hull else []))
    for id,tier,branch,parent,lane in [(starter,1,'Escort',None,1),(escort,2,'Escort',starter,1),
        (interceptor,2,'Interceptor',starter,3),(gun,3,'GunCruiser',escort,0),(other,3,otherbranch,escort,2),
        (bb,4,'LineBattleship',gun,1)]:
        node(id,catalog[id]['flight']['displayName'],tier,branch,parent,lane,id)
    series=[(gun,'GunCruiser',0,['나일','아마존'] if earth else ['멜라스','마리네리스'],'포격순양함',4),
        (other,otherbranch,2,['스트라투스','님버스'] if earth else ['아르기레','헬라스'],'방공순양함' if earth else '미사일순양함',4),
        (bb,'LineBattleship',1,['히말라야'] if earth else ['네르갈'],'전함',5),
        (interceptor,'Interceptor',3,['팔케','사크르','해동청'] if earth else ['허셜','하위헌스','스키아파렐리'],'요격함',3),
        (family+'_recon' if earth else family+'_assault',smallbranch,4,['알타이르','카노푸스','폴라리스'] if earth else ['데이모스','포보스','에니오'],'정찰함' if earth else '강습함',3)]
    for base,branch,lane,names,kind,start in series:
        parent=interceptor if branch in ['Recon','Assault'] else base
        for i,name in enumerate(names):
            tier=start+i; id=f'{base}_t{tier}'
            node(id,f'{name}급 {kind}',tier,branch,parent,lane,id if id in catalog else None); parent=id
    write(f'data/tree/{family}.json',dict(design='Earth' if earth else 'Mars',nodes=nodes))
write('data/tree/rules.json',dict(tierMultiplier=1.18,completionPoints=100,victoryPoints=60,
    contributionLimit=140,damagePerPoint=100,interceptionPoints=3,minimumRewardSeconds=30))
