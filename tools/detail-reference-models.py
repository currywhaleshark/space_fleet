"""Add surface-fitted secondary geometry to saved reference-silhouette models.

Blender --background --factory-startup --python-exit-code 1
  --python tools/detail-reference-models.py -- --root <repo> [--ship id,id]

Re-runnable: replaces only REFIT | objects. Original authored meshes, materials,
rigs, damage volumes and sockets are never regenerated. Static details merge into
FleetHull; moving details inherit the existing turret parent. No new FX sockets.
"""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import shutil
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

PREFIX = 'REFIT | '
VERSION = 1


def fingerprint(objects):
    """Exact original geometry/transforms/parenting, including every EMPTY marker."""
    h = hashlib.sha256()
    for obj in sorted(objects, key=lambda o: o.name):
        if obj.name.startswith(PREFIX):
            continue
        values = [obj.name, obj.type, obj.parent.name if obj.parent else None,
                  [list(row) for row in obj.matrix_world],
                  [list(row) for row in obj.matrix_parent_inverse]]
        if obj.type == 'MESH':
            values += [[list(v.co) for v in obj.data.vertices],
                       [list(f.vertices) for f in obj.data.polygons],
                       [m.name for m in obj.data.materials]]
        h.update(json.dumps(values).encode())
    return h.hexdigest()


class Buffer:
    def __init__(self):
        self.vertices, self.faces = [], []

    def solid(self, rings):
        count = len(rings[0])
        start = len(self.vertices)
        self.vertices += [tuple(p) for ring in rings for p in ring]
        self.faces.append(tuple(start + i for i in reversed(range(count))))
        for level in range(len(rings) - 1):
            a, b = start + level * count, start + (level + 1) * count
            self.faces += [(a+i, a+(i+1) % count, b+(i+1) % count, b+i) for i in range(count)]
        end = start + (len(rings)-1)*count
        self.faces.append(tuple(end + i for i in range(count)))


