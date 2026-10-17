"""Build independent T5 blockouts; never overwrite lower-tier or manually edited originals.
Blender --background --factory-startup --python-exit-code 1 --python this.py -- --root REPO --render
After hand edits, use export-ship-models.py instead of this authoring generator.
"""
import argparse, importlib.util, json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from fleet_model_primitives import g
from fleet_model_export import rig_turrets,rig_point_defense

def load(name,file):
    spec=importlib.util.spec_from_file_location(name,ROOT/'tools'/file)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module
hardware=load('cruiser_hardware','build-cruiser-models.py')
small=load('small_hardware','build-t4-small-models.py')
OUT=ROOT/'docs/validation/t5_models_20261017';OUT.mkdir(parents=True,exist_ok=True)
small.OUT=OUT
KEYS=('battleship_t5','mars_battleship_t5','earth_gun_cruiser_t5','earth_air_defense_cruiser_t5','mars_gun_cruiser_t5','mars_missile_cruiser_t5','interceptor_t5','earth_recon_t5','mars_interceptor_t5','mars_assault_t5')

class SmallT5(small.SmallT4):
    def __init__(self,key):
        super().__init__(key);self.reference='art/reference/20261017_t5_fleet/small.png'
        if not self.earth:
            color=(1,.26,.035,1);material=self.m['engine'];material.diffuse_color=color
            shader=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
            shader.inputs['Base Color'].default_value=color;shader.inputs['Emission Color'].default_value=color
    def build(self):
        super().build()
        for bay in self.data['bays']:self.socket('bay_'+bay['id'].replace('-','_'),bay['center'],half_size=bay['halfSize'])
        if self.form['role']=='assault':
            k=self.sections['am-keel'];w,h,d=k['halfSize'];x,y,z=k['center']
            for side in (-1,1):
                self.box('Armored containment rail',(side*w,y,z),(.15,h*1.5,d*1.8),'plate',.02)
                for dz in (-1.1,0,1.1):self.box('Containment service panel',(side*(w+.08),y,z+dz),(.08,h, .7),'edge',.01)

