"""Author the two approved T3 blockouts; never open or overwrite the original eight models.

blender --background --factory-startup --python-exit-code 1 --python tools/build-small-branch-models.py -- --root <repo> --render
Geometry/FX use data/model_layouts; regenerate tier patches first. Rebuilding overwrites
ONLY these two generated sources. Subsequent manual edits should use export-ship-models.py.
"""
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path(sys.argv[sys.argv.index('--root') + 1])
sys.path.insert(0, str(ROOT / 'tools'))
from branch_model_layout import resolve
from fleet_model_export import export_collection, rig_point_defense
import fleet_model_primitives as primitives
g = primitives.g
OUT = ROOT / 'docs/validation/blockout_small_branches_20261015'
OUT.mkdir(parents=True, exist_ok=True)


class Blockout(primitives.Ship):
    def __init__(self, key):
        self.earth = key.startswith('earth')
        super().__init__(ROOT, 'interceptor' if self.earth else 'mars_interceptor')
        self.key = key
        self.data = resolve(ROOT, key)
        self.layout = json.loads((ROOT / f'data/model_layouts/{key}.json').read_text(encoding='utf-8'))
        self.scene.name = key
        self.coll.name = f'{key} | editable parts'
        self.guides.name = f'{key} | module hit volumes (guides)'
        # Broad material zones for this geometry pass; final panel finishing comes later.
        for role, mat in self.m.items():
            if role not in ('iff', 'engine', 'shield'):
                mat.name = 'Blockout - ' + role
        self.color('armor', (.21,.25,.29) if self.earth else (.12,.14,.16))
        self.color('plate', (.34,.39,.43) if self.earth else (.32,.105,.055))
        self.color('glass', (.025,.10,.14))
        self.color('iff', (.10,.44,.65) if self.earth else (.75,.26,.05))
        self.sections = {s['id']: s for s in self.data['hullSections']}

    def color(self, key, rgb):
        mat = self.m[key]
        mat.diffuse_color = (*rgb, 1)
        next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (*rgb, 1)

    def section_box(self, key, mat='armor', bevel=.12):
        s = self.sections[key]
        return self.box(key, s['center'], [a*2 for a in s['halfSize']], mat, bevel)

    def fin(self, side, ventral=False):
        x = 4 if self.earth else 4.1
        z = 11 if self.earth else 10
        y = -1.2 if ventral else 1.3
        tip = -2.7 if ventral else 3.9
        # Both fins use the same mirrored vertices, including their top cap.
        points = [(side*x,y,z-2),(side*(x+.7),tip,z+.2),
                  (side*(x+.7),tip,z+1.7),(side*x,y,z+2)]
        vertices = [(a+side*t,b,c) for t in (-.12,.12) for a,b,c in points]
        faces = [(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
        self.mesh(f'Attached {"ventral" if ventral else "radiator"} fin {side}', vertices, faces,
                  'plate' if not self.earth else 'radiator')
        for t in (.28,.58,.86):
            self.rod('Fin cooling spar', (side*x,y,z-2+4*t),
                     (side*(x+.7),tip,z+.2+1.5*t), .07, 'edge')

    def build(self):
        e = self.earth
        # Shapes are contained by the corresponding damage section. The blunt sensor prow
        # and tapered assault prow remain distinguishable in flat silhouette.
        self.loft('Main pressure hull', [(-8 if e else -6,1.7 if e else 1.5,1.2,0),
            (-4,2.5,1.55 if e else 1.7,0),(7,2.5,1.55 if e else 1.7,0),
            (10 if e else 8,1.5,1.2,0)], 'armor')
        self.loft('Sensor prow' if e else 'Assault wedge',
            [(-15 if e else -12.8,1.05 if e else .45,.8 if e else .5,0),
             (-12 if e else -10,1.25 if e else 1.0,1.2,0),
             (-7 if e else -5.2,1.25 if e else 1.0,1.2,0)], 'armor')
        # Flat sensor face, not an external dish.
        for side in (-1,1):
            self.box('Sensor aperture', (side*(.57 if e else .25),.18,-15.02 if e else -12.82),
                     (.48 if e else .22,.42,.07),'glass',.025)
            self.box('Sensor status', (side*(.57 if e else .25),-.23,-15.04 if e else -12.84),
                     (.2,.09,.07),'shield',.01)
        self.loft('Cockpit pressure frame',[(-10.7 if e else -10,.5,.15,1),
            (-7.3 if e else -7.1,.9,.5,1.6),(-4.8,.85,.48,1.65),(-4.3,.8,.1,1.5)],'edge')
        self.loft('Cockpit glazing',[(-10.35 if e else -9.65,.38,.1,1.2),
            (-7.3 if e else -7.1,.78,.4,1.87),(-4.85,.72,.35,1.95)],'glass')
        self.rod('Canopy frame', (0,1.35,-10.3 if e else -9.6),(0,2.3,-4.85),.065,'edge')
        for side in (-1,1):
            suffix = 'port' if side < 0 else 'starboard'
            self.section_box('pylon-'+suffix,'edge')
            pod = self.sections['engine-'+suffix]
            x,y,z = pod['center']; w,h,d = pod['halfSize']
            self.loft('Engine pod '+suffix,[(z-d,w*.72,h*.72,0),(z-d+1,w,h,0),
                (z+d-.5,w,h,0),(z+d,w*.85,h*.85,0)], 'armor')
            # Loft utility is centered on X=0; translate complete pod to its module X.
            self.meshes[-1].location.x += x
            for zz in (z-d+1.2,z+d-1.4):
                self.box('Engine armored band',(x,0,zz),(w*2+.1,h*2+.1,.45),'plate',.08)
            nozzle = z+d+.22
            self.tube('Main engine nozzle', (x,0,nozzle-.12),1 if e else 1.18,.65,'edge',wall=.17)
            self.cylinder('Main engine throat',(x,0,nozzle-.35),.8 if e else .96,.07,'engine')
            index = 0 if side < 0 else 1
            self.fx['engines'].append(dict(position=[x,0,nozzle+.205],radius=.87 if e else 1.04,plume=8 if e else 10,index=index))
            self.socket('fx_engine_'+str(index),[x,0,nozzle+.205])
            self.fin(side)
            if not e: self.fin(side, True)
            # Store rack backs and end frames attach to the shoulder, leaving cartridges readable.
            rack_z = -2.2 if e else -3.2
            self.box('Stores backing '+suffix,(side*2.55,0,rack_z),(.5,1.9,7.8 if e else 3.3),'dark',.08)
            missile_z = -5.2 if e else -3.8
            for row in (-1,1):
                self.cylinder('Missile cartridge',(side*2.9,row*.45,missile_z),.27,2.4,'edge')
                self.cylinder('Missile nose',(side*2.9,row*.45,missile_z-1.22),.27,.2,'plate')
                if e:
                    self.cylinder('Recon probe cartridge',(side*2.85,row*.45,0),.30,2.2,'white',axis=(side,0,0))
                    self.cylinder('Probe optical head',(side*3.91,row*.45,0),.29,.08,'glass',axis=(side,0,0))
            for zz in ([ -6.2,-3.7,1.7 ] if e else [-4.9,-2.5]):
                self.box('Store retaining strap',(side*2.95,0,zz),(1.05,2,.18),'plate',.04)
            self.box('Equipment socket cover',(side*2.5,.2,-2.5 if e else -2),(.24,.85,.9),'plate',.06)
            self.box('Shoulder armor',(side*1.9,1.25,-.5),(1.05,.6,4),'plate',.12)
            self.box('IFF stripe',(side*1.92,1.56,-.5),(.55,.025,1.1),'iff',.005)
            # Coolant cassette is centered over the actual cooling damage volume.
            cooling = next(m for m in self.data['modules'] if m['id']=='cooling-'+suffix)['center']
            self.box('Coolant housing', (cooling[0],1.2,cooling[2]),(.75,.6,1.8),'dark',.05)
            for j in range(5):
                self.box('Coolant grille',(cooling[0],1.53,cooling[2]-.6+j*.3),(.65,.08,.12),'copper',.01)
        # One physical bore between two accelerator rails: existing single fixed main gun.
        mx,my,mz = self.data['railgun']['muzzle']
        self.box('Fixed gun breech',(0,my,mz+3.3),(1.0,.85,2.8),'edge',.12)
        for side in (-1,1):
            self.box('Main gun accelerator rail',(side*.27,my,mz+2),(.23,.30,4),'edge',.035)
        self.tube('Fixed main gun muzzle',(mx,my,mz+.12),.24,.24,'plate',wall=.06)
        # Armored containment keel; the lower hatch has a clear downward jettison path.
        keel = self.sections['am-keel']; x,y,z = keel['center']; w,h,d = keel['halfSize']
        for side in (-1,1):
            self.box('AM armored sidewall',(side*(w-.15),y,z),(.3,h*2,d*2),'plate',.08)
        self.box('AM aft bulkhead',(0,y,z+d-.12),(w*2,h*2,.24),'edge',.08)
        self.box('AM forward bulkhead',(0,y,z-d+.02),(w*2,h*2,.3),'edge',.08)
        self.box('AM jettison hatch',(0,y-h+.08,z),(w*2-.25,.16,d*2-.22),'armor',.07)
        self.socket('socket_am_jettison',[0,y-h-.05,z])
        self.socket('fx_am_containment',[0,y-h-.04,z])
        self.socket('socket_antimatter_launch',self.data['antimatter']['flight']['launchPoint'])
        # Single forward launch aperture remains separate from the downward emergency hatch.
        self.tube('AM forward launch port',self.data['antimatter']['flight']['launchPoint'],.39,.22,'edge',wall=.07)
        module = next(m for m in self.data['modules'] if m['id']=='am-containment')
        cx,cy,cz = module['center']
        for dx,dy in ([(0,0)] if e else [(-.5,-.5),(.5,-.5),(-.5,.5),(.5,.5)]):
            self.cylinder('AM isolated cartridge',(cx+dx,cy+dy,cz),.36,2.8,'copper')
        self.box('AM storage cradle',(0,cy,cz+1.5),(w*2-.2,h*2-.2,.3),'dark',.04)
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            sign = -1 if p[1] < 0 else 1
            self.box('PD foundation '+str(i),(p[0],p[1]-sign*.2,p[2]),(1.2,.55,1.3),'armor',.05)
            self.point_defense(p,.55,i)
        self.box('Shield projector',(0,1.55,7 if e else 6),(.8,.3,1.1),'edge',.04)
        self.box('Shield lens',(0,1.72,7 if e else 6),(.5,.05,.65),'shield',.01)
        self.rcs_clusters(-10 if e else -9,10 if e else 9,(1.15,1.0),(5.3 if e else 5.55,1.4 if e else 1.6),.35,4)
        self.modules()
        for i,p in enumerate(self.layout['probeLaunchPoints']): self.socket('socket_probe_'+str(i),p)
        rig_point_defense(self.coll,self.data)

    def save(self):
        stats = export_collection(self.coll,ROOT/f'assets/ships/{self.key}.glb')
        self.lighting()
        self.scene['game_axes']='Forward -Z, up +Y, right +X; meters'
        self.scene['geometry_source']=f'data/model_layouts/{self.key}.json'
        self.scene['stage']='Functional blockout: exterior finishing intentionally pending'
        self.scene.render.resolution_x=1280; self.scene.render.resolution_y=900
        self.scene.cycles.samples=24
        # Soft fill also exposes the keel on underside review images.
        light=bpy.data.lights.new('Ventral fill','AREA');light.energy=3500;light.size=18
        obj=bpy.data.objects.new('Ventral fill',light);self.scene.collection.objects.link(obj)
        obj.location=g((10,-18,-8));obj.rotation_euler=(-obj.location).to_track_quat('-Z','Y').to_euler()
        for obj in self.sockets: obj.hide_set(True)
        bpy.ops.object.select_all(action='DESELECT')
        self.meshes[0].select_set(True);bpy.context.view_layer.objects.active=self.meshes[0]
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/f'art/blender/{self.key}.blend'),compress=True)
        if '--render' in sys.argv:
            camera=self.scene.camera; L=self.data['flight']['length']
            for name,pos in [('hero',(.9,.65,-1.03)),('underside',(.8,-.7,-.85)),('aft',(.8,.5,1.1)),('top',(0,1.4,0))]:
                camera.location=g(Vector(pos)*L)
                camera.rotation_euler=(g((0,0,-.6))-camera.location).to_track_quat('-Z','Y').to_euler()
                if name=='top': camera.rotation_euler=(0,0,math.pi/2)
                camera.data.ortho_scale=L*1.14
                self.scene.render.filepath=str(OUT/f'{self.key}_{name}.png');bpy.ops.render.render(write_still=True)
        return dict(id=self.key,source=f'art/blender/{self.key}.blend',model=f'assets/ships/{self.key}.glb',
                    modules=len(self.data['modules']),**stats,**self.fx)


manifest_path=ROOT/'assets/ships/manifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
for key in ('earth_recon_t3','mars_assault_t3'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    initial=bpy.context.scene
    ship=Blockout(key);bpy.data.scenes.remove(initial)
    ship.build()
    entry=ship.save()
    manifest['ships']=[s for s in manifest['ships'] if s['id']!=key]+[entry]
    print('BUILT',key,entry['triangles'],'triangles',flush=True)
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
