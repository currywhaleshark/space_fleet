"""Side-effect-free Blender primitives for independently authored blockouts.
Adapted from build-ship-models.py; no CLI parser, filesystem writes or generation at import.
"""

import json
import math
import bpy
import bmesh
from mathutils import Vector

def g(p):
    """Godot right/up/aft to Blender right/forward/up; glTF converts it back."""
    return Vector((p[0], -p[2], p[1]))

def material(name, color, metal=0.6, rough=0.45, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
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
    def __init__(self, root, key):
        self.key = key
        self.data = json.loads((root / 'data' / 'ships' / f'{key}.json').read_text(encoding='utf-8-sig'))
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

    def socket(self,name,p,**props):
        obj=bpy.data.objects.new(name,None); self.coll.objects.link(obj); obj.location=g(p)
        obj.empty_display_type='ARROWS'; obj.empty_display_size=self.data['flight']['length']*.008
        for k,v in props.items(): obj[k]=v
        self.sockets.append(obj)

    def point_defense(self,p,s,i):
        x,y,z=p; sign=1 if y>=0 else -1
        self.cylinder(f'PD {i} mount',(x,y,z),s,s*.4,'dark',axis=(0,1,0))
        self.box(f'PD {i} receiver',(x,y+sign*s*.65,z),(s*1.6,s*.9,s*1.8),'plate')
        for side in (-1,1): self.cylinder(f'PD {i} barrel',(x+side*s*.36,y+sign*s*.65,z-s*1.6),s*.15,s*2.5,'edge')
        self.box(f'PD {i} tracker',(x,y+sign*s*1.2,z-s*.45),(s*.6,s*.2,s*.35),'shield')

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
