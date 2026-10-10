"""Seat the authored fleet's armor and equipment without moving gameplay sockets/rig axes."""
import math
import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from fleet_model_export import g, parent_keep_world

def game(p): return Vector((p.x,p.z,-p.y))
def vertices(obj): return [game(obj.matrix_world @ v.co) for v in obj.data.vertices]
def bounds(obj):
    vs=vertices(obj)
    return Vector([min(v[i] for v in vs) for i in range(3)]),Vector([max(v[i] for v in vs) for i in range(3)])
def tree(objects):
    vs=[]; faces=[]
    for obj in objects:
        offset=len(vs);vs.extend(obj.matrix_world@v.co for v in obj.data.vertices)
        faces.extend([offset+i for i in p.vertices] for p in obj.data.polygons)
    return BVHTree.FromPolygons(vs,faces)

def align_ship_parts(collection, definition):
    if collection.get('attachment_revision',0)>=1:
        seat_markings(collection,definition)
        cut_elevation_wells(collection,definition)
        return
    key=definition['id']; capital=key!='interceptor'; k=1 if key=='battleship' else .3
    original=[o for o in collection.all_objects if o.type=='MESH']
    def named(prefix): return [o for o in collection.all_objects if o.type=='MESH' and o.name.startswith(prefix)]
    def material(prefix): return next(m for o in original for m in o.data.materials if m and m.name.startswith(prefix))
    armor=material('Armor - graphite');edge=material('Edge - brushed');dark=material('Recess - carbon')
    def box(name,c,size,mat=armor,bevel=0,parent=None):
        obj=collection.all_objects.get(name)
        if obj: return obj
        bpy.ops.mesh.primitive_cube_add(size=1,location=g(c))
        obj=bpy.context.object;obj.name=name;obj.dimensions=(size[0],size[2],size[1])
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        for coll in list(obj.users_collection):coll.objects.unlink(obj)
        collection.objects.link(obj);obj.data.materials.append(mat)
        if bevel:
            mod=obj.modifiers.new('Seated armor edge','BEVEL');mod.width=bevel;mod.segments=1
        if parent: parent_keep_world(obj,parent)
        return obj
    def shift(obj,delta):
        world=obj.matrix_world.copy();world.translation+=g(delta);obj.matrix_world=world
    def footing(obj,supports,label=None,ratio=.82):
        """A solid bracket joins the fixed fixture's inward face to an actual support ray hit."""
        lo,hi=bounds(obj);c=(lo+hi)*.5;sign=1 if c.y>=0 else -1
        inner=lo.y if sign>0 else hi.y
        start=Vector((c.x,inner-sign*.001,c.z));direction=Vector((0,-sign,0))
        bvh=tree(supports)
        closest,normal,_,_=bvh.find_nearest(g(start))
        if closest is not None and (g(start)-closest).dot(normal)<=.001: return
        hit,_,_,distance=bvh.ray_cast(g(start),g(direction),definition['flight']['length'])
        if hit is None: raise ValueError(f'{key}: no supporting surface for {obj.name}')
        end=game(hit).y
        if abs(inner-end)<.002: return
        low,high=sorted((inner,end));overlap=.06 if capital else .025
        box('Foundation | '+(label or obj.name),(c.x,(low+high)*.5,c.z),
            (max((hi.x-lo.x)*ratio,overlap*2),high-low+overlap*2,max((hi.z-lo.z)*ratio,overlap*2)),
            armor,min(overlap*.5,(high-low)*.1))
    def skin(obj,supports,axis,sign,clearance=0):
        """Seat a decorative plate on a sloped support, preserving its own thickness."""
        lo,hi=bounds(obj);inner=lo[axis] if sign>0 else hi[axis]
        bvh=tree(supports);inverse=obj.matrix_world.inverted();extent=definition['flight']['length']
        for v in obj.data.vertices:
            p=game(obj.matrix_world@v.co);start=p.copy();start[axis]+=sign*extent
            direction=Vector((0,0,0));direction[axis]=-sign
            hit,_,_,_=bvh.ray_cast(g(start),g(direction),extent*2)
            if hit is None: raise ValueError(f'{key}: decorative plate misses backing: {obj.name}')
            p[axis]=game(hit)[axis]+p[axis]-inner+sign*clearance
            v.co=inverse@g(p)
        obj.data.update()

    if capital:
        bb=key=='battleship'
        hull=named('Citadel continuous armored hull') if bb else named('Escort armored hull')
        # The old deck skin floated above its tapered substrate; fill inward, keeping the silhouette.
        deck=box('Continuous gun deck backing',(0,57.5,-5) if bb else (0,16.4,0),
            (89,45,810) if bb else (27.2,14.8,198),dark,.6*k)
        if bb:
            keel=box('Keel to citadel transition',(0,-52,25),(86,26,650),armor,1.2)
        else:
            keel=box('Ventral battery armored pedestal',(0,-19,-30),(18.5,14,22),armor,.5)
        structures=hull+[deck,keel]+named('Raised gun deck')+named('Escort raised deck')+named('Ventral armored keel')
        # Bridge tiers retain their authored heights; smaller collars close the unsupported gaps.
        tiers=sorted(named('Command citadel tier'),key=lambda o:bounds(o)[0].y)
        for tier in tiers:
            footing(tier,structures,'Bridge '+tier.name,ratio=.75)
            structures.append(tier)
        for mount in definition['railgun']['mounts']:
            prefix='Rail battery '+mount['moduleId']+' '
            if bb and mount['moduleId']=='gun-1':
                # This battery uses the low 6 m trunnion, not the other batteries' 16.8 m axis.
                for obj in named(prefix):
                    if obj.parent and obj.parent.name.startswith('turret_'): shift(obj,(0,-10.8,0))
            housing=named(prefix+'armored mantlet')[0]
            sign=-1 if mount['ventral'] else 1
            for obj in named(prefix+'service hatch')+named(prefix+'roof split panel'):
                skin(obj,[housing],1,sign,clearance=-.025*k)
            footing(named(prefix+'bearing')[0],structures,'Battery '+mount['moduleId'])
            # Carry the visible barrels all the way back to their elevation pivot, including at high pitch.
            rig=collection.all_objects['recoil_'+mount['moduleId'].replace('-','_')]
            s=30 if bb else 9; axis=Vector(mount['pivot'])+Vector((0,sign*mount['trunnion'][1],0))
            for side in (-1,1):
                box(prefix+'breech rail '+str(side),axis+Vector((side*s*.28,0,-s*.4)),
                    (s*.24,s*.28,s*.86),dark,s*.02,parent=rig)
            bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=s*.27,depth=s*1.82,
                location=g(axis),rotation=(0,math.pi/2,0))
            axle=bpy.context.object;axle.name=prefix+'trunnion axle'
            for coll in list(axle.users_collection):coll.objects.unlink(axle)
            collection.objects.link(axle);axle.data.materials.append(edge);parent_keep_world(axle,rig)
        # Structural plates connect to the actual octagonal hull, not just its bounding box.
        for obj in named('Ventral shoulder plate')+named('Deck shoulder plate'):
            footing(obj,hull,ratio=.78)
        structures+=named('Deck shoulder plate')+named('Ventral shoulder plate')+named('Shoulder module block')
        for obj in named('Shield projector armored housing')+named('Missile VLS coaming'):
            footing(obj,structures)
        for i in range(len(definition['pointDefense']['mounts'])):
            footing(named(f'PD {i} mount')[0],structures)
        for obj in named('Shoulder module cooling slit'):
            side=1 if sum(v.x for v in vertices(obj))>0 else -1
            skin(obj,named('Shoulder module block'),0,side,clearance=-.02*k)
        for pipe in named('Exposed armored service pipe'):
            lo,hi=bounds(pipe);c=(lo+hi)*.5
            for j,z in enumerate((lo.z+(hi.z-lo.z)*.15,hi.z-(hi.z-lo.z)*.15)):
                box('Pipe saddle | '+pipe.name+str(j),(c.x,0,z),(4*k,5*k,2.8*k),edge,.15*k)
        # Mast flags attach to their whip instead of hanging below it.
        for flag in named('IFF pennant'):
            lo,hi=bounds(flag);c=(lo+hi)*.5;side=1 if c.x>0 else -1
            box('Pennant mounting stem '+str(side),(side*15*k,c.y+4*k,c.z),(1*k,12*k,1*k),edge,.1*k)
        for obj in named('Missile hatch warning stripe'):
            skin(obj,named('Missile VLS armored lid'),1,1,clearance=-.015*k)
        for obj in named('Radiator channels'):
            skin(obj,named('Graphite heat exchanger'),1,1,clearance=-.015*k)
    else:
        hull=named('Armored central fuselage')
        fins=sorted(named('Canted dorsal fin'),key=lambda o:o.matrix_world.translation.x)
        left,right=fins
        inverse=left.matrix_world.inverted()
        for a,b in zip(left.data.vertices,right.data.vertices):
            p=right.matrix_world@b.co;p.x=-p.x;a.co=inverse@p
        bm=bmesh.new();bm.from_mesh(left.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(left.data);bm.free()
        deck=box('Dorsal equipment bedding',(0,1.46,3.05),(2.12,.36,7.8),armor,.025)
        for i in range(len(definition['pointDefense']['mounts'])):
            footing(named(f'PD {i} mount')[0],hull+[deck])
        for obj in named('Missile rail bracket'):
            footing(obj,hull+named('Swept armored wing'),ratio=.65)
        for obj in named('IFF wing band'):
            # Place the marking entirely on its outboard panel before conforming it.
            shift(obj,(0,0,1.2))
            skin(obj,named('Wing plate')+named('Wing panel insert'),1,1,clearance=.001)
        structures=hull+named('Swept armored wing')+named('Fuselage shoulder armor')+[deck]

    bpy.context.view_layer.update()
    # RCS nozzles and exhaust markers stay fixed. Add a short embedded bracket to the nearest hull face.
    structural_tree=tree(structures)
    for obj in named('RCS '):
        if not obj.name.endswith(' block'):continue
        lo,hi=bounds(obj);c=(lo+hi)*.5;hit,_,_,_=structural_tree.find_nearest(g(c))
        if hit is None: raise ValueError('No RCS support')
        end=game(hit);size=hi-lo
        if (end-c).length<size.x*.5:continue
        a=Vector([min(c[i],end[i]) for i in range(3)]);b=Vector([max(c[i],end[i]) for i in range(3)])
        box('RCS embedded bracket | '+obj.name,(a+b)*.5,b-a+size*.7,edge,min(size)*.05)
    # Stencils have zero thickness; project onto real armor faces with a tiny anti-z-fighting offset.
    supports=[o for o in collection.all_objects if o.type=='MESH' and not o.name.startswith('Stencil')]
    for obj in named('Stencil'):
        lo,hi=bounds(obj);c=(lo+hi)*.5
        skin(obj,supports,0 if capital else 1,(1 if c.x>0 else -1) if capital else 1,clearance=.005 if capital else .001)
    bpy.context.view_layer.update()
    collection['attachment_revision']=1
    seat_markings(collection,definition)
    cut_elevation_wells(collection,definition)

def seat_markings(collection,definition):
    """Keep lettering and IFF plates planar; a decal must not bridge multiple surface levels."""
    if collection.get('marking_revision',0)>=1:return
    meshes=[o for o in collection.all_objects if o.type=='MESH']
    stencils=[o for o in meshes if o.name.startswith('Stencil')]
    if definition['id']=='interceptor':
        for obj in stencils:
            inverse=obj.matrix_world.inverted();side=1 if obj.matrix_world.translation.x>0 else -1
            for vertex in obj.data.vertices:
                p=game(obj.matrix_world@vertex.co);p.x+=side*.15;p.z+=.4;p.y=.3045
                vertex.co=inverse@g(p)
        for obj in [o for o in meshes if o.name.startswith('IFF wing band')]:
            lo,hi=bounds(obj);center=(lo+hi)*.5;inverse=obj.matrix_world.inverted()
            for vertex in obj.data.vertices:
                p=game(obj.matrix_world@vertex.co)
                p.z=6.9+(p.z-center.z)/2.1
                p.y=.321+(.0175 if (vertex.index//2)%2 else -.0175)
                vertex.co=inverse@g(p)
    else:
        k=1 if definition['id']=='battleship' else .3
        support=tree([o for o in meshes if not o.name.startswith('Stencil')])
        mat=next(m for o in meshes for m in o.data.materials if m and m.name.startswith('Armor - graphite'))
        for obj in stencils:
            lo,hi=bounds(obj);center=(lo+hi)*.5;side=1 if center.x>0 else -1
            hits=[]
            for vertex in obj.data.vertices:
                p=game(obj.matrix_world@vertex.co);p.x=side*definition['flight']['length']
                hit,_,_,_=support.ray_cast(g(p),g((-side,0,0)),definition['flight']['length']*2)
                if hit is not None:hits.append(side*game(hit).x)
            inner=min(hits)-.12*k;outer=max(hits)+.08*k
            bpy.ops.mesh.primitive_cube_add(size=1,location=g((side*(inner+outer)*.5,center.y,center.z)))
            plaque=bpy.context.object;plaque.name='Identification plate | '+obj.name
            plaque.dimensions=(outer-inner,hi.z-lo.z+1.2*k,hi.y-lo.y+1.2*k)
            bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
            for coll in list(plaque.users_collection):coll.objects.unlink(plaque)
            collection.objects.link(plaque);plaque.data.materials.append(mat)
            inverse=obj.matrix_world.inverted()
            for vertex in obj.data.vertices:
                p=game(obj.matrix_world@vertex.co);p.x=side*(outer+.015*k);vertex.co=inverse@g(p)
    bpy.context.view_layer.update()
    collection['marking_revision']=1

def cut_elevation_wells(collection,definition):
    """Open the front/roof around the shared elevation axle; keep the fixed side cheeks and rear armor."""
    if definition['id']=='interceptor' or collection.get('elevation_clearance_revision',0)>=1:return
    s=30 if definition['id']=='battleship' else 9
    for mount in definition['railgun']['mounts']:
        sign=-1 if mount['ventral'] else 1
        pivot=Vector(mount['pivot']);axis=pivot+Vector((0,sign*mount['trunnion'][1],0))
        center=axis+Vector((0,sign*s*.53,-s*.51))
        bpy.ops.mesh.primitive_cube_add(size=1,location=g(center))
        cutter=bpy.context.object;cutter.name='Temporary elevation clearance'
        cutter.dimensions=(s*1.06,s*1.38,s*1.54)
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        housing=collection.all_objects['Rail battery '+mount['moduleId']+' armored mantlet']
        bpy.ops.object.select_all(action='DESELECT');housing.select_set(True);bpy.context.view_layer.objects.active=housing
        mod=housing.modifiers.new('Elevation swept opening','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(cutter,do_unlink=True)
        assert len(housing.data.polygons)>0,'Boolean removed the turret housing'
    bpy.context.view_layer.update()
    collection['elevation_clearance_revision']=1
