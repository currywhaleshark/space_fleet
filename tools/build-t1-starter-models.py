"""Independent T1 starter blockouts. Legacy scaled originals are archived by author-t1-starter-layouts.py.
Blender --background --factory-startup --python-exit-code 1 --python this.py -- --root REPO --render
After manual editing use export-ship-models.py rather than rebuilding.
"""
import argparse,importlib.util,json,sys
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from fleet_model_export import rig_turrets,rig_point_defense
spec=importlib.util.spec_from_file_location('cruiser_equipment',ROOT/'tools/build-cruiser-models.py')
hardware=importlib.util.module_from_spec(spec);spec.loader.exec_module(hardware)
KEYS=('earth_starter_escort','mars_starter_escort')
OUT=ROOT/'docs/validation/t1_starter_models_20261017'

class Starter(hardware.Cruiser):
    def __init__(self,key,render):
        super().__init__(key,ROOT,render,OUT)
        self.reference='art/reference/20261017_t1_starters/t1-starters.png'

    def engine(self,module):
        core=self.sections['hull']
        try:
            if not self.earth:self.sections['hull']=self.sections['aft-block']
            super().engine(module)
        finally:self.sections['hull']=core

    def radiator(self,module):
        x,y,z=module['center'];w,h,d=module['halfSize'];side=1 if x>0 else -1
        self.box('Radiator frame '+module['id'],(x,y,z),(w*2,h*2,d*2),'edge',min(w,h)*.15)
        if self.earth:
            self.box('Radiator graphite',(x,y+.9,z),(w*1.9,.6,d*1.9),'radiator',0)
            for dz in (-.7,0,.7):self.box('Cooling manifold',(x,y+1.24,z+dz*d),(w*1.9,.18,.5),'copper',.03)
            for dz in (-.7,.7):self.rod('Radiator root',(side*20.8,0,z+dz*d),(side*47,0,z+dz*d),.65,'edge')
        else:
            self.box('Radiator graphite',(x+side*.22,y,z),(.22,h*1.9,d*1.9),'radiator',0)
            for dz in (-.7,0,.7):self.box('Cooling manifold',(x+side*.36,y,z+dz*d),(.12,h*1.9,.6),'copper',.02)
            for dz in (-.82,.82):
                for height in (-8,8):self.rod('Radiator root',(side*15.4,height,z+dz*d),(x,height,z+dz*d),.7,'edge')

    def build(self):
        e=self.earth;core=self.sections['hull'];w,h,_=core['halfSize']
        self.section_loft('hull',(.88,1,1,.91),'armor' if e else 'dark')
        self.section_loft('bow-1',(.83,1,1,.96),'armor')
        self.section_loft('bow-2',(.8,1,1,.98) if e else (.22,.48,1,1),'armor')
        for name in self.sections:
            if name.startswith('turret-base-'):self.section_box(name,'armor',.4)
            elif name=='aft-block':self.section_loft(name,(.94,1,1,.92),'armor')
            elif name=='engineering-keel':self.section_loft(name,(1,1,1,1),'edge')
            elif name.startswith('service-') or name=='starboard-feed':self.section_box(name,'armor',.3)
            elif name.startswith('truss-chord-'):self.section_box(name,'edge',.1)
            elif name=='mast-stem':self.section_box(name,'edge',.3)
        for side in (-1,1):
            for z in ((-70,-8,65) if e else (-67,-18,29)):
                self.box('Pressure collar',(side*(w-.2),0,z),(1.2,h*1.65,5),'plate',.2)
            self.box('Forward identification strip',(side*(11 if e else 7),10.9 if e else 8.7,-106 if e else -91),(3,.4,12),'iff',.05)
            if e:
                for z in (-72,-9,89):self.box('Deck armor mass',(side*11,h-.1,z),(15,1.2,21),'plate',.3)
                self.box('Lower armor shoulder',(side*20,-4,-15),(3,12,46),'plate',.4)
            else:
                for z in (38,77.5):
                    self.box('Engineering collar',(side*15.4,0,z),(2,22,2),'plate',.2)
                # Visible open bays surround actual cooling/generator volumes; the keel and rails join the stern.
                for z0,z1 in ((38,57.7),(57.7,77.5)):
                    self.rod('Engineering diagonal',(side*15.4,-10,z0),(side*15.4,10,z1),.8,'edge')
            # Twelve total magazine covers; the central active exit remains the true launch socket.
            for i in range(6):
                z=(-20 if e else -17)+i*(5.4 if e else 4.6)
                x=side*(16 if e else 11.8)
                self.box('Missile cell cover',(x,h+.4,z),(4.6 if e else 3.8,.8,4.1 if e else 3.5),'edge',.1)
                self.box('Missile cell inset',(x,h+.84,z),(3.5 if e else 2.7,.16,3.1 if e else 2.6),'plate',.03)
        bridge=self.sections['bridge'];bx,by,bz=bridge['center'];bw,bh,bd=bridge['halfSize']
        sensor=next(m for m in self.data['modules'] if m['id']=='sensor');sx,sy,sz=sensor['center'];sw,sh,sd=sensor['halfSize']
        low=by-bh;top=min(by+bh,sy-sh+2)
        self.loft('Bridge supporting trunk',[(bz-bd,bw*.8,(top-low)/2,(top+low)/2),
            (bz-bd+3,bw,(top-low)/2,(top+low)/2),(bz+bd,bw*.85,(top-low)/2,(top+low)/2)],'armor')
        self.box('Sensor protected head',(sx,sy,sz),(sw*2+.5,sh*2+.5,sd*2+.5),'plate',.5)
        for side in (-1,1):
            self.box('Sensor side aperture',(sx+side*(sw+.3),sy,sz),(.2,sh*.7,sd*1.6),'glass',.03)
            self.box('Sensor front aperture',(sx+side*sw*.5,sy,sz-sd-.3),(sw*.7,sh*.7,.2),'glass',.03)
        for x,height in ((-2,6),(2,9)):
            self.rod('Sensor antenna',(x,sy+sh,sz),(x,sy+sh+height,sz),.18,'edge')
        for m in self.data['railgun']['mounts']:self.gun(m)
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            sign=1 if p[1]>=0 else -1;height=max(1,abs(p[1])-h+.5)
            self.box('PD foundation '+str(i),(p[0],sign*(h+height/2-.25),p[2]),(4,height,4),'armor',.2)
            self.point_defense(p,1.8 if e else 1.5,i)
        for m in self.data['modules']:
            if m['kind']=='ShieldEmitter':
                x,y,z=m['center']
                self.box('Shield projector '+m['id'],(x,h+.35,z),(4.5,.8,7),'edge',.15)
                self.box('Shield lens '+m['id'],(x,h+.81,z),(3.2,.16,5.5),'shield',.02)
        bay=self.data['bays'][0];x,y,z=bay['center'];side=1 if x>0 else -1
        self.box('Utility bay hatch',(side*(w+.1),y,z),(.5,5,7),'edge',.2)
        self.box('Utility bay service panel',(side*(w+.4),y,z),(.2,3.6,5),'plate',.05)
        lx,ly,lz=self.data['missiles']['launchPoint']
        trunk_top=ly-.8
        self.box('Missile transfer trunk',(lx,(h+trunk_top)/2,lz),(5,trunk_top-h,6),'armor',.4)
        self.tube('Active missile launch mouth',(lx,ly,lz),1.3,2,'edge',axis=(0,1,0),wall=.18)
        # RCS faces sit on the real skins at both ends, never beyond the tapered bow.
        self.rcs_clusters(-82 if e else -65,100 if e else 85,(w-.5,h-.5),(w-.6,h-.5),1.2,10)
        self.modules_and_markers()
        rig_turrets(self.coll,self.data);rig_point_defense(self.coll,self.data)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--render',action='store_true');parser.add_argument('--ship',choices=KEYS)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    if not (OUT/'baseline.json').exists():raise SystemExit('Run author-t1-starter-layouts.py first to archive the originals.')
    path=ROOT/'assets/ships/manifest.json';manifest=json.loads(path.read_text(encoding='utf-8'))
    for key in KEYS:
        if args.ship and key!=args.ship:continue
        bpy.ops.wm.read_factory_settings(use_empty=True);initial=bpy.context.scene
        ship=Starter(key,args.render);bpy.data.scenes.remove(initial);ship.build();entry=ship.save()
        manifest['ships']=[s for s in manifest['ships'] if s['id']!=key]+[entry]
        path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
        print('BUILT',key,entry['triangles'],'triangles',flush=True)
