"""Shared Blender rig/export operations. Geometry remains editable in the source."""
import math
import bpy
from mathutils import Matrix, Vector

def g(p):
    return Vector((p[0], -p[2], p[1]))

def parent_keep_world(obj, parent):
    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_world = world

def rig_turrets(collection, definition):
    mounts = definition['railgun'].get('mounts', [])
    for mount in mounts:
        key = mount['moduleId'].replace('-', '_')
        if collection.all_objects.get('turret_' + key):
            continue
        prefix = 'Rail battery ' + mount['moduleId'] + ' '
        parts = [o for o in collection.all_objects if o.type == 'MESH' and o.name.startswith(prefix)]
        if not parts:
            raise ValueError(f'No authored parts found for {prefix}')
        def empty(name, pos, parent=None):
            obj = bpy.data.objects.new(name, None)
            collection.objects.link(obj)
            obj.empty_display_type = 'PLAIN_AXES'
            obj.empty_display_size = definition['flight']['length'] * .01
            obj.parent = parent
            obj.location = pos
            return obj
        yaw = empty('turret_' + key, g(mount['pivot']))
        yaw['module_id'] = mount['moduleId']
        # Godot Z rotation maps to Blender -Y; roll preserves local forward.
        if mount['ventral']: yaw.rotation_euler.y = math.pi
        pitch = empty('elevation_' + key, g(mount['trunnion']), yaw)
        recoil = empty('recoil_' + key, (0, 0, 0), pitch)
        bpy.context.view_layer.update()
        barrel_words = ('rail shroud', 'conductor', 'accelerator clamp', 'open muzzle')
        for part in parts:
            if 'bearing' in part.name:
                continue
            parent_keep_world(part, recoil if any(w in part.name for w in barrel_words) else yaw)
        for i, p in enumerate(mount['muzzles']):
            empty(f'muzzle_{key}_{i}', g(p), recoil)
    bpy.context.view_layer.update()

def export_collection(collection, output):
    """Batch static hull and each moving group separately; retain yaw/pitch/recoil hierarchy."""
    bpy.ops.object.select_all(action='DESELECT')
    graph = bpy.context.evaluated_depsgraph_get()
    meshes = [o for o in collection.all_objects if o.type == 'MESH']
    groups = {}
    for obj in meshes:
        groups.setdefault(obj.parent, []).append(obj)
    merged_objects = []
    triangles = 0
    for parent, parts in groups.items():
        bpy.ops.object.select_all(action='DESELECT')
        copies = []
        for obj in parts:
            mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(graph), depsgraph=graph)
            copy = bpy.data.objects.new('export part', mesh)
            bpy.context.scene.collection.objects.link(copy)
            copy.matrix_world = obj.matrix_world.copy()
            copy.select_set(True)
            copies.append(copy)
        bpy.context.view_layer.objects.active = copies[0]
        if len(copies) > 1: bpy.ops.object.join()
        merged = bpy.context.object
        merged.name = 'FleetHull' if parent is None else 'mesh_' + parent.name
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if parent is not None:
            merged.data.transform(parent.matrix_world.inverted())
            merged.parent = parent
            merged.matrix_parent_inverse = Matrix.Identity(4)
            merged.matrix_basis = Matrix.Identity(4)
        merged.data.calc_loop_triangles()
        triangles += len(merged.data.loop_triangles)
        merged_objects.append(merged)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in merged_objects: obj.select_set(True)
    empties = [o for o in collection.all_objects if o.type == 'EMPTY']
    hidden = [o for o in empties if o.hide_get()]
    for obj in empties:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', use_selection=True, use_active_scene=True,
        export_yup=True, export_extras=True, export_cameras=False, export_lights=False, export_animations=False,
        export_materials='EXPORT', export_texcoords=False, export_normals=True)
    for obj in merged_objects: bpy.data.objects.remove(obj, do_unlink=True)
    for obj in hidden: obj.hide_set(True)
    return {'triangles': triangles, 'materials': len({m.name for o in meshes for m in o.data.materials if m}),
        'editableParts': len(meshes), 'meshGroups': len(groups), 'turrets': len([o for o in empties if o.name.startswith('turret_')]),
        'pointDefenseTurrets': len([o for o in empties if o.name.startswith('pd_yaw_')])}

