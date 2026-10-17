"""T4 primary hull forms; reuses validated moving equipment, never rebuilds T3 sources.
Blender --background --factory-startup --python-exit-code 1 --python this.py -- --root REPO --render
After manual edits use export-ship-models.py instead of this generator.
"""
import argparse,importlib.util,json,sys
from pathlib import Path
import bpy
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from fleet_model_export import rig_turrets,rig_point_defense
spec=importlib.util.spec_from_file_location('cruiser_hardware',ROOT/'tools/build-cruiser-models.py')
hardware=importlib.util.module_from_spec(spec);spec.loader.exec_module(hardware)
KEYS=('earth_gun_cruiser_t4','earth_air_defense_cruiser_t4','mars_gun_cruiser_t4','mars_missile_cruiser_t4')
OUT=ROOT/'docs/validation/t4_cruiser_models_20261017';OUT.mkdir(parents=True,exist_ok=True)

class CruiserT4(hardware.Cruiser):
    def __init__(self,key,render):
        super().__init__(key,ROOT,render,OUT)
        self.defense='air_defense' in key;self.missile='missile' in key
        self.reference='art/reference/20261017_t4_cruisers/t4-cruisers.png'

    def radiator(self,module):
        x,y,z=module['center'];w,h,d=module['halfSize'];side=1 if x>0 else -1
        self.box('Radiator frame '+module['id'],(x,y,z),(w*2,h*2,d*2),'edge',min(w,h)*.2)
        if self.earth:
            self.box('Radiator graphite',(x,y+.6,z),(w*1.9,.6,d*1.9),'radiator',0)
            for dz in (-.72,0,.72):self.box('Cooling manifold',(x,y+.92,z+dz*d),(w*1.9,.18,.65),'copper',.04)
            for dz in (-.7,.7):self.rod('Radiator support',(side*24,y,z+dz*d),(x+side*w*.85,y,z+dz*d),.8,'edge')
        else:
            self.box('Radiator graphite',(x+side*.25,y,z),(.5,h*1.9,d*1.9),'radiator',0)
            for dz in (-.72,0,.72):self.box('Cooling manifold',(x+side*.47,y,z+dz*d),(.22,h*1.9,.7),'copper',.04)
            for dz in (-.8,.8):self.rod('Radiator root',(side*17,y,z+dz*d),(x,y,z+dz*d),.85,'edge')

    def build(self):
        e=self.earth;core=self.sections['hull'];w,h,d=core['halfSize']
        self.section_loft('hull',(.93,1,1,.95),'armor' if e else 'dark')
        self.section_loft('bow-1',(.86,1,1,.96),'armor')
        self.section_loft('bow-2',(.9,1,1,1) if e else (.20,.55,1,1),'armor')
        for name,s in self.sections.items():
            if name.startswith(('cheek-','prow-jaw-')):
                self.section_loft(name,(.65,1,1,.85),'plate')
            elif name.startswith(('defense-root-','pontoon-root-','turret-base-','mast-stem')):
                self.section_box(name,'edge' if 'root' in name or name=='mast-stem' else 'armor',.4)
            elif name.startswith('defense-'):
                self.section_loft(name,(.62,1,1,.8),'plate')
                x,y,z=s['center'];sw,sh,sd=s['halfSize'];side=1 if x>0 else -1
                self.box('Defense deck surface',(x,sh+.1,z),(sw*1.6,.4,sd*1.4),'armor',.15)
                if 'aft' in name:
                    self.box('Drone bay recess',(x+side*(sw-.2),0,z),(.7,12,24),'dark',.2)
                    for dz in (-6,6):
                        self.box('Defensive drone cradle',(x+side*(sw+.15),-1,z+dz),(1.2,3,6),'edge',.2)
                        self.box('Cradle retaining beam',(x+side*(sw+.6),-1,z+dz),(.7,3.5,.8),'plate',.1)
            elif name.startswith('forward-rail-'):
                self.section_loft(name,(.4,1,1,.75),'edge')
                x,y,z=s['center'];self.box('Forward spar fairing',(x,y+2,z),(4,.8,55),'plate',.2)
            elif name.startswith('missile-pontoon-'):
                self.section_loft(name,(.78,1,1,.78),'armor')
                x,y,z=s['center'];sw,sh,sd=s['halfSize'];side=1 if x>0 else -1
                for dz in (-21,21):self.box('Missile pod clamp',(x,0,z+dz),(sw*2+.3,sh*2+.3,1.3),'plate',.2)
                for row in (-1,1):
                    for col in range(4):
                        cz=z-18+12*col
                        self.box('Missile cell shutter',(x+side*(sw+.08),row*5,cz),(.4,7.5,9.5),'edge',.2)
                        self.box('Missile hatch face',(x+side*(sw+.3),row*5,cz),(.16,5.6,7.2),'plate',.08)
        # Broad functional masses only: pressure/armor belts and real module service covers.
        for side in (-1,1):
            for z in (-72,-15,45,96):
                zz=z if e else z*.84
                self.box('Pressure belt',(side*(w-.25),0,zz),(1.4,h*1.65,13),'plate',.3)
                if not e:
                    self.rod('Side truss brace',(side*w,-h*.65,zz-18),(side*w,h*.65,zz+18),.8,'edge')
            self.box('Identification band',(side*w*.53,h+.12,-85 if e else -72),(5,.3,12),'iff',.05)
            self.box('Aft cooling access',(side*w*.58,h-.15,82 if e else 69),(8 if e else 6,.8,18),'radiator',.1)
        # The tower encloses the authored sensor; its physical mast bridges every gap.
        sensor=next(m for m in self.data['modules'] if m['id']=='sensor');sx,sy,sz=sensor['center'];sw,sh,sd=sensor['halfSize']
        bridge=self.sections['bridge'];bx,by,bz=bridge['center'];bw,bh,bd=bridge['halfSize']
        bottom=by-bh;top=min(by+bh,sy-sh+.5);half=(top-bottom)/2;cy=(top+bottom)/2
        self.loft('Bridge supporting trunk',[(bz-bd,bw*.86,half,cy),(bz-bd+4,bw,half,cy),(bz+bd-4,bw,half,cy),(bz+bd,bw*.86,half,cy)],'armor')
        self.box('Sensor protected head',(sx,sy,sz),(sw*2+.4,sh*2+.4,sd*2+.4),'plate',.5)
        if self.defense:
            self.box('Transverse sensor bridge',(0,36,30),(43,7,11),'plate',.5)
            self.box('Sensor bridge forward array',(0,36,24.3),(38,4,.5),'glass',.1)
        for side in (-1,1):
            self.box('Sensor side aperture',(sx+side*(sw+.25),sy,sz),(.25,sh*.9,sd*1.4),'glass',.05)
            self.box('Sensor front aperture',(sx+side*sw*.5,sy,sz-sd-.25),(sw*.7,sh*.9,.25),'glass',.05)
        for x,height in ((0,12),(-3,7),(3,9)):
            self.rod('Sensor antenna',(x,sy+sh,sz),(x,sy+sh+height,sz),.22,'edge')
        for m in self.data['railgun']['mounts']:self.gun(m)
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            sign=1 if p[1]>=0 else -1;skin=10 if self.defense and abs(p[0])>30 else h
            height=max(1,abs(p[1])-skin+.5)
            self.box('PD foundation '+str(i),(p[0],sign*(skin+height/2-.25),p[2]),(5,height,5),'armor',.25)
            self.point_defense(p,2.25 if e else 1.8,i)
        if not self.missile:
            for side in (-1,1):
                for i in range(self.data['missiles']['rounds']//2):
                    self.box('Missile magazine hatch',(side*w,-5+10*(i%2),(-100 if e else -84)+6*(i//2)),(1.2,3.5,4),'dark',.2)
        lx,ly,lz=self.data['missiles']['launchPoint']
        self.box('Missile transfer trunk',(lx,(h+ly)/2,lz),(5,abs(ly-h)+2,8),'armor',.4)
        self.tube('Active missile launch mouth',(lx,ly,lz),1.6,2.2,'edge',axis=(0,1,0),wall=.2)
        self.rcs_clusters(-106 if e else -90,110 if e else 92,(w-.7,h*.7),(w-.7,h*.7),1.7,12)
        self.modules_and_markers();rig_turrets(self.coll,self.data);rig_point_defense(self.coll,self.data)

parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--render',action='store_true');parser.add_argument('--ship',choices=KEYS)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
path=ROOT/'assets/ships/manifest.json';manifest=json.loads(path.read_text(encoding='utf-8'))
for key in KEYS:
    if args.ship and args.ship!=key:continue
    bpy.ops.wm.read_factory_settings(use_empty=True);initial=bpy.context.scene;ship=CruiserT4(key,args.render);bpy.data.scenes.remove(initial)
    ship.build();entry=ship.save();manifest['ships']=[s for s in manifest['ships'] if s['id']!=key]+[entry]
    path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8');print('BUILT',key,entry['triangles'],'triangles',flush=True)