class CapitalT5(hardware.Cruiser):
    def __init__(self,key,render):
        super().__init__(key,ROOT,render,OUT)
        self.bb='battleship' in key;self.defense='air_defense' in key;self.missile='missile' in key
        self.reference='art/reference/20261017_t5_fleet/'+('battleships.png' if self.bb else 'cruisers.png')
        self.scale=3.6 if self.bb else 1
    def gun(self,mount):
        if not self.bb:return super().gun(mount)
        prefix='Rail battery '+mount['moduleId']+' ';p=Vector(mount['pivot']);sign=-1 if mount['ventral'] else 1
        def world(local):return p+Vector((local[0]*sign,local[1]*sign,local[2]))
        c=mount['housingCenter'];half=mount['housingHalfSize'];s=half[0]/9
        self.cylinder(prefix+'bearing',p,half[0]*.94,1.1*s,'dark',axis=(0,1,0),sides=16)
        neck=max(.6*s,c[1]-half[1]+.2*s)
        self.cylinder(prefix+'turntable',world((0,neck/2,0)),half[0]*.84,neck+.4*s,'edge',axis=(0,1,0),sides=16)
        self.box(prefix+'armored mantlet',world(c),[h*2 for h in half],'plate',.8*s)
        self.box(prefix+'service hatch',world([c[0],c[1]+half[1]+.1*s,c[2]+2*s]),[half[0],.3*s,half[2]],'armor',.1*s)
        for muzzle in mount['muzzles']:
            tip=world(Vector(mount['trunnion'])+Vector(muzzle));start=world(Vector(mount['trunnion'])+Vector((muzzle[0],0,-half[2]*.3)))
            mid=(tip+start)/2;length=abs(tip.z-start.z)
            self.box(prefix+'rail shroud',mid,(1.05*s,1.25*s,length),'dark',.1*s)
            for dx in (-.68,.68):self.box(prefix+'conductor',mid+Vector((dx*s,0,.3*s)),(.22*s,.7*s,length-.6*s),'edge',.04*s)
            for t in (.12,.4,.7,.93):self.box(prefix+'accelerator clamp',start.lerp(tip,t),(1.9*s,1.7*s,1.1*s),'plate',.1*s)
            self.tube(prefix+'open muzzle',tip+Vector((0,0,.3*s)),.65*s,.6*s,'edge',wall=.25)
        for dx in (-1,1):self.box(prefix+'lateral feed',world([dx*(half[0]-.6*s),c[1],c[2]+2*s]),(1.5*s,half[1]*1.4,half[2]),'edge',.15*s)
    def engine(self,module):
        original=self.sections['hull']
        self.sections['hull']=self.sections.get('engine-block',self.sections.get('aft-core',original))
        super().engine(module)
        self.sections['hull']=original
    def radiator(self,module):
        x,y,z=module['center'];w,h,d=module['halfSize'];side=1 if x>0 else -1;s=self.scale
        self.box('Radiator frame '+module['id'],(x,y,z),(w*2,h*2,d*2),'edge',min(w,h)*.2)
        if self.earth:
            self.box('Radiator graphite',(x,y+h*.7,z),(w*1.94,h*.6,d*1.94),'radiator',0)
            for i in range(7):self.box('Cooling channel',(x,y+h*1.03,z-d*.9+i*d*.3),(w*1.88,.16*s,.4*s),'copper',0)
            root=80 if self.bb else 23
            for dz in (-.7,.7):self.rod('Radiator root',(side*root,y,z+dz*d),(x+side*w*.85,y,z+dz*d),.85*s,'edge')
        else:
            self.box('Radiator graphite',(x+side*w*.7,y,z),(w*.6,h*1.94,d*1.94),'radiator',0)
            for i in range(7):self.box('Cooling channel',(x+side*w*1.03,y,z-d*.9+i*d*.3),(.16*s,h*1.88,.4*s),'copper',0)
            root=23 if self.bb else 17
            for dz in (-.8,.8):self.rod('Radiator root',(side*root,y,z+dz*d),(x,y,z+dz*d),.8*s,'edge')
    def build(self):
        e=self.earth;s=self.scale;core=self.sections['hull'];w,h,d=core['halfSize']
        for name,section in self.sections.items():
            if name.startswith('radiator') or name in ('bridge','sensor-head'):continue
            x,y,z=section['center'];sw,sh,sd=section['halfSize']
            if name.startswith(('aft-truss','mid-truss')):
                for yy in (-sh,sh):self.rod(name+' chord',(x,yy,z-sd),(x,yy,z+sd),max(.6,sw*.7),'edge')
                for zz in (z-sd,z,z+sd):self.rod(name+' post',(x,-sh,zz),(x,sh,zz),max(.6,sw*.7),'edge')
                for a,b in ((z-sd,z),(z,z+sd)):self.rod(name+' diagonal',(x,-sh,a),(x,sh,b),max(.6,sw*.6),'edge')
            elif name.startswith(('deck-root','bank-root','turret-base','mast-stem','mid-spine')):
                self.section_box(name,'edge' if 'root' in name or name=='mast-stem' else 'armor',.35*s)
            elif name.startswith('defense-deck'):
                self.section_loft(name,(.38,.82,1,.65),'plate')
                self.box('Defense deck strip',(x,y+sh+.1,z),(sw*.9,.4,sd*1.45),'armor',.15)
                side=1 if x>0 else -1
                for zz in (-25,20):
                    self.box('Drone cradle recess',(x+side*(sw-.3),y,z+zz),(.8,10,17),'dark',.1)
                    for dd in (-4,4):self.box('Defensive drone cradle',(x+side*(sw+.1),y,z+zz+dd),(1.2,2.5,5),'edge',.25)
            elif name.startswith('missile-bank'):
                self.section_loft(name,(.85,1,1,.85))
                for row in (-1,1):
                    for col in range(8):
                        self.box('VLS cell coaming',(x+row*5.3,y+sh+.12,z-63+18*col),(9,.5,13.5),'edge',.2)
                        self.box('VLS armored lid',(x+row*5.3,y+sh+.4,z-63+18*col),(7.6,.3,11.8),'plate',.1)
            else:
                widths=(.9,1,1,.9)
                if name.startswith('bow'):widths=(.35,.7,1,1) if not e else (.7,.9,1,1)
                if name.startswith('forward-rail'):widths=(.15,.7,1,.8)
                self.section_loft(name,widths,'plate' if name.startswith('armor-shoulder') else 'armor')
        # Large structural belts are readable at combat distance; surface microdetail is deferred.
        for side in (-1,1):
            for frac in (-.7,-.25,.3,.75):
                z=core['center'][2]+frac*d
                self.box('Pressure belt',(side*(w-.15),0,z),(1.1*s,h*1.5,6*s),'plate',.2*s)
            self.box('IFF dorsal band',(side*w*.55,h+.08*s,core['center'][2]-d*.5),(4*s,.2*s,10*s),'iff',.04*s)
        sensor=next(m for m in self.data['modules'] if m['id']=='sensor');sx,sy,sz=sensor['center'];sw,sh,sd=sensor['halfSize']
        bridge=self.sections['bridge'];bx,by,bz=bridge['center'];bw,bh,bd=bridge['halfSize']
        bottom=by-bh;top=min(by+bh,sy-sh+.5*s);mid=(top+bottom)/2;bh2=(top-bottom)/2
        self.loft('Bridge supporting trunk',[(bz-bd,bw*.8,bh2,mid),(bz-bd+4*s,bw,bh2,mid),(bz+bd,bw*.8,bh2,mid)],'armor')
        self.box('Sensor protected head',(sx,sy,sz),(sw*2+.4*s,sh*2+.4*s,sd*2+.4*s),'plate',.5*s)
        for side in (-1,1):
            self.box('Sensor side aperture',(sx+side*(sw+.25*s),sy,sz),(.3*s,sh*.9,sd*1.4),'glass',.05*s)
            self.box('Sensor front aperture',(sx+side*sw*.5,sy,sz-sd-.25*s),(sw*.7,sh*.9,.3*s),'glass',.05*s)
        for xx,height in ((0,12),(-3,7),(3,9)):self.rod('Attached antenna',(xx*s,sy+sh,sz),(xx*s,sy+sh+height*s,sz),.22*s,'edge')
        for mount in self.data['railgun']['mounts']:self.gun(mount)
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            sign=1 if p[1]>0 else -1
            skin=9 if self.defense and abs(p[0])>30 else 42 if self.bb and not e and p[2]>300 else h
            height=max(.5*s,abs(p[1])-skin+.4*s)
            self.box('PD foundation '+str(i),(p[0],sign*(skin+height/2-.2*s),p[2]),(5*s,height,5*s),'armor',.25*s)
            self.point_defense(p,(2.25 if e else 1.8)*s,i)
        if not self.missile:
            for side in (-1,1):
                for i in range(self.data['missiles']['rounds']//2):
                    z=core['center'][2]-d*.65+(i//2)*6*s
                    self.box('Missile magazine hatch',(side*w,(-5+10*(i%2))*s,z),(1*s,3.5*s,4*s),'dark',.2*s)
        lx,ly,lz=self.data['missiles']['launchPoint'];top=ly-.8*s
        self.box('Missile transfer trunk',(lx,(h+top)/2,lz),(5*s,abs(top-h)+.3*s,8*s),'armor',.3*s)
        self.tube('Active missile launch mouth',(lx,ly,lz),1.6*s,2*s,'edge',axis=(0,1,0),wall=.2)
        for bay in self.data['bays']:
            bx,by,bz=bay['center'];bw,bh,bd=bay['halfSize']
            owner=next(v for v in self.sections.values() if all(abs(bay['center'][a]-v['center'][a])+bay['halfSize'][a]<=v['halfSize'][a]+.001 for a in range(3)))
            skin=owner['center'][1]+owner['halfSize'][1]
            self.box('Utility access '+bay['id'],(bx,skin+.12*s,bz),(max(3*s,bw),.25*s,max(4*s,bd)),'edge',.1*s)
        for module in self.data['modules']:
            if module['kind']=='ShieldEmitter':
                x,y,z=module['center'];self.box('Shield projector',(x,h+.15*s,z),(3*s,.5*s,6*s),'edge',.1*s)
                self.box('Shield lens',(x,h+.45*s,z),(2*s,.15*s,4*s),'shield',.05*s)
        bow=core['center'][2]-d*.75;stern=core['center'][2]+d*.75
        self.rcs_clusters(bow,stern,(w-.2*s,h),(w-.2*s,h),1.5*s,12*s)
        self.modules_and_markers();rig_turrets(self.coll,self.data);rig_point_defense(self.coll,self.data)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--render',action='store_true');parser.add_argument('--ship',choices=KEYS)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);path=ROOT/'assets/ships/manifest.json';manifest=json.loads(path.read_text(encoding='utf-8'))
    for key in KEYS:
        if args.ship and key!=args.ship:continue
        bpy.ops.wm.read_factory_settings(use_empty=True);initial=bpy.context.scene
        ship=SmallT5(key) if any(a in key for a in ('interceptor','recon','assault')) else CapitalT5(key,args.render)
        bpy.data.scenes.remove(initial);ship.build();entry=ship.save()
        manifest['ships']=[v for v in manifest['ships'] if v['id']!=key]+[entry]
        path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8');print('BUILT',key,entry['triangles'],'triangles',flush=True)
if __name__=='__main__':main()