def rig_point_defense(collection, definition):
    """Keep fixed bearings in the hull; articulate existing PD receivers and barrels."""
    size = {'battleship': 4, 'escort': 1.5, 'interceptor': .55}[definition['id']]
    for i, p in enumerate(definition.get('pointDefense', {}).get('mounts', [])):
        if collection.all_objects.get(f'pd_yaw_{i}'):
            continue
        parts = [o for o in collection.all_objects if o.type == 'MESH' and o.name.startswith(f'PD {i} ')]
        if not parts:
            raise ValueError(f'Missing authored PD {i} parts')
        def empty(name, pos, parent=None):
            obj = bpy.data.objects.new(name, None)
            collection.objects.link(obj)
            obj.parent = parent
            obj.location = pos
            obj.empty_display_size = size
            return obj
        yaw = empty(f'pd_yaw_{i}', g(p))
        sign = -1 if p[1] < 0 else 1
        if sign < 0: yaw.rotation_euler.y = math.pi
        pitch = empty(f'pd_pitch_{i}', g((0, size*.65, 0)), yaw)
        bpy.context.view_layer.update()
        for part in parts:
            if ' mount' in part.name: continue
            parent_keep_world(part, yaw if ' receiver' in part.name else pitch)
        for barrel, side in enumerate((-1, 1)):
            empty(f'pd_muzzle_{i}_{barrel}', g((side*size*.36, 0, -size*2.85)), pitch)
    bpy.context.view_layer.update()

def fit_antimatter(collection, definition):
    am = definition.get('antimatter')
    if not am or collection.all_objects.get('socket_antimatter_launch'):
        return
    module = next(m for m in definition['modules'] if m['id'] == am['moduleId'])
    def marker(name, p):
        existing = collection.all_objects.get(name)
        if existing: return existing
        obj=bpy.data.objects.new(name,None); collection.objects.link(obj)
        obj.location=g(p); obj.empty_display_size=.3; obj.hide_set(True)
        return obj
    marker('module_am_containment',module['center'])
    marker('socket_antimatter_launch',am['flight']['launchPoint'])
    center=module['center']
    marker('fx_am_containment',(center[0],center[1]-1.85,center[2]))
    armor=next(m for m in bpy.data.materials if m.name.startswith('Armor - raised'))
    copper=next(m for m in bpy.data.materials if m.name.startswith('Thermal - copper'))
    for side in (-1,1):
        for z,depth,radius,material,label in [(0,2.8,.38,armor,'bottle'),(-1.15,.14,.43,copper,'fore ring'),(1.15,.14,.43,copper,'aft ring')]:
            bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=radius,depth=depth,
                location=g((center[0]+side*.38,center[1]-1.45,center[2]+z)),rotation=(math.pi/2,0,0))
            obj=bpy.context.object; obj.name=f'AM containment {side} {label}'
            for coll in list(obj.users_collection): coll.objects.unlink(obj)
            collection.objects.link(obj); obj.data.materials.append(material)
    guides=bpy.data.collections.get(f"{definition['id']} | module hit volumes (guides)")
    if guides and not guides.objects.get('HIT VOLUME | am-containment'):
        bpy.ops.mesh.primitive_cube_add(size=1,location=g(module['center']))
        obj=bpy.context.object; obj.name='HIT VOLUME | am-containment'
        obj.dimensions=g([v*2 for v in module['halfSize']]); obj.dimensions.y=abs(obj.dimensions.y)
        for coll in list(obj.users_collection): coll.objects.unlink(obj)
        guides.objects.link(obj); obj.display_type='WIRE'; obj.hide_render=True; obj.hide_set(True)
