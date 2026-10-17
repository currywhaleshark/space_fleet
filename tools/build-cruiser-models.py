"""Author the four T3 cruiser blockouts from their geometry contracts.
Blender --background --factory-startup --python-exit-code 1 --python this.py -- --root REPO [--render]
Never regenerates existing original ships. Manual edits afterwards: export-ship-models.py.
"""
import argparse
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).resolve().parent))
from branch_model_layout import resolve
from fleet_model_primitives import Ship,g
from fleet_model_export import rig_turrets,rig_point_defense,export_collection
KEYS=('earth_gun_cruiser','earth_air_defense_cruiser','mars_gun_cruiser','mars_missile_cruiser')

class Cruiser(Ship):
    def __init__(self,key,root,render=False,out=None):
        self.root=root;self.render=render;self.out=out or root/'docs/validation/cruiser_models_20261017'
        self.out.mkdir(parents=True,exist_ok=True)
        self.earth=not key.startswith('mars');self.defense=key=='earth_air_defense_cruiser';self.missile=key=='mars_missile_cruiser'
        super().__init__(root,'escort' if self.earth else 'mars_escort')
        self.key=key;self.data=resolve(root,key);self.scene.name=key
        self.coll.name=key+' | editable parts';self.guides.name=key+' | module hit volumes (guides)'
        self.sections={s['id']:s for s in self.data['hullSections']}
        colors={'armor':(.19,.23,.27) if self.earth else (.115,.13,.15),
                'plate':(.32,.37,.41) if self.earth else (.32,.105,.055),
                'edge':(.10,.135,.16),'copper':(.34,.19,.08),
                'engine':(.12,.48,1) if self.earth else (1,.26,.035)}
        for key,color in colors.items():
            mat=self.m[key];mat.diffuse_color=(*color,1)
            bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(*color,1)
            if key=='engine':bs.inputs['Emission Color'].default_value=(*color,1)

    def section_box(self,section,mat='armor',bevel=.5):
        s=self.sections[section];return self.box(section,s['center'],[v*2 for v in s['halfSize']],mat,bevel)

    def section_loft(self,section,widths=(.8,1,1,.85),mat='armor'):
        s=self.sections[section];x,y,z=s['center'];w,h,d=s['halfSize']
        rings=[(z-d,w*widths[0],h*.85,y),(z-d+min(5,d*.25),w*widths[1],h,y),
               (z+d-min(4,d*.25),w*widths[2],h,y),(z+d,w*widths[3],h*.9,y)]
        obj=self.loft(section,rings,mat);obj.location.x+=x;return obj

    def radiator(self,module):
        x,y,z=module['center'];w,h,d=module['halfSize'];side=1 if x>0 else -1
        self.box('Radiator frame '+module['id'],(x,y,z),(w*2,h*2,d*2),'edge',min(w,h)*.2)
        if self.earth:
            self.box('Radiator graphite',(x,y+.8,z),(w*1.9,.6,d*1.93),'radiator',0)
            for i in range(12):self.box('Radiator channel',(x,y+1.2,z-d*.9+i*d*1.8/11),(w*1.86,.18,.35),'copper',0)
            for zz in (-.7,.7):self.rod('Radiator support',(side*24,0,z+zz*d),(x+side*w*.85,0,z+zz*d),.8,'edge')
        else:
            self.box('Radiator graphite',(x+side*.25,y,z),(.5,h*1.9,d*1.94),'radiator',0)
            for i in range(12):self.box('Radiator channel',(x+side*.55,y,z-d*.9+i*d*1.8/11),(.18,h*1.8,.4),'copper',0)
            for zz in (-.8,.8):self.rod('Radiator root',(side*17,y,z+zz*d),(x,y,z+zz*d),.75,'edge')

    def gun(self,mount):
        prefix='Rail battery '+mount['moduleId']+' ';p=Vector(mount['pivot']);sign=-1 if mount['ventral'] else 1
        def world(local):return p+Vector((local[0]*sign,local[1]*sign,local[2]))
        c=mount['housingCenter'];half=mount['housingHalfSize']
        self.cylinder(prefix+'bearing',p,half[0]*.94,1.1,'dark',axis=(0,1,0),sides=16)
        # The yaw turntable spans the gap between the deck bearing and the housing.
        neck=max(.6,c[1]-half[1]+.2)
        self.cylinder(prefix+'turntable',world((0,neck/2,0)),half[0]*.84,neck+.4,'edge',axis=(0,1,0),sides=16)
        self.box(prefix+'armored mantlet',world(c),[h*2 for h in half],'plate',.8)
        self.box(prefix+'service hatch',world([c[0],c[1]+half[1]+.1,c[2]+2]),[half[0],.3,half[2]],'armor',.05)
        for muzzle in mount['muzzles']:
            tip=world(Vector(mount['trunnion'])+Vector(muzzle));start=world(Vector(mount['trunnion'])+Vector((muzzle[0],0,-half[2]*.3)))
            mid=(tip+start)/2;length=abs(tip.z-start.z)
            # The visible open bore terminates at the exact exported muzzle marker.
            self.box(prefix+'rail shroud',mid,(1.05,1.25,length),'dark',.1)
            for dx in (-.68,.68):self.box(prefix+'conductor',mid+Vector((dx,0,.3)),(.22,.7,length-.6),'edge',.04)
            for t in (.12,.4,.7,.93):self.box(prefix+'accelerator clamp',start.lerp(tip,t),(1.9,1.7,1.1),'plate',.1)
            self.tube(prefix+'open muzzle',tip+Vector((0,0,.3)),.65,.6,'edge',wall=.25)
        for dx in (-1,1):self.box(prefix+'lateral feed',world([dx*(half[0]-.6),c[1],c[2]+2]),(1.5,half[1]*1.4,half[2]),'edge',.15)

    def engine(self,module):
        x,y,z=module['center'];w,h,d=module['halfSize'];i=module['visualEngineIndex']
        core=self.sections['hull'];aft=core['center'][2]+core['halfSize'][2]
        radius=min(w,h)*.85;mouth=max(z+d,aft)+radius*.65
        cradle_end=mouth-radius*.7;cradle_start=z-d
        self.box('Engine cradle '+str(i),(x,y,(cradle_start+cradle_end)/2),
            (radius*2.2,radius*2.2,cradle_end-cradle_start),'armor',1.2)
        self.tube('Engine nozzle '+str(i),(x,y,mouth-radius*.3),radius,radius*1.05,'edge',wall=.18)
        self.cylinder('Engine throat '+str(i),(x,y,mouth-radius*.35),radius*.78,.3,'engine',sides=20)
        for n in range(8):
            a=n*math.tau/8;self.rod('Engine cooling rib',(x+radius*math.cos(a),y+radius*math.sin(a),mouth-radius),
                (x+radius*math.cos(a),y+radius*math.sin(a),mouth),.22,'copper')
        self.fx['engines'].append(dict(position=[x,y,mouth+radius*.225],radius=radius*.82,plume=28,index=i))
        self.socket('fx_engine_'+str(i),[x,y,mouth+radius*.225])

    def modules_and_markers(self):
        for module in self.data['modules']:
            self.socket('module_'+module['id'].replace('-','_'),module['center'],module_id=module['id'],module_kind=module['kind'],half_size=module['halfSize'])
            obj=self.box('HIT VOLUME | '+module['id'],module['center'],[v*2 for v in module['halfSize']],'iff',0)
            self.coll.objects.unlink(obj);self.guides.objects.link(obj);self.meshes.remove(obj)
            obj.display_type='WIRE';obj.hide_render=True;obj.hide_set(True)
            if module['id'].startswith('radiator-'):self.radiator(module)
            if module['kind']=='Thruster':self.engine(module)
        self.socket('socket_railgun_muzzle',self.data['railgun']['muzzle'])
        self.socket('socket_missile_launch',self.data['missiles']['launchPoint'])
        for i,p in enumerate(self.data['pointDefense']['mounts']):self.socket('socket_point_defense_'+str(i),p)
        for bay in self.data['bays']:self.socket('bay_'+bay['id'].replace('-','_'),bay['center'],half_size=bay['halfSize'])

    def build(self):
        e=self.earth;core=self.sections['hull'];w,h,d=core['halfSize'];cy=core['center'][1]
        self.section_loft('hull',(.92,1,1,.92),'armor' if e else 'dark')
        self.section_loft('bow-1',(.8,1,1,1))
        self.section_loft('bow-2',(.78,1,1,1) if e else (.18,.45,1,1))
        # Bow sensor face and attached armor belt give the gun hull a chisel silhouette.
        nose=self.sections['bow-2'];nx,ny,nz=nose['center'];nw,nh,nd=nose['halfSize']
        for side in (-1,1):
            nose_scale=.78 if e else .18
            self.box('Forward aperture',(side*nw*nose_scale*.35,ny,nz-nd+.08),(nw*nose_scale*.45,nh*.35,.4),'glass',.1)
            for z in (-100,-65,-20,20,65,106):
                if e:
                    self.box('Citadel side armor',(side*(w-.1),0,z),(1.2,25,27),'plate',.4)
                    self.box('Deck armor',(side*w*.48,h-.5,z),(w*.66,1.4,26),'armor',.3)
                    self.box('Deck access well',(side*w*.45,h+.25,z+3),(5,.6,8),'edge',.1)
                    self.box('Access cover',(side*w*.45,h+.6,z+3),(4,.25,6),'plate',.1)
                else:
                    self.box('Pressure compartment',(side*(w-1.6),0,z*.83),(2.7,22,23),'armor',.4)
                    self.rod('Truss diagonal',(side*(w+.1),-10,z*.83-12),(side*(w+.1),10,z*.83+12),.75,'edge')
                    self.rod('Truss chord',(side*(w+.1),10,z*.83-12),(side*(w+.1),10,z*.83+12),.75,'edge')
            for z in (-101,13,99):self.box('Structural collar',(side*(w-1),0,z if e else z*.83),(2.5,h*1.3,4),'plate',.2)
            for z in ((-135,-151) if e else (-117,-128)):
                b=self.sections['bow-1'];by=b['center'][1];bh=b['halfSize'][1]
                self.box('Bow dorsal armor seam',(side*(12 if e else 7),by+bh-.5,z),(10 if e else 7,1.3,9),'plate',.25)
            self.box('Team identification panel',(side*(12 if e else 7),by+bh+.25,-135 if e else -117),(4,.3,6),'iff',.04)
        for name in self.sections:
            if name.startswith('gun-shoulder'):self.section_loft(name,(.75,1,1,.7),'plate')
            elif name.startswith('pd-deck'):
                self.section_loft(name,(.7,1,1,.75),'plate')
                s=self.sections[name];x,y,z=s['center'];sw,sh,sd=s['halfSize']
                self.box('Defensive deck inset',(x,y+sh,z),(sw*1.6,.35,sd*1.6),'armor',.2)
                if 'aft' in name:
                    side=1 if x>0 else -1
                    self.box('Drone cradle recess',(x+side*(sw-.3),0,z),(.8,12,26),'dark',.1)
                    for zz in (-6,6):
                        self.box('Defense drone in cradle',(x+side*(sw+.2),0,z+zz),(1.5,3,7),'edge',.4)
                        self.box('Drone cradle retaining rib',(x+side*(sw+.9),0,z+zz),(1,4,1),'plate',.1)
            elif name.startswith('missile-cassette'):
                self.section_loft(name,(.88,1,1,.88),'armor')
                s=self.sections[name];x,y,z=s['center'];sw,sh,sd=s['halfSize'];side=1 if x>0 else -1
                for zz in (-sd*.55,sd*.55):
                    self.box('Missile armored collar',(x,0,z+zz),(sw*2+.3,sh*2+.3,1.8),'plate',.25)
                # Four individually readable hatches per cassette, 8 cassettes = 32 rounds.
                for dy in (-5.5,5.5):
                    for dz in (-7,7):
                        self.box('Missile cell shutter',(x+side*(sw+.06),dy,z+dz),(.5,8,11),'edge',.25)
                        self.box('Missile shutter inset',(x+side*(sw+.35),dy,z+dz),(.16,6,8),'plate',.08)
            elif name.startswith('turret-base'):self.section_box(name,'armor',.5)
        # A large mast houses the real sensor damage volume rather than a decorative orphan antenna.
        bridge=self.sections['bridge'];x,y,z=bridge['center'];bw,bh,bd=bridge['halfSize']
        self.loft('Bridge lower trunk',[(z-bd,bw,bh*.58,y-bh*.42),(z+bd,bw,bh*.58,y-bh*.42)],'armor')
        if not e:
            self.box('Bridge upper trunk',(x,y+bh*.35,z),(bw*1.55,bh*1.3,bd*1.55),'armor',1)
        sensor=next(m for m in self.data['modules'] if m['id']=='sensor');sx,sy,sz=sensor['center'];sw,sh,sd=sensor['halfSize']
        self.box('Sensor armored core',(sx,sy,sz),(sw*2+.5,sh*2+.5,sd*2+.5),'plate',.7)
        if e:
            # A compact sensor tower with exposed arrays, not an empty full-height bridge box.
            self.box('Sensor roof',(sx,sy+sh+.35,sz),(sw*2+2,.8,sd*2+2),'armor',.2)
        else:
            for side in (-1,1):
                self.rod('Mast brace',(side*bw*.6,y+bh*.45,z),(side*sw,sy-sh,sz),.65,'edge')
        if 'sensor-head' in self.sections:
            head=self.sections['sensor-head'];self.box('Sensor mast support',(0,(y+bh+sy-sh)/2,z),(6,abs(sy-sh-y-bh)+2,7),'edge',.4)
        face=sh*.7
        for side in (-1,1):
            self.box('Sensor side aperture',(sx+side*(sw+.35),sy,sz),(.35,face,sd*1.5),'glass',.05)
            self.box('Sensor face aperture',(sx+side*sw*.5,sy,sz-sd-.3),(sw*.7,face,.3),'glass',.05)
        mast_top=sy+sh+.7
        for xx,height in ((0,14),(-3,8),(3,10)):
            self.rod('Attached antenna',(xx,mast_top-.5,z),(xx,mast_top+height,z),.22,'edge')
        for mount in self.data['railgun']['mounts']:self.gun(mount)
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            sign=1 if p[1]>=0 else -1
            # Foundations physically meet the skin even where the original sockets were raised.
            skin=11 if self.defense and abs(p[0])>30 else h
            height=max(1,abs(p[1])-skin+.5)
            self.box('PD foundation '+str(i),(p[0],sign*(skin+height/2-.25),p[2]),(5,height,5),'armor',.25)
            self.point_defense(p,2.25 if e else 1.8,i)
        if not self.missile:
            count=self.data['missiles']['rounds']//2
            for side in (-1,1):
                for n in range(count):
                    y=-5+10*(n%2);z=(-100 if e else -87)+4.5*(n//2)
                    self.box('Missile cell face',(side*(w+.65),y,z),(1,3.4,3.5),'dark',.25)
        launch=self.data['missiles']['launchPoint'];lx,ly,lz=launch
        self.box('Missile transfer trunk',(lx,(h+ly)/2,lz),(5,abs(ly-h)+2,8),'armor',.5)
        self.tube('Missile active launch aperture',launch,1.6,2.2,'edge',axis=(0,1,0),wall=.2)
        self.rcs_clusters(-106 if e else -90,110 if e else 92,(w-.7,h*.7),(w-.7,h*.7),1.7,12)
        self.modules_and_markers()
        rig_turrets(self.coll,self.data);rig_point_defense(self.coll,self.data)

    def save(self):
        stats=export_collection(self.coll,self.root/f'assets/ships/{self.key}.glb')
        self.lighting();self.scene.cycles.samples=20
        self.scene.render.resolution_x=1280;self.scene.render.resolution_y=900
        self.scene['stage']='Role silhouette and functional equipment blockout; surface finish pending'
        self.scene['geometry_source']=f'data/model_layouts/{self.key}.json'
        self.scene['reference']=getattr(self,'reference',f'art/reference/20261017_t3_cruisers/{self.key}.png')
        self.scene['game_axes']='Forward -Z, up +Y, right +X; meters'
        light=bpy.data.lights.new('Underside fill','AREA');light.energy=500000;light.size=150
        obj=bpy.data.objects.new('Underside fill',light);self.scene.collection.objects.link(obj)
        obj.location=g((100,-220,-120));obj.rotation_euler=(-obj.location).to_track_quat('-Z','Y').to_euler()
        for obj in self.sockets:obj.hide_set(True)
        bpy.ops.object.select_all(action='DESELECT');self.meshes[0].select_set(True);bpy.context.view_layer.objects.active=self.meshes[0]
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(self.root/f'art/blender/{self.key}.blend'),compress=True)
        if self.render:
            camera=self.scene.camera;L=self.data['flight']['length'];target=g((0,4,-15))
            for name,pos in [('hero',(.9,.68,-1.05)),('underside',(.75,-.65,-.9)),('top',(0,1.5,0)),('rear',(.25,.25,1.4))]:
                camera.location=g(Vector(pos)*L);camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
                camera.data.ortho_scale=L*(1.8 if name=='top' else 1.23);self.scene.render.filepath=str(self.out/f'{self.key}_{name}.png')
                bpy.ops.render.render(write_still=True)
        return dict(id=self.key,source=f'art/blender/{self.key}.blend',model=f'assets/ships/{self.key}.glb',modules=len(self.data['modules']),**stats,**self.fx)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--render',action='store_true');parser.add_argument('--ship',choices=KEYS)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);root=Path(args.root)
    manifest_path=root/'assets/ships/manifest.json';manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
    for key in KEYS:
        if args.ship and args.ship!=key:continue
        bpy.ops.wm.read_factory_settings(use_empty=True);initial=bpy.context.scene
        ship=Cruiser(key,root,args.render);bpy.data.scenes.remove(initial);ship.build();entry=ship.save()
        manifest['ships']=[s for s in manifest['ships'] if s['id']!=key]+[entry]
        manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
        print('BUILT',key,entry['triangles'],'triangles',flush=True)
