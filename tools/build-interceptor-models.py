"""Independent Falke/Herschel functional blockouts. No existing hull is rebuilt.
Blender --background --factory-startup --python-exit-code 1 --python <this> -- --root <repo> --render
After manual editing, use export-ship-models.py instead of rebuilding these sources.
"""
import json,sys,math
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from branch_model_layout import resolve
from fleet_model_primitives import Ship,g
from fleet_model_export import export_collection,rig_point_defense
OUT=ROOT/'docs/validation/interceptor_models_20261017'
OUT.mkdir(parents=True,exist_ok=True)

class Interceptor(Ship):
    def __init__(self,key):
        self.earth=key=='interceptor_t3'
        super().__init__(ROOT,'interceptor' if self.earth else 'mars_interceptor')
        self.key=key;self.data=resolve(ROOT,key)
        self.scene.name=key;self.coll.name=key+' | editable parts';self.guides.name=key+' | module hit volumes (guides)'
        self.sections={s['id']:s for s in self.data['hullSections']}
        for role,rgb in {'armor':(.22,.26,.29) if self.earth else (.12,.145,.16),
                         'plate':(.32,.37,.40) if self.earth else (.38,.13,.065)}.items():
            m=self.m[role];m.diffuse_color=(*rgb,1)
            next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=(*rgb,1)
    def prism(self,name,points,thickness,mat):
        n=len(points);vertices=[(x,y+t*thickness/2,z) for t in (-1,1) for x,y,z in points]
        faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        return self.mesh(name,vertices,faces,mat)
    def build(self):
        e=self.earth;w=2.4 if e else 1.65;h=1.6 if e else 1.3
        self.loft('Pressure hull',[(-8 if e else -6.8,w*.6,h*.7,0),(-3,w,h,0),(6,w,h,0),(10 if e else 8.8,w*.75,h*.85,0)])
        self.loft('Armored prow' if e else 'Needle prow',[(-14 if e else -13,.48 if e else .28,.65,0),(-11 if e else -10,1.35 if e else .9,.95 if e else .8,0),(-5,1.35 if e else .9,.95 if e else .8,0)])
        self.loft('Canopy frame',[(-8.2 if e else -7.7,.35,.15,1),(-6.4,1 if e else .63,.55,1.4),(-3.4,1 if e else .63,.5,1.4),(-2.8,.8 if e else .5,.1,1.3)],'edge')
        self.loft('Cockpit glazing',[(-7.9 if e else -7.4,.25,.08,1.2),(-6.2,.82 if e else .5,.4,1.65),(-3.6,.82 if e else .5,.35,1.65)],'glass')
        self.rod('Canopy central frame',(0,1.35,-7.8 if e else -7.3),(0,2.05,-3.6),.055,'edge')
        sensor=next(m for m in self.data['modules'] if m['id']=='sensor')
        self.box('Sensor aperture',(0,1.03 if e else .87,sensor['center'][2]),(.65 if e else .45,.07,.55),'glass',.02)
        # Single bore, twin accelerator rails. Ammo/shot count stay unchanged.
        mx,my,mz=self.data['railgun']['muzzle'];gun=next(m for m in self.data['modules'] if m['id']=='gun')
        self.box('Fixed gun breech',gun['center'],(.85,.7,gun['halfSize'][2]*2),'dark',.06)
        length=gun['center'][2]-mz
        for side in (-1,1):self.box('Fixed accelerator rail',(side*.27,my,mz+length/2),(.22,.32,length),'edge',.03)
        self.tube('Fixed muzzle',(mx,my,mz+.12),.24,.24,'plate',wall=.22)
        for side,suffix,index in [(-1,'port',0),(1,'starboard',1)]:
            pod=self.sections['engine-'+suffix];x,y,z=pod['center'];pw,ph,pd=pod['halfSize']
            if e:
                wing=[(side*1.7,0,-4),(side*7.2,0,3),(side*7.2,0,5.5),(side*3.5,0,8),(side*1.7,0,6)]
            else:
                wing=[(side*1.2,0,-.2),(side*4.4,0,4.1),(side*4.4,0,6.2),(side*1.2,0,2.3)]
            self.prism('Attached wing '+suffix,wing,.8 if e else .65,'plate' if e else 'armor')
            if not e:self.rod('Pylon leading spar',(side*1.25,.22,0),(side*3.7,.22,3.5),.18,'plate')
            # Pod body stops before the open nozzle; no cap or band can hide its throat.
            podmesh=self.loft('Engine nacelle '+suffix,[(z-pd,pw*.7,ph*.7,0),(z-pd+.8,pw,ph,0),(z+pd-.7,pw,ph,0),(z+pd-.25,pw*.82,ph*.82,0)])
            podmesh.location.x+=x
            self.box('Nacelle armor band '+suffix,(x,0,z-1.2),(pw*2+.04,ph*2+.04,.65),'plate',.09)
            self.box('IFF band '+suffix,(x,ph+.04,z-1.2),(pw*1.4,.045,.4),'iff',.005)
            nozzle=z+pd+.08;radius=pw*.84
            self.tube('Engine rim '+str(index),(x,0,nozzle),radius,.85,'edge',wall=.2)
            self.cylinder('Engine throat '+str(index),(x,0,nozzle-.3),radius*.78,.06,'engine')
            self.fx['engines'].append(dict(position=[x,0,nozzle+.43],radius=radius*.78,plume=8 if e else 10,index=index))
            self.socket('fx_engine_'+str(index),[x,0,nozzle+.43])
            # Mirror the same four vertices; tops attach to the nacelle on both sides.
            fy=ph*.65;top=3.3 if e else 3.1
            fin=[(x,fy,z-1.5),(x+side*.6,top,z+.2),(x+side*.6,top,z+1.7),(x,fy,z+2.4)]
            vertices=[(a+side*t,b,c) for t in (-.12,.12) for a,b,c in fin]
            self.mesh('Attached fin '+suffix,vertices,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'plate')
            cooling=next(m for m in self.data['modules'] if m['id']=='cooling-'+suffix)['center']
            self.box('Cooling cassette '+suffix,(x,ph-.08,cooling[2]),(pw*1.4,.35,1.8),'radiator',.04)
            for j in (-.5,0,.5):self.box('Cooling spar',(x,ph+.11,cooling[2]+j),(pw*1.3,.08,.12),'edge',.01)
            rackx=side*abs(self.data['missiles']['launchPoint'][0]);rackz=self.data['missiles']['launchPoint'][2]+1.25
            self.box('Missile rack back '+suffix,(side*(w-.05),-.65,rackz),(.55,1,2.8),'dark',.06)
            self.cylinder('Missile cassette '+suffix,(rackx,-.65,rackz),.38,2.5,'plate')
            self.tube('Missile exit '+suffix,(rackx,-.65,rackz-1.25),.38,.2,'edge',wall=.2)
            px,py,pz=self.data['reconProbes']['launchPoints'][index]
            self.cylinder('Probe cartridge '+suffix,(px-side*.35,py,pz),.23,.8,'edge',axis=(side,0,0))
            self.cylinder('Probe head '+suffix,(px+side*.05,py,pz),.20,.05,'glass',axis=(side,0,0))
            self.socket('socket_probe_'+str(index),[px,py,pz])
            self.box('Equipment hatch '+suffix,(side*w,.35,-.8),(.12,.55,.8),'plate',.025)
        keel=self.sections['am-keel'];x,y,z=keel['center'];kw,kh,kd=keel['halfSize']
        self.loft('Containment keel',[(z-kd,kw*.8,kh,y),(z-kd+.3,kw,kh,y),(z+kd,kw,kh,y)],'armor')
        self.box('AM jettison hatch',(0,y-kh-.02,z),(kw*1.7,.09,kd*1.65),'plate',.03)
        self.socket('socket_am_jettison',[0,y-kh-.08,z]);self.socket('fx_am_containment',[0,y-kh-.08,z])
        self.tube('AM launch port',self.data['antimatter']['flight']['launchPoint'],.32,.3,'edge',wall=.2)
        self.socket('socket_antimatter_launch',self.data['antimatter']['flight']['launchPoint'])
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            sign=1 if p[1]>0 else -1
            self.box('PD foundation '+str(i),(p[0],p[1]-sign*.18,p[2]),(1,.6,1.2),'armor',.05)
            self.point_defense(p,.5 if e else .43,i)
        self.box('Shield projector',(0,h,4.3),(.65,.25,.9),'edge',.04)
        self.box('Shield lens',(0,h+.14,4.3),(.4,.04,.55),'shield',.005)
        # Fourteen RCS ports seated on the actual prow and engine skin.
        bow=-10 if e else -9;bw=1.32 if e else .89;bh=.9 if e else .78
        for side in (-1,1):
            self.rcs((side*bw,0,bow),(side,0,0),.32,4)
            for v in (-1,1):self.rcs((side*bw*.6,v*bh,bow),(0,v,0),.32,4)
            self.rcs((side*(bw+.16),0,bow),(0,0,-1),.32,4)
            pod=self.sections['engine-'+('port' if side<0 else 'starboard')];x,_,z=pod['center'];pw,ph,_=pod['halfSize']
            self.rcs((x+side*pw,0,z),(side,0,0),.32,4)
            for v in (-1,1):self.rcs((x,v*ph,z),(0,v,0),.32,4)
        self.modules();rig_point_defense(self.coll,self.data)
    def save(self):
        stats=export_collection(self.coll,ROOT/f'assets/ships/{self.key}.glb')
        self.lighting();self.scene['geometry_source']=f'data/model_layouts/{self.key}.json'
        self.scene['stage']='Primary forms and functional placements; surface finishing deferred'
        self.scene.render.resolution_x=1280;self.scene.render.resolution_y=900;self.scene.cycles.samples=20
        L=self.data['flight']['length'];light=bpy.data.lights.new('Ventral fill','AREA');light.energy=4*L*L;light.size=L*.6
        o=bpy.data.objects.new('Ventral fill',light);self.scene.collection.objects.link(o);o.location=g((L*.3,-L*.6,-L*.3));o.rotation_euler=(-o.location).to_track_quat('-Z','Y').to_euler()
        for o in self.sockets:o.hide_set(True)
        bpy.ops.object.select_all(action='DESELECT');self.meshes[0].select_set(True);bpy.context.view_layer.objects.active=self.meshes[0]
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/f'art/blender/{self.key}.blend'),compress=True)
        if '--render' in sys.argv:
            for name,pos in [('hero',(.85,.7,-1.05)),('underside',(.8,-.7,-.9)),('rear',(.6,.5,1.1)),('top',(0,1.5,0))]:
                camera=self.scene.camera;camera.location=g(Vector(pos)*L);target=g((0,0,-1))
                camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
                camera.data.ortho_scale=L*(1.65 if name=='top' else 1.26)
                self.scene.render.filepath=str(OUT/f'{self.key}_{name}.png');bpy.ops.render.render(write_still=True)
        return dict(id=self.key,source=f'art/blender/{self.key}.blend',model=f'assets/ships/{self.key}.glb',modules=len(self.data['modules']),**stats,**self.fx)

path=ROOT/'assets/ships/manifest.json';manifest=json.loads(path.read_text(encoding='utf-8'))
keys=('interceptor_t3','mars_interceptor_t3')
if '--ship' in sys.argv:keys=(sys.argv[sys.argv.index('--ship')+1],);assert keys[0] in ('interceptor_t3','mars_interceptor_t3')
for key in keys:
    bpy.ops.wm.read_factory_settings(use_empty=True);initial=bpy.context.scene;ship=Interceptor(key);bpy.data.scenes.remove(initial)
    ship.build();entry=ship.save();manifest['ships']=[s for s in manifest['ships'] if s['id']!=key]+[entry]
    print('BUILT',key,entry['triangles'],'triangles',flush=True)
path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
