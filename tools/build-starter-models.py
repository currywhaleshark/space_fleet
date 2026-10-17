"""Derive separate, editable T1 hulls from the current manually edited escort sources.
blender --background --python tools/build-starter-models.py -- --root <repo>
Original six sources/GLBs are read-only. Local geometry and all rig/socket frames scale together.
"""
import argparse,copy,json,sys
from pathlib import Path
import bpy
from mathutils import Vector

parser=argparse.ArgumentParser();parser.add_argument('--root',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);root=Path(args.root)
if any((root/f'data/model_layouts/{family}_starter_escort.json').exists() for family in ('earth','mars')):
    raise SystemExit('T1 has independent authored geometry. Use build-t1-starter-models.py; after manual editing use export-ship-models.py. Scaled originals are archived.')
sys.path.insert(0,str(root/'tools'))
from fleet_model_export import export_collection
manifest_path=root/'assets/ships/manifest.json';manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
for design,base in [('earth','escort'),('mars','mars_escort')]:
    key=design+'_starter_escort';factor=.8
    entry=copy.deepcopy(next(e for e in manifest['ships'] if e['id']==base))
    definition=json.loads((root/f'data/variants/{key}.json').read_text(encoding='utf-8'))['patch']
    bpy.ops.wm.open_mainfile(filepath=str(root/entry['source']))
    collection=bpy.data.collections[f'{base} | editable parts'];collection.name=f'{key} | editable parts'
    # Remove the complete ventral battery, including its fixed bearing and hit-volume guides.
    removed=set()
    for obj in bpy.data.objects:
        if 'gun_2' in obj.name or 'gun-2' in obj.name:
            removed.add(obj.name)
            removed.update(child.name for child in obj.children_recursive)
    for name in removed:
        bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
    objects=list(bpy.data.objects)
    old={obj:obj.matrix_world.copy() for obj in objects}
    def depth(obj):
        n=0
        while obj.parent is not None:n+=1;obj=obj.parent
        return n
    for obj in sorted(objects,key=depth):
        if obj.type=='MESH':
            obj.data=obj.data.copy()
            for vertex in obj.data.vertices:vertex.co*=factor
            for modifier in obj.modifiers:
                if modifier.type=='BEVEL':modifier.width*=factor
        matrix=old[obj].copy();matrix.translation*=factor;obj.matrix_world=matrix
        if obj.type=='EMPTY':obj.empty_display_size*=factor
        bpy.context.view_layer.update()
    def godot(obj):
        p=obj.matrix_world.translation;return [p.x,p.z,-p.y]
    for module in definition['modules']:
        obj=collection.all_objects['module_'+module['id'].replace('-','_')]
        assert (Vector(godot(obj))-Vector(module['center'])).length<.001,(key,module['id'])
    for mount in definition['railgun']['mounts']:
        part=mount['moduleId'].replace('-','_')
        assert (Vector(godot(collection.all_objects['turret_'+part]))-Vector(mount['pivot'])).length<.001
    for engine in entry['engines']:
        engine['position']=godot(collection.all_objects[f'fx_engine_{engine["index"]}'])
        engine['radius']*=factor;engine['plume']*=factor
    for i,jet in enumerate(entry['rcs']):
        jet['position']=godot(collection.all_objects[f'fx_rcs_{i}'])
        for field in ['radius','plume']:
            if field in jet:jet[field]*=factor
    entry.update(id=key,source=f'art/blender/{key}.blend',model=f'assets/ships/{key}.glb',modules=len(definition['modules']))
    entry.update(export_collection(collection,root/entry['model']))
    bpy.context.scene['module_source']=f'data/variants/{key}.json';bpy.context.scene['derived_from']=base
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':area.spaces.active.region_3d.view_distance*=factor;area.spaces.active.region_3d.view_location*=factor
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(root/entry['source']),compress=True)
    manifest['ships']=[e for e in manifest['ships'] if e['id']!=key]+[entry]
    print('STARTER',key,entry['triangles'],'triangles',entry['turrets'],'turret',flush=True)
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
