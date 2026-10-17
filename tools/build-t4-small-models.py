"""Build only four new T4 sources. After manual edits use export-ship-models.py.
Blender --background --factory-startup --python-exit-code 1 --python <this> -- --root REPO --render
"""
import json,sys
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--root')+1]);sys.path.insert(0,str(ROOT/'tools'))
from branch_model_layout import resolve
from fleet_model_primitives import Ship,g
from fleet_model_export import export_collection,rig_point_defense
KEYS=('earth_recon_t4','interceptor_t4','mars_assault_t4','mars_interceptor_t4')
OUT=ROOT/'docs/validation/t4_small_models_20261017';OUT.mkdir(parents=True,exist_ok=True)

class SmallT4(Ship):
    def __init__(self,key):
        self.earth=not key.startswith('mars');super().__init__(ROOT,'interceptor' if self.earth else 'mars_interceptor')
        self.key=key;self.data=resolve(ROOT,key);self.layout=json.loads((ROOT/f'data/model_layouts/{key}.json').read_text(encoding='utf-8'))
        self.form=self.layout['blockout'];self.sections={s['id']:s for s in self.data['hullSections']}
        self.scene.name=key;self.coll.name=key+' | editable parts';self.guides.name=key+' | module hit volumes (guides)'
        for role,rgb in {'armor':(.22,.26,.29) if self.earth else (.12,.145,.16),'plate':(.32,.37,.40) if self.earth else (.38,.13,.065)}.items():
            mat=self.m[role];mat.diffuse_color=(*rgb,1)
            next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=(*rgb,1)
    def prism(self,name,points,thickness,mat='armor'):
        n=len(points);vs=[(x,y+t*thickness/2,z) for t in (-1,1) for x,y,z in points]
        fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        return self.mesh(name,vs,fs,mat)
    def section(self,id,mat='armor'):
        s=self.sections[id];return self.box(id,s['center'],[2*v for v in s['halfSize']],mat,.09)
    def build(self):
        f=self.form;e=self.earth;role=f['role'];recon=role=='recon';assault=role=='assault'
        self.loft('Main pressure hull',f['core']);self.loft('Sensor prow' if recon else 'Tapered prow',f['nose'])
        cw=1.05 if recon else .85 if e else .65;cz=-7.2 if e else -6.3
        self.loft('Cockpit frame',[(cz-2,.35,.13,1),(cz,cw,.55,1.5 if e else 1.2),(cz+2.3,cw,.45,1.5 if e else 1.2),(cz+2.8,cw*.8,.1,1.3 if e else 1)],'edge')
        self.loft('Cockpit glazing',[(cz-1.7,.22,.08,1.2 if e else 1),(cz,cw*.84,.39,1.7 if e else 1.4),(cz+2,cw*.84,.3,1.7 if e else 1.4)],'glass')
        self.rod('Canopy spine',(0,1.35 if e else 1.1,cz-1.6),(0,2.05 if e else 1.75,cz+2),.05,'edge')
        sensor=next(m for m in self.data['modules'] if m['id']=='sensor')
        self.box('Sensor dorsal window',(0,1.09 if e else .94,sensor['center'][2]),(.75 if recon else .42,.08,.8),'glass',.02)
        mx,my,mz=self.data['railgun']['muzzle'];gun=next(m for m in self.data['modules'] if m['id']=='gun');gx,gy,gz=gun['center']
        self.box('Fixed gun breech',(gx,gy,gz),(.8,.65,2*gun['halfSize'][2]),'dark',.06)
        for side in (-1,1):self.box('Fixed accelerator rail',(side*.27,my,(mz+gz)/2),(.21,.3,gz-mz),'edge',.025)
        self.tube('Fixed muzzle',(mx,my,mz+.12),.24,.24,'plate',wall=.22)
        ex,_,ez=f['engine'];pw,ph,pd=f['engineHalf'];cant,top=f['fin']
        for side,suffix,index in [(-1,'port',0),(1,'starboard',1)]:
            for j,points in enumerate(f['wings']):
                self.prism(f'Attached wing {suffix} {j}',[(side*x,0,z) for x,z in points],f['wingThickness'],'plate' if e else 'armor')
                # Large spars emphasize load paths, without surface detailing.
                a,b=points[0],points[1]
                self.rod('Wing leading spar',(side*a[0],f['wingThickness']/2,a[1]),(side*b[0],f['wingThickness']/2,b[1]),.09,'edge')
            pod=self.loft('Engine nacelle '+suffix,[(ez-pd,pw*.7,ph*.7,0),(ez-pd+.7,pw,ph,0),(ez+pd-.7,pw,ph,0),(ez+pd-.25,pw*.82,ph*.82,0)])
            pod.location.x+=side*ex
            self.box('Nacelle armor band '+suffix,(side*ex,0,ez-1.1),(pw*2+.04,ph*2+.04,.6),'plate',.08)
            self.box('IFF band '+suffix,(side*ex,ph+.04,ez-1.1),(pw*1.4,.04,.4),'iff',.005)
            nozzle=ez+pd+.08;radius=pw*.84
            self.tube('Engine rim '+str(index),(side*ex,0,nozzle),radius,.85,'edge',wall=.2)
            self.cylinder('Engine throat '+str(index),(side*ex,0,nozzle-.3),radius*.78,.06,'engine')
            position=[side*ex,0,nozzle+.43]
            self.fx['engines'].append(dict(position=position,radius=radius*.78,plume=8 if e else 10,index=index));self.socket('fx_engine_'+str(index),position)
            fin=[(side*ex,ph*.65,ez-1.5),(side*(ex+cant),top,ez+.2),(side*(ex+cant),top,ez+1.7),(side*ex,ph*.65,ez+2.4)]
            vs=[(x+side*t,y,z) for t in (-.12,.12) for x,y,z in fin]
            self.mesh('Attached fin '+suffix,vs,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'plate')
            cooling=next(m for m in self.data['modules'] if m['id']=='cooling-'+suffix)
            self.box('Cooling cassette '+suffix,(side*ex,ph-.08,cooling['center'][2]),(pw*1.4,.35,1.7),'radiator',.04)
            for dz in (-.5,0,.5):self.box('Cooling manifold',(side*ex,ph+.11,cooling['center'][2]+dz),(pw*1.3,.08,.1),'edge',.01)
            lx,ly,lz=self.data['missiles']['launchPoint'];lx=side*abs(lx)
            self.box('Missile rack support '+suffix,(lx-side*.24,ly,lz+1.2),(.65,.8,2.5),'dark',.05)
            self.cylinder('Missile cassette '+suffix,(lx,ly,lz+1.25),.38,2.5,'plate')
            self.tube('Missile mouth '+suffix,(lx,ly,lz),.38,.2,'edge',wall=.2)
            # Utility bay remains inside the pressure hull; its accessible cover is on the skin.
            hullw=max(r[1] for r in f['core'])
            self.box('Utility access '+suffix,(side*hullw,.2,-1),(.12,.7,1.1),'plate',.02)
            if recon:
                self.section('sensor-ear-'+suffix,'plate')
                ear=self.sections['sensor-ear-'+suffix];ec=ear['center'];eh=ear['halfSize']
                self.box('Lateral sensor aperture',(ec[0],ec[1],ec[2]-eh[2]-.02),(eh[0]*1.4,eh[1]*1.2,.06),'glass',.015)
                self.box('Prow side sensor',(side*1.9,.1,-10.7),(.06,.65,1.25),'glass',.02)
        probes=self.data.get('reconProbes')
        if recon:self.section('probe-carrier','edge')
        if probes:
            for i,(x,y,z) in enumerate(probes['launchPoints']):
                if recon:
                    self.cylinder('Probe cartridge '+str(i),(x,y,z+.8),.27,1.6,'plate')
                    self.tube('Probe mouth '+str(i),(x,y,z),.28,.16,'edge',wall=.2)
                    self.cylinder('Probe lens '+str(i),(x,y,z-.03),.2,.06,'glass')
                else:
                    side=1 if x>0 else -1
                    self.box('Probe mount '+str(i),(x-side*.3,y,z),(.65,.5,1),'edge',.04)
                    self.cylinder('Probe cartridge '+str(i),(x-side*.25,y,z),.23,.65,'plate',axis=(side,0,0))
                    self.cylinder('Probe lens '+str(i),(x+side*.08,y,z),.20,.05,'glass',axis=(side,0,0))
                self.socket('socket_probe_'+str(i),[x,y,z])
        keel=self.sections['am-keel'];x,y,z=keel['center'];w,h,d=keel['halfSize']
        self.loft('Containment keel',[(z-d,w*.8,h,y),(z-d+.3,w,h,y),(z+d,w,h,y)],'armor')
        self.box('AM jettison hatch',(0,y-h-.03,z),(w*1.7,.1,d*1.7),'plate',.03)
        # Storage indication only; there is one actual launch mouth and containment module.
        for i in range(self.data['antimatter']['rounds']):
            self.box('Containment segment',(0,y-h-.09,z-d*.65+(i+.5)*1.3*d/self.data['antimatter']['rounds']),(w*1.45,.05,.1),'edge',.01)
        self.socket('socket_am_jettison',[0,y-h-.1,z]);self.socket('fx_am_containment',[0,y-h-.1,z])
        self.tube('AM launch mouth',self.data['antimatter']['flight']['launchPoint'],.32,.4,'edge',wall=.2)
        self.socket('socket_antimatter_launch',self.data['antimatter']['flight']['launchPoint'])
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            sign=1 if p[1]>0 else -1
            self.box('PD foundation '+str(i),(p[0],p[1]-sign*.22,p[2]),(1,.75,1.3),'armor',.05)
            self.point_defense(p,.5 if e else .43,i)
        h=max(r[2] for r in f['core'])
        shield=next(m for m in self.data['modules'] if m['id']=='shield');shield_z=shield['center'][2]
        self.box('Shield projector',(0,h-.02,shield_z),(.65,.25,.9),'edge',.04)
        self.box('Shield lens',(0,h+.12,shield_z),(.4,.04,.55),'shield',.005)
        # RCS ports follow the fully widened nose ring, and the pod skins.
        bow,bw,bh,_=f['nose'][1]
        for side in (-1,1):
            self.rcs((side*bw,0,bow),(side,0,0),.28,4)
            for v in (-1,1):self.rcs((side*bw*.6,v*bh,bow),(0,v,0),.28,4)
            self.rcs((side*(bw+.13),0,bow),(0,0,-1),.28,4)
            self.rcs((side*(ex+pw),0,ez),(side,0,0),.3,4)
            for v in (-1,1):self.rcs((side*ex,v*ph,ez),(0,v,0),.3,4)
        self.modules();rig_point_defense(self.coll,self.data)
    def save(self):
        stats=export_collection(self.coll,ROOT/f'assets/ships/{self.key}.glb')
        self.lighting();self.scene['geometry_source']=f'data/model_layouts/{self.key}.json'
        self.scene['reference']=getattr(self,'reference','art/reference/20261017_t4_small/t4-small.png')
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
                camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=L*(1.65 if name=='top' else 1.26)
                self.scene.render.filepath=str(OUT/f'{self.key}_{name}.png');bpy.ops.render.render(write_still=True)
        return dict(id=self.key,source=f'art/blender/{self.key}.blend',model=f'assets/ships/{self.key}.glb',modules=len(self.data['modules']),**stats,**self.fx)

if __name__=='__main__':
    path=ROOT/'assets/ships/manifest.json';manifest=json.loads(path.read_text(encoding='utf-8'))
    keys=KEYS
    if '--ship' in sys.argv:keys=(sys.argv[sys.argv.index('--ship')+1],);assert keys[0] in KEYS
    for key in keys:
        bpy.ops.wm.read_factory_settings(use_empty=True);initial=bpy.context.scene;ship=SmallT4(key);bpy.data.scenes.remove(initial)
        ship.build();entry=ship.save();manifest['ships']=[s for s in manifest['ships'] if s['id']!=key]+[entry]
        path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8');print('BUILT',key,entry['triangles'],'triangles',flush=True)