class Refit:
    def __init__(self, key, definition, coll):
        self.key, self.data, self.coll = key, definition, coll
        self.L = definition['flight']['length']
        self.small = any(s in key for s in ('interceptor', 'recon', 'assault'))
        self.mars = key.startswith('mars')
        self.buffers = defaultdict(Buffer)
        self.counts = Counter()
        self.anchors = []
        self.max_depth = self.L * (.0020 if self.small else .0040)
        self.panel_width = self.L * (.047 if self.small else .034)
        self.panel_length = self.L * (.072 if self.small else .058)
        self.originals = [o for o in coll.all_objects if not o.name.startswith(PREFIX)]
        self.keepouts = []
        for obj in self.originals:
            name = obj.name
            direction = None
            if name == 'socket_missile_launch':
                direction = (0,1,0) if self.small else (0,0,1)
            elif name in ('socket_railgun_muzzle','socket_antimatter_launch'):
                direction = (0,1,0)
            elif name.startswith('socket_probe_'):
                direction = (0,1,0) if 'recon' in key else (1 if obj.location.x>0 else -1,0,0)
            elif name.startswith('fx_engine_'):
                direction = (0,-1,0)
            elif name.startswith('fx_rcs_') and 'exhaust' in obj:
                x,y,z = obj['exhaust']
                direction = (x,-z,y)
            if direction is not None:
                self.keepouts.append((obj.matrix_world.translation.copy(),Vector(direction),self.L*.002))
        self.materials = {m.name: m for o in self.originals if o.type == 'MESH' for m in o.data.materials}
        self.armor = 'Armor - graphite titanium'
        self.plate = 'Armor - raised ceramic'
        self.edge = 'Edge - brushed alloy'
        self.dark = 'Recess - carbon'
        self.copper = 'Thermal - copper manifold'
        self.panel = 'Armor - machined panels'
        if self.panel not in bpy.data.materials:
            material = bpy.data.materials[self.armor].copy()
            material.name = self.panel
        self.materials[self.panel] = bpy.data.materials[self.panel]
        bpy.context.view_layer.update()
        # All evaluated original hardware participates in occlusion, not just hull
        # AABBs: sloping bows, wings, paint bands and existing manual edits survive.
        graph = bpy.context.evaluated_depsgraph_get()
        vertices, faces = [], []
        self.owners, self.object_surfaces = [], {}
        for obj in self.originals:
            if obj.type != 'MESH':
                continue
            evaluated = obj.evaluated_get(graph)
            mesh = evaluated.to_mesh()
            offset = len(vertices)
            local_vertices = [obj.matrix_world @ v.co for v in mesh.vertices]
            local_faces = [list(face.vertices) for face in mesh.polygons]
            self.object_surfaces[obj.name] = BVHTree.FromPolygons(local_vertices,local_faces)
            vertices += local_vertices
            faces += [[offset+i for i in face.vertices] for face in mesh.polygons]
            self.owners += [obj.name]*len(mesh.polygons)
            evaluated.to_mesh_clear()
        self.surface = BVHTree.FromPolygons(vertices, faces)

    @staticmethod
    def seed(p):
        # Reflecting an assembly across the centreline preserves its detail style.
        return int(hashlib.sha256(','.join(f'{abs(v):.3f}' for v in p).encode()).hexdigest()[:8], 16)

    def visible(self, p, n, owner):
        hit, normal, index, _ = self.surface.ray_cast(p+n*self.L*2, -n, self.L*2.01)
        return hit is not None and self.owners[index] == owner and (hit-p).length < self.L*.00004 and normal.dot(n) > .85

    def obstructs_port(self, samples, n):
        # Conservative bounds of the entire raised assembly against each actual
        # launch/nozzle ray. A clear socket must stay clear after decoration.
        reach = self.max_depth*3.1
        points = samples+[p+n*reach for p in samples]
        lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
        hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
        for origin,direction,radius in self.keepouts:
            near,far = 0.,self.L*2
            for axis in range(3):
                if abs(direction[axis]) < 1e-7:
                    if not lo[axis]-radius <= origin[axis] <= hi[axis]+radius:
                        far = -1
                        break
                else:
                    a = (lo[axis]-radius-origin[axis])/direction[axis]
                    b = (hi[axis]+radius-origin[axis])/direction[axis]
                    near,far = max(near,min(a,b)),min(far,max(a,b))
            if near <= far:
                return True
        return False

    def patch(self, parent, material, point, uv, depth, bevel=True):
        """Extrude a chamfered octagon along the face normal; base slightly embedded."""
        u0, v0, u1, v1 = uv
        cu, cv = (u1-u0)*.09, (v1-v0)*.09
        polygon = [(u0+cu,v0), (u1-cu,v0), (u1,v0+cv), (u1,v1-cv),
                   (u1-cu,v1), (u0+cu,v1), (u0,v1-cv), (u0,v0+cv)]
        samples = [point(u,v) for u,v in polygon]
        n = (samples[1]-samples[0]).cross(samples[3]-samples[0]).normalized()
        base = [p - n*self.L*.00006 for p in samples]
        if bevel:
            center = sum(samples, Vector())/8
            shoulder = [p+n*depth*.72 for p in samples]
            face = [center+(p-center)*.95+n*depth for p in samples]
            self.buffers[parent,material].solid([base, shoulder, face])
        else:
            self.buffers[parent,material].solid([base, [p+n*depth for p in samples]])

    def box(self, parent, material, center, u, v, n, width, length, height):
        # Low-poly flush equipment, lower edge embedded in its supporting surface.
        a, b = u*width/2, v*length/2
        ring = [center-a-b, center+a-b, center+a+b, center-a+b]
        self.buffers[parent,material].solid(
            [[p-n*self.L*.00007 for p in ring], [p+n*height for p in ring]])

    def equipment(self, obj, point, uv, depth, n, seed, mode):
        """Recess, framing and hinges belong to the panel, not arbitrary hull AABBs."""
        u0,v0,u1,v1 = uv
        du,dv = u1-u0,v1-v0
        sample = lambda a,b: point(u0+du*a,v0+dv*b)+n*depth
        c = sample(.5,.5)
        u = (sample(1,.5)-sample(0,.5)).normalized()
        v = n.cross(u).normalized()
        w = (sample(1,.5)-sample(0,.5)).length
        length = (sample(.5,1)-sample(.5,0)).length
        parent = obj.parent
        if mode == 'vent':
            # Heat-exchanger grille: raised backing, inset dark bed and louvers.
            self.box(parent,self.edge,c,u,v,n,w*.70,length*.72,depth*.40)
            c += n*depth*.42
            self.box(parent,self.dark,c,u,v,n,w*.59,length*.63,depth*.09)
            for i in range(5):
                p = c+v*length*(i-2)*.113+n*depth*.11
                self.box(parent,self.edge,p,u,v,n,w*.56,length*.032,depth*.23)
            self.counts['coolingGrilles'] += 1
        elif mode == 'hatch':
            self.box(parent,self.dark,c,u,v,n,w*.65,length*.70,depth*.11)
            self.box(parent,self.plate if seed%4 == 0 else self.edge,c+n*depth*.12,u,v,n,w*.54,length*.60,depth*.30)
            for s in (-1,1):
                self.box(parent,self.edge,c+u*w*.29+v*length*.19*s+n*depth*.14,u,v,n,w*.10,length*.08,depth*.38)
            self.box(parent,self.dark,c-u*w*.14+n*depth*.44,u,v,n,w*.04,length*.20,depth*.05)
            self.counts['accessHatches'] += 1
        elif mode == 'cabinet':
            # A low, bevelled service enclosure provides a larger middle-scale
            # silhouette break on broad decks without inventing a weapon socket.
            def face(a,b):
                return c+u*w*(a-.5)+v*length*(b-.5)
            self.patch(parent,self.edge,face,(.13,.17,.87,.83),depth*.6)
            self.patch(parent,self.panel,lambda a,b: face(a,b)+n*depth*.61,
                       (.20,.22,.80,.78),depth*.9)
            top=c+n*depth*1.52
            self.box(parent,self.dark,top,u,v,n,w*.36,length*.37,depth*.07)
            for i in range(3):
                self.box(parent,self.edge,top+v*length*(i-1)*.12+n*depth*.08,u,v,n,w*.34,length*.025,depth*.13)
            self.counts['serviceEnclosures'] += 1
        elif mode == 'conduit':
            # Two shielded service conduits with retaining clamps. Mars exposes
            # copper thermal lines; Earth uses enclosed alloy cable raceways.
            for s in (-1,1):
                p = c+u*w*.22*s
                self.box(parent,self.dark,p,u,v,n,w*.11,length*.82,depth*.25)
                self.box(parent,self.copper if self.mars else self.edge,p+n*depth*.24,u,v,n,w*.065,length*.77,depth*.30)
                for t in (-.27,.27):
                    self.box(parent,self.edge,p+v*length*t+n*depth*.54,u,v,n,w*.14,length*.06,depth*.15)
            self.counts['serviceConduits'] += 1
        else:
            # Sparse captive fasteners; avoid making every face equally busy.
            for a,b in ((.14,.20),(.86,.80)):
                self.box(parent,self.edge,sample(a,b),u,v,n,w*.065,length*.06,depth*.28)
            self.counts['fastenerPairs'] += 1

    def face(self, obj, poly, mat):
        if len(poly.vertices) != 4:
            return
        points = [obj.matrix_world @ obj.data.vertices[i].co for i in poly.vertices]
        a,b,c,d = points
        normal = (b-a).cross(d-a).normalized()
        width, length = ((b-a).length+(c-d).length)/2, ((d-a).length+(c-b).length)/2
        # Tiny bevels, thin edge faces, functional mounts and shallow trim stay clean.
        if min(width,length) < self.L*(.013 if self.small else .010):
            return
        longitudinal_u = abs((b-a).normalized().y) > abs((d-a).normalized().y)
        nu = max(1, round(width/(self.panel_length if longitudinal_u else self.panel_width)))
        nv = max(1, round(length/(self.panel_width if longitudinal_u else self.panel_length)))
        if any(s in obj.name.lower() for s in ('command','sensor protected')):
            # Smaller inspection panels fit below bridge optics without covering
            # the whole face (and consequently being rejected by the window).
            nu = max(2, math.ceil(width/(self.panel_width*.55)))
            nv = max(2, math.ceil(length/(self.panel_width*.55)))
        def point(u,v):
            p = a*(1-u)*(1-v)+b*u*(1-v)+c*u*v+d*(1-u)*v
            # Loft quads can be nonplanar. Project each point onto the actual
            # triangulated skin so attached panels do not float over tapering bows.
            hit, _, _, _ = self.object_surfaces[obj.name].ray_cast(p+normal*self.L, -normal, self.L*1.01)
            return hit if hit is not None else p
        for i in range(nu):
            for j in range(nv):
                u0,u1 = (i+.055)/nu, (i+.945)/nu
                v0,v1 = (j+.045)/nv, (j+.955)/nv
                center = point((u0+u1)/2,(v0+v1)/2)
                samples = [center,point(u0,v0),point(u1,v0),point(u1,v1),point(u0,v1)]
                if not all(self.visible(p,normal,obj.name) for p in samples):
                    continue
                if self.obstructs_port(samples,normal):
                    self.counts['portClearanceSkips'] += 1
                    continue
                # Clearance around all yaw/pitch mechanisms and fixed gun barrels.
                if any((center-o.matrix_world.translation).length < self.L*(.025 if self.small else .036)
                       for o in self.originals if o.type == 'EMPTY' and
                       (o.name.startswith('turret_') or o.name.startswith('pd_yaw_'))):
                    continue
                seed = self.seed(center)
                depth = self.max_depth*(.46+(seed%5)*.10)
                if abs(normal.z) < .25:
                    depth *= 1.15
                # Preserve faction paint bands, with neutral armoured plate fields.
                material = self.plate if mat == self.plate else self.panel
                self.patch(obj.parent,material,point,(u0,v0,u1,v1),depth)
                self.counts['armorPanels'] += 1
                self.anchors.append([list(center),list(normal),obj.name])
                # Keep slender wings and tails legible. Mechanical detail is
                # concentrated on engineering, broadside and command surfaces.
                name = obj.name.lower()
                is_wing = any(s in name for s in ('wing','fin','canard','tail','strake'))
                if is_wing or mat == self.plate:
                    if seed%5 == 0:
                        self.equipment(obj,point,(u0,v0,u1,v1),depth,normal,seed,'fastener')
                    continue
                slot = seed%12
                mode = 'vent' if slot < (3 if self.mars else 2) else 'hatch' if slot < 5 else 'conduit' if slot < (8 if self.mars else 6) else 'cabinet' if slot == 8 and abs(normal.z) > .6 and not self.small else 'fastener'
                if slot < 10:
                    self.equipment(obj,point,(u0,v0,u1,v1),depth,normal,seed,mode)

    def radiator(self, obj, poly):
        if len(poly.vertices) != 4:
            return
        a,b,c,d = [obj.matrix_world @ obj.data.vertices[i].co for i in poly.vertices]
        n = (b-a).cross(d-a).normalized()
        width,length = (b-a).length,(d-a).length
        if min(width,length) < self.L*.009:
            return
        center = (a+b+c+d)/4
        u,v = (b-a).normalized(),n.cross((b-a).normalized()).normalized()
        # A physical fin bank, recessed between the existing radiator frame.
        count = min(14,max(4,round(length/(self.L*.008))))
        added = 0
        for i in range(count):
            for column in (-1,0,1):
                p = center+v*length*((i+.5)/count-.5)*.88+u*width*column*.29
                samples = [p,p-u*width*.13,p+u*width*.13]
                if all(self.visible(sample,n,obj.name) for sample in samples) and not self.obstructs_port(samples,n):
                    self.box(obj.parent,self.dark,p,u,v,n,width*.27,length/count*.48,self.L*.00045)
                    added += 1
        for s in (-1,1):
            p = center+u*width*.43*s
            if self.visible(p,n,obj.name):
                self.box(obj.parent,self.copper,p,u,v,n,width*.025,length*.92,self.L*.0006)
        if added:
            self.counts['radiatorBanks'] += 1

    def build(self):
        for obj in self.originals:
            if obj.type != 'MESH' or not obj.data.materials:
                continue
            mat = obj.data.materials[0].name
            if mat == 'Thermal - graphite fins':
                for poly in obj.data.polygons:
                    self.radiator(obj,poly)
                continue
            if mat not in (self.armor,self.plate):
                continue
            # Never cover optics, ports, gun slides, launcher covers or their openings.
            if any(x in obj.name.lower() for x in ('clamp','muzzle','launcher','launch','hatch','coaming','missile rack','probe','glazing','bearing','turntable','projector','lens','iff','rcs')):
                continue
            if self.small and 'missile cassette' in obj.name.lower():
                continue
            for poly in obj.data.polygons:
                self.face(obj,poly,mat)
        for (parent,material),buf in self.buffers.items():
            name = PREFIX+(parent.name if parent else 'hull')+' | '+material
            mesh = bpy.data.meshes.new(name)
            # Store local coordinates in moving nodes, identity transform for static hull.
            inv = parent.matrix_world.inverted() if parent else None
            vertices = [inv@Vector(v) for v in buf.vertices] if inv else buf.vertices
            mesh.from_pydata(vertices,[],buf.faces)
            mesh.update()
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
            bm.to_mesh(mesh)
            bm.free()
            obj = bpy.data.objects.new(name,mesh)
            self.coll.objects.link(obj)
            obj.parent = parent
            obj.data.materials.append(self.materials[material])
            obj['refit_version'] = VERSION
        bpy.context.view_layer.update()
        triangles = sum(sum(len(f)-2 for f in b.faces) for b in self.buffers.values())
        # Guard against accidentally subdividing a model into an unbounded grid.
        assert triangles < (26000 if self.small else 42000), (self.key,triangles)
        return dict(version=VERSION,addedTriangles=triangles,groups=len(self.buffers),
                    assemblies=dict(self.counts),verifiedSurfaceAnchors=len(self.anchors))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root',required=True)
    parser.add_argument('--ship',help='comma separated model IDs; default all authored layout hulls')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    root = Path(args.root)
    sys.path.insert(0,str(root/'tools'))
    from branch_model_layout import resolve
    from fleet_model_export import export_collection
    manifest_path = root/'assets/ships/manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    keys = args.ship.split(',') if args.ship else [e['id'] for e in manifest['ships'] if (root/'data/model_layouts'/f"{e['id']}.json").exists()]
    output = root/'docs/validation/model_detail_20261020'
    output.mkdir(parents=True,exist_ok=True)
    backup = root/'shots/detail-pass/source-backup'
    backup.mkdir(parents=True,exist_ok=True)
    for key in keys:
        entry = next(e for e in manifest['ships'] if e['id'] == key)
        source = root/entry['source']
        if not (backup/source.name).exists():
            shutil.copy2(source,backup/source.name)
        bpy.ops.wm.open_mainfile(filepath=str(source))
        coll = bpy.data.collections[key+' | editable parts']
        before = fingerprint(coll.all_objects)
        for obj in list(coll.all_objects):
            if obj.name.startswith(PREFIX):
                mesh = obj.data
                bpy.data.objects.remove(obj,do_unlink=True)
                if mesh.users == 0:
                    bpy.data.meshes.remove(mesh)
        refit = Refit(key,resolve(root,key),coll)
        report = refit.build()
        after = fingerprint(coll.all_objects)
        assert before == after, key+': original geometry or socket changed'
        report.update(id=key,originalGeometryHash=before,originalGeometryPreserved=True)
        # Leave editable assemblies in the .blend; merge only in the export copy.
        bpy.context.preferences.filepaths.save_version = 0
        if hasattr(bpy.context.preferences.filepaths,'file_preview_type'):
            bpy.context.preferences.filepaths.file_preview_type = 'NONE'
        bpy.ops.wm.save_as_mainfile(filepath=str(source))
        old_groups = entry['meshGroups']
        entry.update(export_collection(coll,root/entry['model']))
        assert entry['meshGroups'] == old_groups, key+': draw groups changed'
        entry['surfaceDetail'] = dict(version=VERSION,panels=report['assemblies']['armorPanels'])
        report.update(triangles=entry['triangles'],meshGroups=entry['meshGroups'],materials=entry['materials'])
        manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
        (output/f'{key}.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
        print('DETAILED',key,json.dumps(report),flush=True)


if __name__ == '__main__':
    main()
