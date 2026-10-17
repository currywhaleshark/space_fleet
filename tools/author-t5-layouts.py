"""T5 geometry contracts. Preserve performance, armor sources and all existing assets."""
import copy, hashlib, json
from pathlib import Path
from branch_model_layout import resolve, apply_layout
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/validation/t5_models_20261017'
KEYS=('battleship_t5','mars_battleship_t5','earth_gun_cruiser_t5','earth_air_defense_cruiser_t5','mars_gun_cruiser_t5','mars_missile_cruiser_t5','interceptor_t5','earth_recon_t5','mars_interceptor_t5','mars_assault_t5')
def write(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def diff(a,b):
    r={k:None for k in a if k not in b}
    for k,v in b.items():
        if k not in a:r[k]=v
        elif isinstance(v,dict) and isinstance(a[k],dict):
            part=diff(a[k],v)
            if part:r[k]=part
        elif v!=a[k]:r[k]=v
    return r
def small(key,d):
    e=not key.startswith('mars');recon='recon' in key;assault='assault' in key
    if recon:
        core=[[-7,2.1,1.4,0],[-3,2.5,1.5,0],[7.5,2.5,1.5,0],[10.5,1.7,1.3,0]]
        nose=[[-14.6,.7,.75,0],[-11,1.8,1.1,0],[-5,1.8,1.1,0]]
        wings=[[[1.8,-2],[10,-2],[10,.1],[1.8,1.2]],[[1.6,4],[4.9,4],[4.9,10],[1.5,10]]]
        engine=[4.2,0,8.6];half=[1.05,1.2,3.3];fin=[.2,4.3]
        sensor=[0,.5,-11];gun=[0,-.6,-12.3];muzzle=[0,-.6,-16.7]
        am=[0,-1.5,-3.3];amh=[.95,1,1.8];pd=[[0,-1.75,7],[0,1.75,8.9]]
        probes=[[x,2.25,3.2] for x in (-1.65,-.99,-.33,.33,.99,1.65)]
    elif e:
        core=[[-7,1.9,1.35,0],[-2,2.9,1.65,0],[6.5,2.9,1.65,0],[10.6,1.7,1.35,0]]
        nose=[[-14.6,.5,.6,0],[-9,1.45,1,0],[-5,1.45,1,0]]
        wings=[[[1.6,-8],[4.2,-6.3],[4.2,-5.3],[1.7,-5.4]],[[2.2,-3],[6.2,1],[9.4,6.2],[8.8,8],[2.1,8.8]]]
        engine=[5.6,0,8.9];half=[1.4,1.4,3.5];fin=[.25,4.5]
        sensor=[0,.75,-7];gun=[0,-.6,-12.3];muzzle=[0,-.6,-16.7]
        am=[0,-1.55,-3.1];amh=[1,1,1.8];pd=[[0,-1.9,6.7],[0,1.9,7.2]]
        probes=[[s*3.05,.8,-.3] for s in (-1,1)]
    elif assault:
        core=[[-6.5,1.55,1.25,0],[-2,2.35,1.6,0],[6,2.35,1.6,0],[10,1.6,1.3,0]]
        nose=[[-13.4,.4,.55,0],[-8.7,1.15,.95,0],[-4,1.4,1.1,0]]
        wings=[[[1.8,-3],[6.1,-.7],[6.1,7.6],[1.6,7.6]]]
        engine=[5.2,0,6.9];half=[1.35,1.55,3.7];fin=[.25,4.4]
        sensor=[0,.7,-6.8];gun=[0,-.48,-11.2];muzzle=[0,-.48,-15.2]
        am=[0,-1.65,-2.8];amh=[1.25,1.2,2.1];pd=[[0,1.85,6.5]];probes=[]
    else:
        core=[[-6.5,1.2,1.1,0],[-2,1.65,1.25,0],[6.5,1.65,1.25,0],[9.4,1.15,1.1,0]]
        nose=[[-13.5,.24,.45,0],[-9,.9,.85,0],[-4.8,.9,.85,0]]
        wings=[[[1,-7.5],[3.4,-6.2],[3.4,-5.3],[1,-5.4]],[[1.2,-1.7],[6.5,7.7],[5.9,8.6],[1.2,5.6]]]
        engine=[3.25,0,7.4];half=[1.05,1.1,3.6];fin=[1.55,2.5]
        sensor=[0,.65,-6.5];gun=[0,-.48,-11];muzzle=[0,-.48,-15.1]
        am=[0,-1.2,-2.8];amh=[.8,.85,1.6];pd=[[0,1.5,7.2]]
        probes=[[s*1.8,.8,-.2] for s in (-1,1)]
    sections=[]
    def sec(i,c,h,source):sections.append(dict(id=i,name=i,center=c,halfSize=h,armorFrom=source))
    def rings(i,shape,source):
        low=min(r[0] for r in shape);high=max(r[0] for r in shape)
        sec(i,[0,0,(low+high)/2],[max(r[1] for r in shape),max(r[2] for r in shape),(high-low)/2],source)
    rings('hull',core,'hull');rings('nose',nose,'nose');sec('am-keel',am,amh,'hull')
    modules={'am-containment':{'center':am},'gun':{'center':gun},'sensor':{'center':sensor},'shield':{'center':[0,.6 if e else .48,1.7]}}
    thickness=1 if assault else .7
    for side,suffix,index in [(-1,'port',0),(1,'starboard',1)]:
        sec('engine-'+suffix,[side*engine[0],0,engine[2]],half,'engine-block')
        modules['engine-'+str(index)]={'center':[side*engine[0],0,engine[2]+.5]}
        modules['cooling-'+suffix]={'center':[side*engine[0],0,engine[2]-1.6]}
        for i,shape in enumerate(wings):
            xs=[p[0] for p in shape];zs=[p[1] for p in shape]
            sec(f'wing-{suffix}-{i}',[side*(min(xs)+max(xs))/2,0,(min(zs)+max(zs))/2],[(max(xs)-min(xs))/2,thickness/2,(max(zs)-min(zs))/2],'wing-back')
        sec('fin-'+suffix,[side*(engine[0]+fin[0]/2),(half[1]*.65+fin[1])/2,engine[2]+.4],[fin[0]/2+.14,(fin[1]-half[1]*.65)/2,2.1],'fins')
        if recon:sec('sensor-ear-'+suffix,[side*9.6,0,-.9],[.8,.75,1.45],'nose')
    if recon:sec('probe-carrier',[0,1.72,4.25],[2.05,.37,1.2],'hull')
    result=dict(id=key,sections=sections,modules=modules,bays={},gunMuzzle=muzzle,
        missileLaunch=[-(2.65 if recon else 2.95 if e else 2.45 if assault else 1.8),-.65,-3.8],
        amLaunch=[0,am[1],am[2]-amh[2]-.18],pdMounts=pd,
        blockout=dict(core=core,nose=nose,wings=wings,wingThickness=thickness,engine=engine,engineHalf=half,fin=fin,role='recon' if recon else 'assault' if assault else 'defense' if e else 'agile'))
    if probes:result['probeLaunchPoints']=probes
    return result
def large(key,d):
    bb='battleship' in key;e=not key.startswith('mars');defense='air_defense' in key;missile='missile' in key
    sections=[dict(id=s['id'],name=s.get('name',s['id']),center=s['center'],halfSize=s['halfSize'],armorFrom=s['id']) for s in d['hullSections']]
    byid={s['id']:s for s in sections};modules={};pd=copy.deepcopy(d['pointDefense']['mounts'])
    def sec(i,c,h,source='hull'):sections.append(dict(id=i,name=i,center=c,halfSize=h,armorFrom=source))
    def change(i,c=None,h=None):
        if c is not None:byid[i]['center']=c
        if h is not None:byid[i]['halfSize']=h
    if bb:
        if e:
            change('bow-1',h=[87,50,42]);change('bow-2',h=[74,38,43]);change('bow-3',h=[58,27,42])
            change('bridge',[0,102,294],[38,43,60]);modules['sensor']={'center':[0,136,294]}
            sec('sensor-head',[0,136,294],[14.5,15.5,20.5],'bridge')
            for s in (-1,1):sec('armor-shoulder-'+str(s),[s*91,-2,-35],[22,48,380])
        else:
            change('hull',[0,0,-100],[59.5,48,282.5]);change('deck',[0,57.6,-130],[33.6,12.8,240]);change('keel',[0,-57.6,-80],[40.6,12,230])
            change('bridge',[0,86,335],[24,39,35]);change('sensor-head',[0,153,335],[10.78,13.2,18.7]);modules['sensor']={'center':[0,153,335]}
            change('engine-block',[0,0,464],[74,51,29]);sec('aft-spine',[0,0,306],[25,27,135])
            sec('mast-stem',[0,132,335],[5,9,7],'bridge');sec('bridge-foundation',[0,37,335],[23,11,30],'bridge')
            for s,suffix in [(-1,'port'),(1,'starboard')]:
                sec('engineering-pod-'+suffix,[s*47,0,270],[17,25,57])
                sec('aft-truss-'+suffix,[s*61,0,330],[3,42,112])
                change('radiator-'+suffix,[s*78,0,240]);modules['radiator-'+suffix]={'center':[s*78,0,240]}
                for j,yy in enumerate((-25.6,25.6)):
                    i=(0 if s<0 else 1)+2*j;modules['engine-'+str(i)]={'center':[s*51,yy,467.5]}
            pd=[[46.2,49.6,-221],[-46.2,49.6,-221],[61,44,370],[-61,44,370]]
        for m in d['railgun']['mounts']:
            id='turret-base-'+m['moduleId'];p=m['pivot'];sign=1 if p[1]>0 else -1
            if id not in byid:sec(id,[p[0],sign*(abs(p[1])-5),p[2]],[m['housingHalfSize'][0],5,m['housingHalfSize'][2]])
    else:
        if e:
            for m in d['railgun']['mounts']:sec('turret-base-'+m['moduleId'],[0,22.5 if m['pivot'][1]>0 else -22.5,m['pivot'][2]],[10,3.5,10])
            if defense:
                change('bow-1',h=[19,14,25]);change('bow-2',h=[8,9,12.5]);change('bridge',[0,29,46],[18,12,22]);modules['sensor']={'center':[0,38,46]}
                sec('sensor-head',[0,38,46],[8.5,10.5,9.5],'bridge')
                for s in (-1,1):
                    sec('defense-deck-'+str(s),[s*44,0,-5],[14,9,100])
                    for j,z in enumerate((-65,0,65)):sec(f'deck-root-{s}-{j}',[s*29,0,z],[8,4,9])
                pd=[[44,11,65],[-44,11,65],[0,-21,45],[0,23,-38],[44,11,-65],[-44,11,-65]]
            else:
                change('bow-1',h=[30,16,25]);change('bow-2',h=[20,11,12.5]);change('bridge',[0,31,58],[17,15,24]);modules['sensor']={'center':[0,36,58]}
                for s in (-1,1):sec('armor-shoulder-'+str(s),[s*26,0,61],[11,16,51])
            for s,suffix in [(-1,'port'),(1,'starboard')]:
                c=[s*(61 if defense else 52),-3,70];change('radiator-'+suffix,c);modules['radiator-'+suffix]={'center':c}
        elif missile:
            change('bridge',[0,26,65],[12,12,18]);change('sensor-head',[0,46,65]);modules['sensor']={'center':[0,46,65]}
            sec('mast-stem',[0,39,65],[3,3,4],'bridge')
            for s,suffix in [(-1,'port'),(1,'starboard')]:
                sec('missile-bank-'+suffix,[s*35,0,-12],[13,12,78])
                for j,z in enumerate((-66,-8,48)):sec(f'bank-root-{s}-{j}',[s*20,0,z],[7,4,8])
                c=[s*27,-16,85];change('radiator-'+suffix,c);modules['radiator-'+suffix]={'center':c}
        else:
            change('hull',[0,0,-44],[19.71,14.82,56.8]);sec('aft-core',[0,0,75],[19.71,14.82,40.92]);sec('mid-spine',[0,0,25.2],[10,10,15])
            change('bow-1',h=[13,11.7,21]);change('bow-2',h=[3.8,7.8,10.5]);change('bridge',[0,26,75],[9,12,14]);change('sensor-head',[0,60,75]);modules['sensor']={'center':[0,60,75]}
            sec('mast-stem',[0,46,75],[3,9,4],'bridge')
            for s,suffix in [(-1,'port'),(1,'starboard')]:
                sec('mid-truss-'+suffix,[s*18.5,0,23.5],[1.2,12,14]);sec('forward-rail-'+suffix,[s*20.5,-7,-73],[3.5,3.5,43])
                c=[s*34,0,83];change('radiator-'+suffix,c);modules['radiator-'+suffix]={'center':c}
    launch=copy.deepcopy(d['missiles']['launchPoint'])
    # A real raised vertical launch mouth, clear of deck and main barrel swept volumes.
    launch=[-55,66,-180] if bb and e else [-35,52,-160] if bb else [-18,23,-104] if e else [-13,19,-86]
    return dict(id=key,sections=sections,modules=modules,bays={'hangar':{'center':[-41.65,0,160]}} if key=='mars_battleship_t5' else {},pdMounts=pd,missileLaunch=launch,blockout=dict(role='battleship' if bb else 'defense' if defense else 'missile' if missile else 'gun'))
def main():
    baseline=OUT/'baseline.json'
    if not baseline.exists():
        files=[p for folder in ('data/ships','data/variants','data/tree','art/blender','assets/ships','assets/ui/ships') for p in (ROOT/folder).glob('*') if p.is_file() and p.suffix!='.import']
        write(baseline,dict(files={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files},definitions={k:resolve(ROOT,k) for k in KEYS}))
    for key in KEYS:
        d=resolve(ROOT,key);path=ROOT/f'data/model_layouts/{key}.json'
        if not path.exists():write(path,small(key,d) if 'interceptor' in key or 'recon' in key or 'assault' in key else large(key,d))
        variant=json.loads((ROOT/f'data/variants/{key}.json').read_text(encoding='utf-8'));base=resolve(ROOT,variant['baseHullId'])
        previous={s['id']:s for s in d['hullSections']}
        for s in base['hullSections']:
            if s['id'] not in previous:d['hullSections'].append(copy.deepcopy(s))
        result=apply_layout(ROOT,d)
        if d['modelId']==key:
            for s in result['hullSections']:
                for f in ('armor','faces'):
                    if f in previous.get(s['id'],{}):s[f]=copy.deepcopy(previous[s['id']][f])
        variant['patch']=diff(base,result);write(ROOT/f'data/variants/{key}.json',variant);print('Applied',key)
if __name__=='__main__':main()
