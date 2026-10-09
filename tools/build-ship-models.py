"""Blender 5.x: authored hard-surface fleet, using existing Godot module coordinates.

blender --background --python tools/build-ship-models.py -- --root <repository>
Editable .blend sources retain individual parts. GLBs batch static geometry;
module volumes and FX sockets remain named empties. No ship balance is changed.
"""
import argparse
import json
import math
from pathlib import Path
import sys
import bpy
import bmesh
from mathutils import Vector

args = argparse.ArgumentParser()
args.add_argument('--root', required=True)
args.add_argument('--render', action='store_true')
opt = args.parse_args(sys.argv[sys.argv.index('--') + 1:])
ROOT = Path(opt.root)
sys.path.insert(0, str(ROOT / 'tools'))
from fleet_model_export import rig_turrets, export_collection, fit_antimatter
ART = ROOT / 'art' / 'blender'
OUT = ROOT / 'assets' / 'ships'
SHOTS = ROOT / 'shots' / 'models'
for p in (ART, OUT, SHOTS):
    p.mkdir(parents=True, exist_ok=True)

def g(p):
    """Godot right/up/aft to Blender right/forward/up; glTF converts it back."""
    return Vector((p[0], -p[2], p[1]))

def material(name, color, metal=0.6, rough=0.45, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    if emission:
        bs.inputs['Emission Color'].default_value = (*color, 1)
        bs.inputs['Emission Strength'].default_value = emission
    return m

def make_materials():
    return {
        'armor': material('Armor - graphite titanium', (.075, .09, .11), .7, .48),
        'plate': material('Armor - raised ceramic', (.16, .18, .205), .52, .52),
        'edge': material('Edge - brushed alloy', (.075, .095, .12), .8, .33),
        'dark': material('Recess - carbon', (.025, .034, .042), .4, .6),
        'radiator': material('Thermal - graphite fins', (.046, .055, .064), .8, .4),
        'copper': material('Thermal - copper manifold', (.32, .17, .065), .8, .32),
        'iff': material('IFF', (.17, .33, .46), .55, .4),
        'glass': material('Canopy - smoked blue', (.02, .095, .14), .55, .16),
        'white': material('Markings - ivory', (.7, .73, .73), .2, .6),
        'amber': material('Service lighting', (.95, .37, .06), .1, .3, 1.5),
        'engine': material('EngineGlow', (.10, .47, 1), .1, .25, 2),
        'shield': material('ShieldGlow', (.08, .53, .8), .2, .3, .7),
    }

class Ship:
    def __init__(self, key):
        self.key = key
        self.data = json.loads((ROOT / 'data' / 'ships' / f'{key}.json').read_text(encoding='utf-8-sig'))
        self.scene = bpy.data.scenes.new(key.title())
        bpy.context.window.scene = self.scene
        self.coll = bpy.data.collections.new(f'{key} | editable parts')
        self.scene.collection.children.link(self.coll)
        self.guides = bpy.data.collections.new(f'{key} | module hit volumes (guides)')
        self.scene.collection.children.link(self.guides)
        self.guides.hide_render = True
        self.m = make_materials()
        self.meshes = []
        self.sockets = []
        self.fx = {'engines': [], 'rcs': []}
        self.n = 0

    def mesh(self, name, vertices, faces, mat='armor', bevel=0):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([g(v) for v in vertices], [], faces)
        mesh.update()
        bm = bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh); bm.free()
        obj = bpy.data.objects.new(name, mesh)
        self.coll.objects.link(obj)
        # Local part origins make manual rotation / scaling in Blender practical.
        center = Vector(tuple((min(v.co[a] for v in mesh.vertices) + max(v.co[a] for v in mesh.vertices)) * .5 for a in range(3)))
        for vertex in mesh.vertices:
            vertex.co -= center
        obj.location = center
        obj.data.materials.append(self.m[mat])
        if bevel:
            mod = obj.modifiers.new('Machined edge chamfer', 'BEVEL')
            mod.width = bevel; mod.segments = 1
            mod.affect = 'EDGES'
        self.meshes.append(obj)
        return obj

    def box(self, name, c, size, mat='armor', bevel=None):
        x,y,z = c; a,b,d = (s*.5 for s in size)
        vs = [(x+sx*a,y+sy*b,z+sz*d) for sz in (-1,1) for sy in (-1,1) for sx in (-1,1)]
        fs = [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
        return self.mesh(name, vs, fs, mat, min(size)*.1 if bevel is None else bevel)

    def loft(self, name, rings, mat='armor'):
        # Each ring is z, half-width, half-height, y-center; chamfered octagon.
        vs=[]; fs=[]
        for z,w,h,y in rings:
            vs += [(x,y+v,z) for x,v in [(-.72*w,-h),(.72*w,-h),(w,-.64*h),(w,.64*h),(.72*w,h),(-.72*w,h),(-w,.64*h),(-w,-.64*h)]]
        fs.append(tuple(reversed(range(8))))
        for r in range(len(rings)-1):
            for i in range(8): fs.append((r*8+i,r*8+(i+1)%8,(r+1)*8+(i+1)%8,(r+1)*8+i))
        fs.append(tuple(range(len(vs)-8,len(vs))))
        return self.mesh(name,vs,fs,mat)

    def cylinder(self, name, c, radius, length, mat='edge', axis=(0,0,1), r2=None, sides=12):
        n=Vector(axis).normalized(); helper=Vector((0,1,0)) if abs(n.y)<.9 else Vector((1,0,0))
        u=n.cross(helper).normalized(); v=n.cross(u).normalized(); c=Vector(c)
        vs=[]
        for sign,r in ((-1,radius),(1,radius if r2 is None else r2)):
            for i in range(sides):
                t=i*math.tau/sides
                vs.append(c+n*length*.5*sign+r*(math.cos(t)*u+math.sin(t)*v))
        fs=[tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
        fs += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
        return self.mesh(name,vs,fs,mat)

    def tube(self,name,c,r,length,mat='edge',axis=(0,0,1),wall=.16):
        n=Vector(axis).normalized(); helper=Vector((0,1,0)) if abs(n.y)<.9 else Vector((1,0,0))
        u=n.cross(helper).normalized(); v=n.cross(u).normalized(); c=Vector(c); N=16
        vs=[]; fs=[]
        for d,rad in ((-length/2,r),(length/2,r),(-length/2,r*(1-wall)),(length/2,r*(1-wall))):
            vs += [c+n*d+rad*(math.cos(i*math.tau/N)*u+math.sin(i*math.tau/N)*v) for i in range(N)]
        for i in range(N):
            j=(i+1)%N
            fs += [(i,j,N+j,N+i),(2*N+i,3*N+i,3*N+j,2*N+j),(i,2*N+i,2*N+j,j),(N+i,N+j,3*N+j,3*N+i)]
        return self.mesh(name,vs,fs,mat)

    def rod(self,name,a,b,r,mat='edge'):
        a=Vector(a); b=Vector(b)
        return self.cylinder(name,(a+b)/2,r,(b-a).length,mat,axis=b-a,sides=8)

    def label(self, text, pos, size, facing='top'):
        curve=bpy.data.curves.new('Stencil '+text,'FONT'); curve.body=text; curve.size=size
        curve.align_x='CENTER'; curve.extrude=0
        obj=bpy.data.objects.new('Stencil '+text,curve); self.coll.objects.link(obj)
        obj.location=g(pos)
        if facing=='port': obj.rotation_euler=(math.pi/2,0,-math.pi/2)
        elif facing=='starboard': obj.rotation_euler=(math.pi/2,0,math.pi/2)
        curve.materials.append(self.m['white'])
        bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active=obj
        bpy.ops.object.convert(target='MESH'); self.meshes.append(bpy.context.object)

    def socket(self,name,p,**props):
        obj=bpy.data.objects.new(name,None); self.coll.objects.link(obj); obj.location=g(p)
        obj.empty_display_type='ARROWS'; obj.empty_display_size=self.data['flight']['length']*.008
        for k,v in props.items(): obj[k]=v
        self.sockets.append(obj)

    def turret(self,c,s,tip=None,ventral=False,name='Rail battery'):
        x,y,z=c; sign=-1 if ventral else 1
        self.cylinder(name+' bearing',(x,y,z),s*1.15,s*.32,'dark',axis=(0,1,0),sides=16)
        self.loft(name+' armored mantlet',[(z-s*1.1,s*.82,s*.26,y+sign*s*.55),(z+s*.8,s,s*.4,y+sign*s*.55)],'plate')
        by=tip[1] if tip else y+sign*s*.56
        end=tip[2] if tip else z-s*4.0
        start=z-s*.8
        for side in (-1,1):
            bx=x+side*s*.28
            self.box(name+' rail shroud',(bx,by,(start+end)/2),(s*.22,s*.26,abs(start-end)),'dark',s*.025)
            for k in (-1,1): self.box(name+' conductor',(bx+k*s*.14,by,(start+end)/2),(s*.065,s*.17,abs(start-end)*.95),'edge',s*.015)
            for t in (.18,.48,.78):
                self.box(name+' accelerator clamp',(bx,by,start+(end-start)*t),(s*.48,s*.42,s*.25),'plate',s*.04)
            self.tube(name+' open muzzle',(bx,by,end+s*.15),s*.12,s*.3,'edge')
        self.box(name+' service hatch',(x,y+sign*s*.98,z+s*.25),(s*.65,s*.07,s*.6),'armor')
        for side in (-1,1):
            self.box(name+' lateral mechanism',(x+side*s*.89,y+sign*s*.52,z),(s*.16,s*.43,s*.75),'dark',s*.03)
            self.box(name+' armored hinge',(x+side*s*.98,y+sign*s*.50,z-s*.12),(s*.10,s*.2,s*.2),'copper',s*.02)
            self.box(name+' roof split panel',(x+side*s*.6,y+sign*s*.95,z+s*.05),(s*.26,s*.06,s*.8),'armor',s*.01)

    def point_defense(self,p,s,i):
        x,y,z=p; sign=1 if y>=0 else -1
        self.cylinder(f'PD {i} mount',(x,y,z),s,s*.4,'dark',axis=(0,1,0))
        self.box(f'PD {i} receiver',(x,y+sign*s*.65,z),(s*1.6,s*.9,s*1.8),'plate')
        for side in (-1,1): self.cylinder(f'PD {i} barrel',(x+side*s*.36,y+sign*s*.65,z-s*1.6),s*.15,s*2.5,'edge')
        self.box(f'PD {i} tracker',(x,y+sign*s*1.2,z-s*.45),(s*.6,s*.2,s*.35),'shield')

    def engine(self,p,r,plume,index):
        x,y,z=p
        self.loft(f'Engine {index} armored pod',[(z-r*2.5,r*1.13,r*1.13,y),(z-r*.7,r*1.25,r*1.25,y)],'armor') if x==0 else self.box(f'Engine {index} armored pod',(x,y,z-r*1.8),(r*2.3,r*2.3,r*2.7),'armor',r*.25)
        self.tube(f'Engine {index} outer bell',(x,y,z-r*.45),r,r*1.4,'edge')
        self.tube(f'Engine {index} lip',(x,y,z),r*1.03,r*.18,'plate')
        self.cylinder(f'Engine {index} core',(x,y,z-r*.3),r*.82,r*.08,'engine',sides=24)
        self.cylinder(f'Engine {index} hub',(x,y,z-r*.22),r*.26,r*.1,'dark')
        for i in range(8):
            t=i*math.tau/8
            self.box(f'Engine {index} cooling vane',(x+math.cos(t)*r,y+math.sin(t)*r,z-r*.75),(r*.15,r*.15,r*1.5),'copper',0)
        self.fx['engines'].append({'position':list(p),'radius':r,'plume':plume,'index':index})
        self.socket(f'fx_engine_{index}',p)

    def rcs(self,p,axis,s,plume):
        p=Vector(p); n=Vector(axis)
        i=len(self.fx['rcs'])
        self.box(f'RCS {i} block',p-n*s*.25,(s,s,s),'edge')
        self.tube(f'RCS {i} nozzle',p+n*s*.3,s*.34,s*.35,'dark',axis=n)
        self.cylinder(f'RCS {i} throat',p+n*s*.24,s*.23,s*.05,'copper',axis=n)
        self.fx['rcs'].append({'position':list(p),'exhaust':list(n),'size':s,'plume':plume})
        self.socket(f'fx_rcs_{i}',p,exhaust=list(n))

    def rcs_clusters(self,bow,stern,bh,sh,s,plume,lift=0):
        for z,(w,h) in ((bow,bh),(stern,sh)):
            for side in (-1,1):
                self.rcs((side*w,lift,z),(side,0,0),s,plume)
                for vert in (-1,1): self.rcs((side*w*.6,vert*h,z),(0,vert,0),s,plume)
        for side in (-1,1): self.rcs((side*(bh[0]+s*.5),lift,bow),(0,0,-1),s,plume)

    def radiator(self,c,half):
        x,y,z=c; w,h,d=half
        side=1 if x>0 else -1
        for zz in (-.6,.6):
            self.rod('Radiator coolant feed',(side*(abs(x)-w-8*h),y,z+zz*d),(side*(abs(x)+w*.8),y,z+zz*d),h*1.3,'edge')
        self.box('Radiator perimeter',c,(w*2,h*2,d*2),'edge',h*.3)
        self.box('Graphite heat exchanger',(x,y+h*1.1,z),(w*1.88,h*.6,d*1.88),'radiator',0)
        for i in range(14):
            zz=z-d*.88+i*d*1.76/13
            self.box('Radiator channels',(x,y+h*1.5,zz),(w*1.86,h*.18,d*.025),'copper',0)
        for side in (-1,1): self.box('Radiator reinforced spar',(x+side*w*.94,y,z),(w*.07,h*2.3,d*2),'plate',h*.15)

    def modules(self):
        for m in self.data['modules']:
            self.socket('module_'+m['id'].replace('-','_'),m['center'],module_id=m['id'],module_kind=m['kind'],half_size=m['halfSize'])
            obj=self.box('HIT VOLUME | '+m['id'],m['center'],[a*2 for a in m['halfSize']],'iff',0)
            self.coll.objects.unlink(obj); self.guides.objects.link(obj); self.meshes.remove(obj)
            obj.display_type='WIRE'; obj.hide_render=True; obj.hide_set(True)
            if m['kind']=='Cooling' and m['id'].startswith('radiator-'): self.radiator(m['center'],m['halfSize'])
        self.socket('socket_railgun_muzzle',self.data['railgun']['muzzle'])
        self.socket('socket_missile_launch',self.data['missiles']['launchPoint'])
        for i,p in enumerate(self.data['pointDefense']['mounts']):
            self.socket(f'socket_point_defense_{i}',p)

    def capital(self):
        bb=self.key=='battleship'; k=1 if bb else .3
        if bb:
            self.loft('Citadel continuous armored hull',[(-680,18,14,-10),(-620,40,29,-8),(-540,57,41,-5),(-440,65,42,0),(130,72,48,0),(470,73,49,0),(525,71,47,0)],'dark')
            self.loft('Raised gun deck',[(-450,28,9,57),(-330,46,13,64),(410,48,15,65)])
            self.loft('Ventral armored keel',[(-320,32,10,-65),(-180,52,14,-72),(370,57,15,-72)])
            w,h,start,end,step=84,58,-410,485,90
        else:
            self.loft('Escort armored hull',[(-185,6,5,-3),(-165,13,10,-2),(-120,19,12,0),(-85,22,14,0),(100,23,15,0),(114,22,14,0)],'dark')
            self.loft('Escort raised deck',[(-105,11,3,17),(-65,16,3,20),(95,17,3,20)])
            w,h,start,end,step=26,18,-100,113,25
        # Repeated, deliberately laid-out armor bays, inset seams and service ribs.
        count=int((end-start)/step)
        for i in range(count):
            z=start+(i+.5)*step
            wh=w*(.89+.11*(z-start)/(end-start))
            for side in (-1,1):
                self.box('Side recessed service trunk',(side*(wh-4*k),0,z),(12*k,h*1.35,step*.92),'dark')
                for row in (-1,1):
                    self.box('Armored side cassette',(side*wh,row*h*.30,z),(7*k,h*.55,step*.80),'plate' if i%3==0 else 'armor',1.1*k)
                    self.box('Cassette side inlay',(side*(wh+3.6*k),row*h*.3,z),(k,h*.32,step*.58),'dark',.1*k)
                    for q in (-1,1):
                        self.box('Cassette locking lug',(side*(wh+4.1*k),row*h*.3+q*h*.18,z+step*.28),(1.6*k,3*k,3*k),'copper',.3*k)
                self.box('Deck shoulder plate',(side*wh*.68,h*.87,z),(wh*.48,4*k,step*.81),'plate' if i%4==1 else 'armor',1.2*k)
                self.box('Ventral shoulder plate',(side*wh*.65,-h*.87,z),(wh*.51,4*k,step*.82),'armor',1.1*k)
                self.box('Transverse structural rib',(side*(wh-1*k),0,z-step*.47),(5*k,h*1.5,3*k),'edge',.4*k)
                self.rod('Exposed armored service pipe',(side*(wh+4.5*k),0,z-step*.35),(side*(wh+4.5*k),0,z+step*.32),1.3*k,'edge')
                for q in (-1,1):
                    self.box('Cassette hinge',(side*(wh+4.5*k),q*h*.3,z-step*.27),(1.8*k,5*k,8*k),'edge',.4*k)
                    self.box('Cassette inspection hatch',(side*(wh+4.5*k),q*h*.3,z-step*.05),(1.4*k,8*k,step*.19),'armor',.5*k)
                for q in (-1,0,1):
                    self.box('Service port',(side*(wh+3*k),h*.11,z+q*step*.20),(k,2*k,2*k),'amber',0)
            for side in (-1,1):
                self.box('Deck maintenance hatch',(side*w*.3,h+3*k,z),(w*.25,3*k,step*.45),'edge',.6*k)
                for j in range(4):
                    self.box('Vent louver',(side*w*.3,h+5*k,z-step*.13+j*step*.085),(w*.21,k,step*.025),'dark',0)
            # The broad raised deck is plated in offset sections, with a service spine.
            deck_y=81 if bb else 24
            self.box('Gun deck armor seam',(0,deck_y,z),(w*.93,2*k,step*.82),'plate' if i%4==1 else 'armor',k*.5)
            for side in (-1,1):
                self.box('Deck armored conduit',(side*w*.49,deck_y+1*k,z),(3*k,4*k,step*.83),'edge',.6*k)
                for q in (-1,1): self.box('Deck captive bolt',(side*w*.42,deck_y+1.2*k,z+q*step*.3),(2*k,k,2*k),'copper',.2*k)
                self.box('Deck equipment well',(side*w*.27,deck_y+1.4*k,z),(w*.23,1*k,step*.43),'dark',.4*k)
                self.box('Deck inset cover',(side*w*.27,deck_y+2.2*k,z-step*.055),(w*.19,1*k,step*.24),'armor',.3*k)
                for j in range(4): self.box('Deck intake grate',(side*w*.27,deck_y+2.1*k,z+step*(.105+j*.025)),(w*.19,.8*k,step*.012),'edge',0)
            if i%3==0: self.box('Deck service stripe',(w*.37,deck_y+1.5*k,z),(2*k,.5*k,step*.50),'iff',0)
        # Segmented bow plating follows the narrowing profile rather than floating blocks.
        bow=[(-604,42,30),(-530,59,43),(-456,71,49)] if bb else [(-170,12,9),(-142,18,12),(-117,22,15)]
        for z,bw,bh in bow:
            length=67 if bb else 21
            self.loft('Segmented prow armor',[(z-length*.5,bw*.85,bh*.90,-3*k),(z+length*.5,bw*1.03,bh*1.08,-3*k)],'armor')
            for side in (-1,1):
                self.box('Bow armor cheek',(side*bw*.74,-1,z),(bw*.5,4*k,(55 if bb else 18)),'plate',1*k)
                self.box('Bow deck spine',(side*bw*.28,bh-3*k,z),(bw*.40,4*k,(53 if bb else 17)),'armor',k)
                for j in range(3):
                    self.box('Bow segmented lateral plate',(side*bw*.99,-3*k,z+(j-1)*length*.24),(3*k,bh*.64,length*.21),'plate' if j==1 else 'armor',.7*k)
        # Layered bridge, armored sensor mast, ECM panels and shield emitters.
        by,bz=(105,294) if bb else (30,40)
        for j,(sx,sy,sz) in enumerate(((72,35,108),(60,26,77),(83,18,53),(36,27,37))):
            yy=by+j*25*k
            self.box('Command citadel tier '+str(j),(0,yy,bz-j*3*k),(sx*k,sy*k,sz*k),'plate' if j==2 else 'armor',3*k)
            if j==2:
                for ix in range(-4,5): self.box('Bridge armored viewport',(ix*7*k,yy+2*k,bz-sz*k*.5-k),(4*k,3*k,k),'glass',.2*k)
            for side in (-1,1):
                self.box('Citadel recessed facade',(side*(sx*.5+.3)*k,yy,bz-j*3*k),(k,sy*.64*k,sz*.76*k),'dark',.3*k)
                for q in (-1,1):
                    self.box('Citadel corner buttress',(side*(sx*.5+.6)*k,yy,bz-j*3*k+q*sz*.35*k),(2*k,sy*.84*k,3*k),'edge',.5*k)
                if j==2:
                    for q in range(5): self.box('Bridge flank viewport',(side*(sx*.5+1)*k,yy+2*k,bz-18*k+q*8*k),(k,3*k,4*k),'glass',.1*k)
        sensor=next(m for m in self.data['modules'] if m['id']=='sensor')['center']
        for side in (-1,1):
            self.box('Sensor phased array',(side*19*k,sensor[1],sensor[2]),(4*k,21*k,28*k),'dark',k)
            for j in range(4): self.box('ECM antenna elements',(side*21.2*k,sensor[1]-7*k+j*4.5*k,sensor[2]),(k,k,24*k),'edge',0)
        mast_y=by+100*k
        self.rod('Sensor main mast',(0,mast_y-25*k,bz),(0,mast_y+45*k,bz),1.2*k)
        for side in (-1,1):
            self.rod('Mast outriggers',(0,mast_y+3*k,bz),(side*19*k,mast_y+8*k,bz),k)
            self.rod('Antenna whip',(side*15*k,mast_y,bz),(side*15*k,mast_y+32*k,bz),.45*k)
            self.box('IFF pennant',(side*14*k,mast_y-6*k,bz),(6*k,7*k,3*k),'iff')
        for side in (-1,1):
            self.box('Shield projector armored housing',(side*w*.65,h+5*k,10*k),(13*k,10*k,35*k),'edge',2*k)
            for j in range(4): self.box('Shield emitter strips',(side*w*.65,h+10.2*k,-1*k+j*7*k),(9*k,k,2*k),'shield',0)
            for z in ((-180,350) if bb else (-45,84)):
                self.box('Shoulder module block',(side*w*.76,h*.76,z),(w*.39,15*k,25*k),'armor',3*k)
                for j in range(3): self.box('Shoulder module cooling slit',(side*w*.97,h*.76,z-7*k+j*7*k),(k,10*k,3*k),'dark',.3*k)
        guns=[m for m in self.data['modules'] if m['kind']=='Gun']
        for i,m in enumerate(guns):
            c=m['center']; c=[c[0],(88 if bb else 26) * (1 if c[1]>=0 else -1),c[2]]
            self.turret(c,30 if bb else 9, self.data['railgun']['muzzle'] if i==0 else None,c[1]<0, 'Rail battery '+m['id'])
        launch=self.data['missiles']['launchPoint']
        for side in (-1,1):
            for j in range(6 if bb else 4):
                x=side*(15 if bb else 7); z=launch[2]+(j-2.5)*(9 if bb else 5)
                self.box('Missile VLS coaming',(x,launch[1]-2*k,z),(11*k,4*k,7*k),'edge',k)
                self.box('Missile VLS armored lid',(x,launch[1]+.2*k,z),(9*k,k,5.7*k),'plate',.4*k)
                self.box('Missile hatch warning stripe',(x,launch[1]+.8*k,z),(6*k,.15*k,.6*k),'copper',0)
        for i,p in enumerate(self.data['pointDefense']['mounts']): self.point_defense(p,4 if bb else 1.5,i)
        # Docking and drone recesses give the long side faces useful landmarks.
        for side in (-1,1):
            z=130 if bb else 20
            self.box('Drone cradle recess',(side*(w+1*k),-h*.1,z),(3*k,26*k,54*k),'dark',k)
            for j in (-1,1):
                self.box('Drone cradle doors',(side*(w+3*k),-h*.1,z+j*18*k),(2*k,23*k,11*k),'plate',k)
                self.box('Dock service lamp',(side*(w+4.2*k),h*.08,z+j*21*k),(k,2*k,3*k),'amber',0)
            self.label('03' if bb else 'F7',(side*(w+3.2*k),-h*.25, -200 if bb else -65),12*k,'starboard' if side>0 else 'port')
            self.box('Faction identification band',(side*(w+3*k),h*.32,385 if bb else 105),(k,h*.55,12*k),'iff',0)
        if bb:
            for i,(x,y) in enumerate(((-48,-32),(48,-32),(-48,32),(48,32))): self.engine((x,y,580),24,420,i)
            self.rcs_clusters(-430,470,(76,51),(86,61),8,90)
        else:
            for i,x in enumerate((-14,14)): self.engine((x,0,134),10.5,150,i)
            self.rcs_clusters(-95,105,(22.5,15.5),(26.5,18.5),3,28)
        self.modules()

    def interceptor(self):
        self.loft('Armored central fuselage',[(-14.8,.75,.65,-.1),(-13,1.2,.9,0),(-7,1.7,1.2,0),(-2,2.05,1.4,0),(6,2.05,1.4,0),(10.5,2.1,1.35,0)])
        for side in (-1,1):
            pts=[(side*1.6,-.2,-5),(side*3.5,-.2,-3),(side*9.8,-.2,5),(side*9.4,-.2,8.5),(side*2.0,-.2,7)]
            verts=[(x,y+dy,z) for dy in (-.3,.3) for x,y,z in pts]
            faces=[tuple(reversed(range(5))),tuple(range(5,10))]+[(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)]
            self.mesh('Swept armored wing',verts,faces,'armor',.08)
            # Separate armor tiles make wing construction and panel seams readable.
            for j in range(4):
                x=side*(3+j*1.55); z=-.5+j*1.85
                self.box('Wing plate',(x,.17,z+2),(1.35,.15,3.8-j*.3),'plate' if j%2==0 else 'armor',.08)
                self.box('Wing leading edge reinforcement',(x,.05,z),(1.3,.3,.30),'edge',.04)
                self.box('Wing recessed panel',(x,.26,z+2),(.93,.03,2.3-j*.2),'dark',.01)
                self.box('Wing panel insert',(x,.285,z+1.85),(.84,.035,1.9-j*.2),'armor',.02)
                for dx in (-.49,.49):
                    for dz in (-.8,.8): self.box('Wing captive bolt',(x+dx,.295,z+2+dz),(.055,.06,.07),'copper',.01)
            self.box('Wingtip RCS pod',(side*9.3,-.05,6.6),(.9,.85,2.3),'edge',.14)
            for yy in (-.23,.23): self.cylinder('Wingtip maneuver jet',(side*9.3,yy,7.8),.18,.15,'engine')
            # Dorsal stabilizer, kept near the original collision volume.
            x=side*1.7
            self.mesh('Canted dorsal fin',[(x-.15,1.1,2),(x+.15,1.1,2),(x+.25,3.5,6.8),(x+.25,3.5,8.3),(x+.2,1.1,9),
                (x-.35,1.1,2),(x-.05,1.1,2),(x+.05,3.5,6.8),(x+.05,3.5,8.3),(x,1.1,9)],
                [(0,1,2,3,4),(9,8,7,6,5),(0,5,6,1),(1,6,7,2),(2,7,8,3),(3,8,9,4),(4,9,5,0)],'armor')
            self.box('Fin tip reinforcement',(side*1.85,3.45,7.4),(.4,.2,1.8),'plate',.04)
            for j in range(5):
                z=-4+j*2.7
                self.box('Fuselage shoulder armor',(side*1.65,1.1,z),(.72,.6,2.35),'plate' if j%3==0 else 'armor',.1)
                self.box('Cheek service block',(side*2.2,.0,z),(.32,1.1,1.95),'edge',.07)
                self.box('Fastener strip',(side*2.38,.15,z),(.06,.09,1.4),'copper',.015)
            self.box('Armored gun cheek',(side*.9,-.25,-12),(.55,.85,4.9),'plate',.12)
            for j in range(4):
                z=-12.7+j*1.3
                self.box('Prow cheek inspection plate',(side*(1.17+(j*.12)),.1,z),(.08,.45,.92),'armor',.02)
                self.box('Prow upper panel',(side*.40,.92+j*.10,z),(.62,.06,1.02),'armor',.025)
            self.box('Wing root equipment pod',(side*3.1,.48,5.5),(1.15,1.1,3.5),'armor',.16)
            self.box('Root equipment inset',(side*3.1,1.055,5.5),(.83,.08,2.7),'dark',.025)
            for j in range(5): self.box('Root equipment heat fin',(side*3.1,1.13,4.55+j*.45),(.65,.15,.17),'edge',.03)
            # Paired guns share the existing logical center muzzle.
            self.box('Forward gun rail',(side*.34,-.6,-14.4),(.35,.4,7.7),'dark',.04)
            self.cylinder('Forward rail sleeve',(side*.34,-.6,-16.3),.23,3.6,'edge')
            self.tube('Forward open muzzle',(side*.34,-.6,-18.25),.21,.30,'plate')
            for j in range(3): self.box('Gun accelerator collar',(side*.34,-.6,-12.4-j*1.7),(.48,.48,.45),'plate',.06)
            # Four light missiles / two bays; matches the existing four-round magazine.
            for j in range(2):
                x=side*(1.3+j*.55)
                self.cylinder('Micro missile body',(x,-1.87,-2),.19,2.7,'plate')
                self.cylinder('Micro missile seeker',(x,-1.87,-3.5),.03,.45,'dark',r2=.19)
                self.box('Missile rail bracket',(x,-1.58,-2),(.35,.45,2.4),'edge',.04)
            self.box('Aft heat sink',(side*2.15,.35,6),(.4,1.1,2.9),'dark',.08)
            for j in range(8): self.box('Heat sink fin',(side*2.38,.35,4.7+j*.36),(.12,.9,.08),'copper',.01)
            self.box('IFF wing band',(side*7.65,.27,5.5),(.48,.035,2.1),'iff',.01)
            self.label('07',(side*5.9,.38,4.6),.7)
        # Multi-facet, visibly framed canopy.
        self.loft('Cockpit pressure frame',[(-9.4,.38,.15,.85),(-6.9,.9,.55,1.3),(-3.6,.98,.5,1.45),(-2.8,.88,.2,1.2)],'edge')
        self.loft('Smoked canopy glazing',[(-9,.3,.1,1.17),(-6.8,.85,.44,1.66),(-3.7,.9,.4,1.75)],'glass')
        for z,w,y in [(-6.7,.64,2.10),(-4.7,.65,2.13),(-3.72,.66,2.15)]:
            self.rod('Canopy transverse frame',(-w,y,z),(w,y,z),.045,'edge')
        self.rod('Canopy center frame',(0,1.28,-8.8),(0,2.17,-3.7),.04,'edge')
        for j in range(4): self.box('Dorsal service armor',(0,1.64,.5+j*1.6),(2.1,.15,1.3),'armor',.04)
        self.box('Shield projector',(0,1.86,5),(.8,.4,1.45),'edge',.08)
        for j in range(3): self.box('Shield emitter',(0,2.07,4.55+j*.4),(.55,.04,.12),'shield',0)
        for i, p in enumerate(self.data['pointDefense']['mounts']): self.point_defense(p,.55,i)
        for i,x in enumerate((-1.1,1.1)): self.engine((x,0,12),.91,9,i)
        self.rcs_clusters(-11,7,(1.0,1.0),(2.3,1.6),.45,5,.7)
        self.modules()

    def lighting(self):
        scene=self.scene; L=self.data['flight']['length']
        world=bpy.data.worlds.new(self.key+' studio'); world.use_nodes=True
        world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.075,.11,1)
        world.node_tree.nodes['Background'].inputs[1].default_value=.25; scene.world=world
        def area(name,pos,power,color,scale):
            data=bpy.data.lights.new(name,'AREA'); data.energy=power*L*L; data.shape='DISK'; data.size=L*scale; data.color=color
            obj=bpy.data.objects.new(name,data); scene.collection.objects.link(obj); obj.location=g(Vector(pos)*L)
            obj.rotation_euler=(-obj.location).to_track_quat('-Z','Y').to_euler()
        area('Studio key',(.55,1,-.5),12,(.84,.91,1),.8)
        area('Warm rim',(-.5,.5,.3),9,(1,.68,.38),.6)
        area('Front fill',(.7,.45,-1),8,(.65,.79,1),.6)
        data=bpy.data.cameras.new('Hero camera'); camera=bpy.data.objects.new('Hero camera',data); scene.collection.objects.link(camera)
        camera.location=g(Vector((.86,.65,-1.03))*L); target=g((0,0,-L*.035))
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        data.type='ORTHO'; data.ortho_scale=L*1.27; data.clip_end=L*12; data.clip_start=L*.001
        scene.camera=camera; scene.render.engine='CYCLES'; scene.cycles.samples=24
        scene.cycles.use_denoising=True
        scene.render.resolution_x=1600; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
        scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
        scene.view_settings.view_transform='AgX'
        # Initial viewport: material colors and an oblique, fully framed editable ship.
        for screen in bpy.data.screens:
            for a in screen.areas:
                if a.type=='VIEW_3D':
                    a.spaces.active.clip_end=L*30
                    a.spaces.active.shading.color_type='MATERIAL'
                    a.spaces.active.region_3d.view_distance=L*1.4
                    a.spaces.active.region_3d.view_location=target
                    a.spaces.active.region_3d.view_rotation=camera.rotation_euler.to_quaternion()

    def export(self):
        fit_antimatter(self.coll,self.data)
        bpy.context.window.scene=self.scene
        rig_turrets(self.coll, self.data)
        from fleet_model_export import rig_point_defense, fit_interceptor_dorsal_pd
        fit_interceptor_dorsal_pd(self.coll, self.data)
        rig_point_defense(self.coll, self.data)
        stats = export_collection(self.coll, OUT/f'{self.key}.glb')
        self.lighting()
        self.scene['game_axes']='Forward -Z, up +Y; Godot meters'
        self.scene['module_source']=f'data/ships/{self.key}.json'
        for obj in self.sockets: obj.hide_set(True)
        bpy.ops.object.select_all(action='DESELECT')
        self.meshes[0].select_set(True); bpy.context.view_layer.objects.active=self.meshes[0]
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(ART/f'{self.key}.blend'),compress=True)
        if opt.render:
            self.scene.render.filepath=str(SHOTS/f'{self.key}_hero.png')
            bpy.ops.render.render(write_still=True)
        return {'id':self.key,**stats,
            'modules':len(self.data['modules']),'source':f'art/blender/{self.key}.blend','model':f'assets/ships/{self.key}.glb',**self.fx}

# This process starts with a factory scene; never touches another running Blender session.
manifest={'version':1,'coordinateSystem':'Godot meters, +Y up, -Z forward','ships':[]}
for key in ('battleship','escort','interceptor'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    initial=bpy.context.scene
    ship=Ship(key)
    bpy.data.scenes.remove(initial)
    ship.interceptor() if key=='interceptor' else ship.capital()
    manifest['ships'].append(ship.export())
    print('BUILT',key,manifest['ships'][-1]['triangles'],'triangles',flush=True)
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print('FLEET BUILD COMPLETE',flush=True)
