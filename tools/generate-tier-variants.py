"""Stage-six branch variants. Preserve earlier hulls; apply authored branch layouts after stats.
Run before generate-progression-data.py; the latter links generated IDs into the trees.
"""
import copy,json
from pathlib import Path
from branch_model_layout import apply_layout, without_authored_layout
ROOT=Path(__file__).resolve().parents[1]
def read(path):return json.loads((ROOT/path).read_text(encoding='utf-8-sig'))
def merge(base,patch):
    result=copy.deepcopy(base)
    for k,v in patch.items():
        if v is None:result.pop(k,None)
        elif isinstance(v,dict) and isinstance(result.get(k),dict):result[k]=merge(result[k],v)
        else:result[k]=copy.deepcopy(v)
    return result
def diff(base,value):
    result={k:None for k in base if k not in value}
    for k,v in value.items():
        if k not in base:result[k]=v
        elif isinstance(v,dict) and isinstance(base[k],dict):
            part=diff(base[k],v)
            if part:result[k]=part
        elif v!=base[k]:result[k]=v
    return result
def upgrade(d,steps):
    # Modest compound growth, never scaling weapons, health and mobility by 18% all together.
    def scale(obj,key,factor):
        if key in obj:obj[key]*=factor**steps
    scale(d,'structureHitPoints',1.08)
    for m in d['modules']:scale(m,'hitPoints',1.05)
    for k in ['capacity','rechargePerSecond']:scale(d['shield'],k,1.06)
    for k in ['outputMw','coolingMw','heatCapacityMj']:scale(d['power'],k,1.05)
    scale(d['sensors'],'strength',1.04)
    for k in ['forwardAccel','strafeAccel','brakeAccel']:scale(d['flight'],k,1.02)
    for k in ['energy','moduleDamage']:scale(d['railgun'],k,1.05)
    scale(d['railgun'],'penetrationMm',1.04);scale(d['railgun'],'reloadSeconds',.97)
    for k in ['energy','moduleDamage']:scale(d['missiles'],k,1.04)
    if d.get('pointDefense'):scale(d['pointDefense'],'damagePerHit',1.04)
    if d.get('antimatter'):
        for k in ['energy','moduleDamage']:scale(d['antimatter']['flight'],k,1.04)

for family in ['earth','mars']:
    tree=read(f'data/tree/{family}.json'); prefix='' if family=='earth' else 'mars_'
    for node in tree['nodes']:
        id=node['id']
        if '_t' not in id:continue
        branch=node['branch'];tier=node['tier']
        small=branch in ['Interceptor','Recon','Assault']
        base=prefix+('interceptor' if small else 'battleship' if branch=='LineBattleship' else 'escort')
        original=read(f'data/ships/{base}.json'); d=copy.deepcopy(original)
        if not small and branch!='LineBattleship':
            trunk=id.rsplit('_t',1)[0];d=merge(d,read(f'data/variants/{trunk}.json')['patch'])
            d=without_authored_layout(ROOT,d,trunk)
        start=2 if small else 4 if branch=='LineBattleship' else 3
        upgrade(d,tier-start)
        d['id']=id;d['branch']=branch;d['flight']['displayName']=node['name']
        if branch=='LineBattleship':
            # These are prerequisites of this battleship, so a newly unlocked ship can launch.
            for slot in d['squadron']['members'][1:]:slot['hullId']=family+'_gun_cruiser'
        if branch=='Recon':
            d['squadron']['composition']='정찰함 5'
            d['structureHitPoints']*=.9;d['shield']['capacity']*=.9
            d['railgun']['energy']*=.75;d['railgun']['moduleDamage']*=.75
            d['sensors']['strength']*=1.75;d['sensors']['signature']*=.65
            d['antimatter']['rounds']=d['antimatter']['flight']['rounds']=1
            d['reconProbes'].update(count=tier+1,lifetimeSeconds=180+(tier-3)*15,sensorStrength=160+(tier-3)*25,
                deploymentMeters=36000,acceleration=280,burnSeconds=8)
            d['ai']['defaultPips']=[3,1,1,3,0]
        elif branch=='Assault':
            d['squadron']['composition']='강습함 5';d.pop('reconProbes',None)
            d['structureHitPoints']*=1.2;d['flight']['boostMultiplier']*=1.15
            d['flight']['massKg']*=1.12
            for k in ['forwardAccel','strafeAccel','brakeAccel']:d['flight'][k]/=1.12
            d['sensors']['signature']*=1.2
            d['pointDefense']['damagePerHit']*=.75
            d['antimatter']['rounds']=d['antimatter']['flight']['rounds']=tier+1
            d['ai']['defaultPips']=[3,2,2,1,0]
            d['ai']['targetPreference']={'Battleship':0,'Escort':1,'Interceptor':2}
        d=apply_layout(ROOT,d)
        output=dict(id=id,baseHullId=base,tier=tier,patch=diff(original,d))
        (ROOT/f'data/variants/{id}.json').write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Generated 22 tier/role variants; authored layouts reapplied; original hulls/models preserved.')
